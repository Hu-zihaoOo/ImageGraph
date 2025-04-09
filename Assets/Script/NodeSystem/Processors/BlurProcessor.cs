using UnityEngine;
using NodeImageEditor.Interfaces;

namespace NodeImageEditor.Processors
{
    public class BlurProcessor : IImageProcessor
    {
        private ComputeShader imageProcessingShader;
        private int blurKernelIndex;

        public BlurProcessor()
        {
            imageProcessingShader = Resources.Load<ComputeShader>("ImageProcessingShader");
            blurKernelIndex = imageProcessingShader.FindKernel("BlurEffect");
        }

        public Texture2D ProcessTexture(Texture2D inputTexture, object parameters)
        {
            if (inputTexture == null || parameters == null)
                return null;

            BlurParameters blurParams = parameters as BlurParameters;
            if (blurParams == null)
                return null;

            Color[] pixels = inputTexture.GetPixels();
            Color[] resultPixels = new Color[pixels.Length];

            for (int y = 0; y < inputTexture.height; y++)
            {
                for (int x = 0; x < inputTexture.width; x++)
                {
                    Color sum = Color.black;
                    int count = 0;

                    for (int ky = -blurParams.Radius; ky <= blurParams.Radius; ky++)
                    {
                        for (int kx = -blurParams.Radius; kx <= blurParams.Radius; kx++)
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

            Texture2D resultTexture = new Texture2D(inputTexture.width, inputTexture.height);
            resultTexture.SetPixels(resultPixels);
            resultTexture.Apply();

            return resultTexture;
        }

        public void ProcessPreview(RenderTexture input, RenderTexture output, object parameters)
        {
            if (imageProcessingShader == null || parameters == null)
                return;

            BlurParameters blurParams = parameters as BlurParameters;
            if (blurParams == null)
                return;

            imageProcessingShader.SetTexture(blurKernelIndex, "InputTexture", input);
            imageProcessingShader.SetTexture(blurKernelIndex, "Result", output);
            imageProcessingShader.SetInt("BlurRadius", blurParams.Radius);

            int threadGroupsX = Mathf.CeilToInt(output.width / 8.0f);
            int threadGroupsY = Mathf.CeilToInt(output.height / 8.0f);
            imageProcessingShader.Dispatch(blurKernelIndex, threadGroupsX, threadGroupsY, 1);
        }
    }
} 
