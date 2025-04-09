using UnityEngine;
using System.Collections.Generic;

namespace NodeImageEditor
{
    public class BlurNode : BaseNode
    {
        public int BlurRadius = 1;
        
        private ComputeShader imageProcessingShader;
        
        public BlurNode() : base()
        {
            Name = "Blur";
            imageProcessingShader = Resources.Load<ComputeShader>("ImageProcessingShader");
        }
        
        public override Texture2D ProcessTexture(Dictionary<string, BaseNode> nodeMap)
        {
            Texture2D inputTexture = GetInputTexture(0, nodeMap);
            if (inputTexture == null)
            {
                return null;
            }
            
            // Create a new texture for the result
            Texture2D resultTexture = new Texture2D(inputTexture.width, inputTexture.height);
            
            // Apply blur
            Color[] pixels = inputTexture.GetPixels();
            Color[] resultPixels = new Color[pixels.Length];
            
            for (int y = 0; y < inputTexture.height; y++)
            {
                for (int x = 0; x < inputTexture.width; x++)
                {
                    Color sum = Color.black;
                    int count = 0;
                    
                    for (int ky = -BlurRadius; ky <= BlurRadius; ky++)
                    {
                        for (int kx = -BlurRadius; kx <= BlurRadius; kx++)
                        {
                            int nx = x + kx;
                            int ny = y + ky;
                            
                            if (nx >= 0 && nx < inputTexture.width && ny >= 0 && ny < inputTexture.height)
                            {
                                sum += pixels[ny * inputTexture.width + nx];
                                count++;
                            }
                        }
                    }
                    
                    resultPixels[y * inputTexture.width + x] = sum / count;
                }
            }
            
            resultTexture.SetPixels(resultPixels);
            resultTexture.Apply();
            
            // Cache the preview texture
            previewTexture = resultTexture;
            
            return resultTexture;
        }

        public void SetPreviewTexture(RenderTexture texture)
        {
            if (texture != null)
            {
                previewTexture = new Texture2D(texture.width, texture.height);
                RenderTexture.active = texture;
                previewTexture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                previewTexture.Apply();
                RenderTexture.active = null;
            }
        }

        public override void ProcessPreview(RenderTexture input, RenderTexture output, Dictionary<string, BaseNode> nodeMap)
        {
            if (imageProcessingShader == null)
                return;

            int kernelIndex = imageProcessingShader.FindKernel("BlurEffect");
            imageProcessingShader.SetTexture(kernelIndex, "InputTexture", input);
            imageProcessingShader.SetTexture(kernelIndex, "Result", output);
            imageProcessingShader.SetInt("BlurRadius", BlurRadius);

            int threadGroupsX = Mathf.CeilToInt(output.width / 8.0f);
            int threadGroupsY = Mathf.CeilToInt(output.height / 8.0f);
            imageProcessingShader.Dispatch(kernelIndex, threadGroupsX, threadGroupsY, 1);
        }
    }
} 