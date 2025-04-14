using UnityEngine;
using NodeImageEditor.Interfaces;

namespace NodeImageEditor.Processors
{
    public class BlendProcessor : IImageProcessor
    {
        private ComputeShader imageProcessingShader;
        private int blendKernelIndex;

        public BlendProcessor()
        {
            imageProcessingShader = Resources.Load<ComputeShader>("ImageProcessingShader");
            if (imageProcessingShader == null)
            {
                Debug.LogError("Failed to load ImageProcessingShader in BlendProcessor");
                return;
            }
            
            blendKernelIndex = imageProcessingShader.FindKernel("BlendTextures");
            Debug.Log($"BlendProcessor initialized with kernel index: {blendKernelIndex}");
        }

        public Texture2D ProcessTexture(Texture2D inputTexture, object parameters)
        {
            return null; // 需要两个输入贴图，所以在Node类中处理
        }

        public Texture2D ProcessTextures(Texture2D inputTexture1, Texture2D inputTexture2, object parameters)
        {
            if (inputTexture1 == null || inputTexture2 == null || parameters == null)
                return null;

            BlendParameters blendParams = parameters as BlendParameters;
            if (blendParams == null)
                return null;

            Debug.Log($"ProcessTextures: BlendMode={blendParams.Mode}, Strength={blendParams.Strength}");
            
            // 使用GPU计算着色器处理混合
            if (imageProcessingShader != null && SystemInfo.supportsComputeShaders)
            {
                try
                {
                    // 创建临时RenderTexture
                    RenderTexture tempRT1 = RenderTexture.GetTemporary(
                        inputTexture1.width, inputTexture1.height, 0, RenderTextureFormat.DefaultHDR);
                    tempRT1.enableRandomWrite = true;
                    tempRT1.Create();
                    
                    RenderTexture tempRT2 = RenderTexture.GetTemporary(
                        inputTexture2.width, inputTexture2.height, 0, RenderTextureFormat.DefaultHDR);
                    tempRT2.enableRandomWrite = true;
                    tempRT2.Create();
                    
                    // 复制输入纹理到RenderTexture
                    Graphics.Blit(inputTexture1, tempRT1);
                    Graphics.Blit(inputTexture2, tempRT2);
                    
                    // 创建输出RenderTexture
                    RenderTexture resultRT = RenderTexture.GetTemporary(
                        inputTexture1.width, inputTexture1.height, 0, RenderTextureFormat.DefaultHDR);
                    resultRT.enableRandomWrite = true;
                    resultRT.Create();
                    
                    // 使用计算着色器处理混合
                    ProcessPreview(tempRT1, tempRT2, resultRT, blendParams);
                    
                    // 创建结果纹理
                    Texture2D gpuResultTexture = new Texture2D(inputTexture1.width, inputTexture1.height, TextureFormat.RGBA32, false);
                    RenderTexture.active = resultRT;
                    gpuResultTexture.ReadPixels(new Rect(0, 0, resultRT.width, resultRT.height), 0, 0);
                    gpuResultTexture.Apply();
                    RenderTexture.active = null;
                    
                    // 释放临时资源
                    RenderTexture.ReleaseTemporary(tempRT1);
                    RenderTexture.ReleaseTemporary(tempRT2);
                    RenderTexture.ReleaseTemporary(resultRT);
                    
                    Debug.Log("GPU blend processing completed successfully");
                    return gpuResultTexture;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error in GPU blend processing: {e.Message}");
                    // 失败时回退到CPU处理
                }
            }
            
            Debug.Log("Falling back to CPU blend processing");
            
            // CPU回退处理（现有的像素处理方法）
            // 创建目标纹理
            Texture2D resultTexture = new Texture2D(inputTexture1.width, inputTexture1.height, TextureFormat.RGBA32, false);

            // 获取输入纹理的像素
            Color[] pixels1 = inputTexture1.GetPixels();
            Color[] pixels2 = inputTexture2.GetPixels();
            Color[] resultPixels = new Color[pixels1.Length];

            // 手动进行混合操作
            for (int i = 0; i < pixels1.Length; i++)
            {
                Color color1 = pixels1[i];
                
                // 确保索引在范围内
                Color color2 = i < pixels2.Length ? pixels2[i] : Color.black;

                // 根据混合模式处理
                if (blendParams.Mode == BlendMode.Multiply)
                {
                    resultPixels[i] = new Color(
                        color1.r * color2.r,
                        color1.g * color2.g,
                        color1.b * color2.b,
                        color1.a * color2.a
                    );
                }
                else // Add模式
                {
                    resultPixels[i] = new Color(
                        Mathf.Clamp01(color1.r + color2.r * blendParams.Strength),
                        Mathf.Clamp01(color1.g + color2.g * blendParams.Strength),
                        Mathf.Clamp01(color1.b + color2.b * blendParams.Strength),
                        Mathf.Clamp01(color1.a + color2.a * blendParams.Strength)
                    );
                }
            }

            resultTexture.SetPixels(resultPixels);
            resultTexture.Apply();

            return resultTexture;
        }

        public void ProcessPreview(RenderTexture input, RenderTexture output, object parameters)
        {
            // 在BlendNode中会调用特定的双输入处理方法
            Graphics.Blit(input, output);
        }

        public void ProcessPreview(RenderTexture input1, RenderTexture input2, RenderTexture output, object parameters)
        {
            if (imageProcessingShader == null)
            {
                Debug.LogError("ImageProcessingShader is null in BlendProcessor.ProcessPreview");
                Graphics.Blit(input1, output);
                return;
            }

            if (parameters == null)
            {
                Debug.LogError("Parameters are null in BlendProcessor.ProcessPreview");
                Graphics.Blit(input1, output);
                return;
            }

            BlendParameters blendParams = parameters as BlendParameters;
            if (blendParams == null)
            {
                Debug.LogError("Invalid parameters type in BlendProcessor.ProcessPreview");
                Graphics.Blit(input1, output);
                return;
            }

            try
            {
                Debug.Log($"Setting up compute shader for blending with mode={blendParams.Mode}, strength={blendParams.Strength}");
                
                // 设置计算着色器参数
                imageProcessingShader.SetTexture(blendKernelIndex, "InputTexture", input1);
                imageProcessingShader.SetTexture(blendKernelIndex, "SecondTexture", input2);
                imageProcessingShader.SetTexture(blendKernelIndex, "Result", output);
                imageProcessingShader.SetInt("BlendMode", (int)blendParams.Mode);
                imageProcessingShader.SetFloat("BlendStrength", blendParams.Strength);

                // 计算线程组数量
                int threadGroupsX = Mathf.CeilToInt(output.width / 8.0f);
                int threadGroupsY = Mathf.CeilToInt(output.height / 8.0f);
                
                Debug.Log($"Dispatching compute shader with thread groups: {threadGroupsX}x{threadGroupsY}");
                
                // 执行计算着色器
                imageProcessingShader.Dispatch(blendKernelIndex, threadGroupsX, threadGroupsY, 1);
                
                Debug.Log("Compute shader dispatched successfully");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error dispatching compute shader: {e.Message}");
                // 出错时使用简单的混合处理
                Graphics.Blit(input1, output);
            }
        }
    }
} 