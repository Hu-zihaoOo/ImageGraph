using UnityEngine;
using System.Collections.Generic;

namespace NodeImageEditor
{
    public class ColorAdjustmentNode : BaseNode
    {
        [Range(0, 2)] public float Brightness = 1.0f;
        [Range(0, 2)] public float Contrast = 1.0f;
        [Range(0, 2)] public float Saturation = 1.0f;
        [Range(0, 1)] public float Hue = 0.0f;

        [Range(0, 1)] public float aaaa = 0.0f;
        
        private ComputeShader imageProcessingShader;
        
        public ColorAdjustmentNode() : base()
        {
            Name = "Color Adjustment";
            imageProcessingShader = Resources.Load<ComputeShader>("ImageProcessingShader");
        }
        
        public override Texture2D ProcessTexture(Dictionary<string, BaseNode> nodeMap)
        {
            // Get the input texture from the first input port
            Texture2D inputTexture = GetInputTexture(0, nodeMap);
            
            if (inputTexture == null)
            {
                if (previewTexture == null)
                {
                    previewTexture = new Texture2D(1, 1);
                    previewTexture.SetPixel(0, 0, Color.black);
                    previewTexture.Apply();
                }
                return previewTexture;
            }
            
            // Create a copy for modification
            Texture2D resultTexture = new Texture2D(inputTexture.width, inputTexture.height);
            Color[] pixels = inputTexture.GetPixels();
            Color[] result = new Color[pixels.Length];
            
            for (int i = 0; i < pixels.Length; i++)
            {
                // Apply color adjustments
                Color pixel = pixels[i];
                
                // Convert to HSV
                float h, s, v;
                Color.RGBToHSV(pixel, out h, out s, out v);
                
                // Apply HSV adjustments
                h = (h + Hue) % 1.0f;
                s *= Saturation;
                v *= Brightness;
                
                // Contrast adjustment
                v = (v - 0.5f) * Contrast + 0.5f;
                
                // Clamp values
                s = Mathf.Clamp01(s);
                v = Mathf.Clamp01(v);
                
                // Convert back to RGB
                result[i] = Color.HSVToRGB(h, s, v);
                result[i].a = pixel.a; // Preserve alpha
            }
            
            resultTexture.SetPixels(result);
            resultTexture.Apply();
            
            // Cache the result for preview
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

            int kernelIndex = imageProcessingShader.FindKernel("ColorAdjustment");
            imageProcessingShader.SetTexture(kernelIndex, "InputTexture", input);
            imageProcessingShader.SetTexture(kernelIndex, "Result", output);
            imageProcessingShader.SetFloat("Brightness", Brightness);
            imageProcessingShader.SetFloat("Contrast", Contrast);
            imageProcessingShader.SetFloat("Saturation", Saturation);
            imageProcessingShader.SetFloat("Hue", Hue);

            int threadGroupsX = Mathf.CeilToInt(output.width / 8.0f);
            int threadGroupsY = Mathf.CeilToInt(output.height / 8.0f);
            imageProcessingShader.Dispatch(kernelIndex, threadGroupsX, threadGroupsY, 1);
        }
    }
} 