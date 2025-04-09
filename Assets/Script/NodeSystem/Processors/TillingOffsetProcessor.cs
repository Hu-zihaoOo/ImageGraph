using UnityEngine;
using NodeImageEditor.Interfaces;

namespace NodeImageEditor.Processors
{
    public class TillingOffsetProcessor : IImageProcessor
    {
        private ComputeShader imageProcessingShader;
        private int tillingOffsetKernelIndex;

        public TillingOffsetProcessor()
        {
            imageProcessingShader = Resources.Load<ComputeShader>("ImageProcessingShader");
            tillingOffsetKernelIndex = imageProcessingShader.FindKernel("TillingOffset");
        }

        public Texture2D ProcessTexture(Texture2D inputTexture, object parameters)
        {
            if (inputTexture == null || parameters == null)
                return null;

            TillingOffsetParameters tillingParams = parameters as TillingOffsetParameters;
            if (tillingParams == null)
                return null;

            Texture2D resultTexture = new Texture2D(inputTexture.width, inputTexture.height);
            Color[] pixels = inputTexture.GetPixels();
            Color[] resultPixels = new Color[pixels.Length];

            for (int y = 0; y < inputTexture.height; y++)
            {
                for (int x = 0; x < inputTexture.width; x++)
                {
                    // 计算UV坐标
                    float u = (float)x / inputTexture.width;
                    float v = (float)y / inputTexture.height;

                    // 应用Tilling和Offset
                    u = (u * tillingParams.TillingX) + tillingParams.OffsetX;
                    v = (v * tillingParams.TillingY) + tillingParams.OffsetY;

                    // 重复采样
                    u = Mathf.Repeat(u, 1.0f);
                    v = Mathf.Repeat(v, 1.0f);

                    // 转换回像素坐标
                    int sx = Mathf.FloorToInt(u * inputTexture.width);
                    int sy = Mathf.FloorToInt(v * inputTexture.height);

                    // 获取采样颜色
                    resultPixels[y * inputTexture.width + x] = pixels[sy * inputTexture.width + sx];
                }
            }

            resultTexture.SetPixels(resultPixels);
            resultTexture.Apply();

            return resultTexture;
        }

        public void ProcessPreview(RenderTexture input, RenderTexture output, object parameters)
        {
            if (imageProcessingShader == null || parameters == null)
                return;

            TillingOffsetParameters tillingParams = parameters as TillingOffsetParameters;
            if (tillingParams == null)
                return;

            imageProcessingShader.SetTexture(tillingOffsetKernelIndex, "InputTexture", input);
            imageProcessingShader.SetTexture(tillingOffsetKernelIndex, "Result", output);
            imageProcessingShader.SetFloat("TillingX", tillingParams.TillingX);
            imageProcessingShader.SetFloat("TillingY", tillingParams.TillingY);
            imageProcessingShader.SetFloat("OffsetX", tillingParams.OffsetX);
            imageProcessingShader.SetFloat("OffsetY", tillingParams.OffsetY);

            int threadGroupsX = Mathf.CeilToInt(output.width / 8.0f);
            int threadGroupsY = Mathf.CeilToInt(output.height / 8.0f);
            imageProcessingShader.Dispatch(tillingOffsetKernelIndex, threadGroupsX, threadGroupsY, 1);
        }
    }
} 