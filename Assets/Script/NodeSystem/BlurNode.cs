using UnityEngine;
using System.Collections.Generic;
using NodeImageEditor.Interfaces;
using NodeImageEditor.Processors;

namespace NodeImageEditor
{
    public class BlurNode : BaseNode
    {
        private IImageProcessor blurProcessor;
        private BlurParameters blurParameters;

        public int BlurRadius
        {
            get => blurParameters.Radius;
            set => blurParameters.Radius = value;
        }

        public BlurNode() : base()
        {
            Name = "Blur";
            blurProcessor = new BlurProcessor();
            blurParameters = new BlurParameters(1);
        }

        public override Texture2D ProcessTexture(Dictionary<string, BaseNode> nodeMap)
        {
            Debug.Log($"BlurNode ProcessTexture called for node {NodeID}");
            Texture2D inputTexture = GetInputTexture(0, nodeMap);
            
            if (inputTexture == null)
            {
                Debug.LogWarning($"BlurNode {NodeID}: No input texture found");
                if (previewTexture == null)
                {
                    previewTexture = new Texture2D(1, 1);
                    previewTexture.SetPixel(0, 0, Color.black);
                    previewTexture.Apply();
                }
                return previewTexture;
            }

            Debug.Log($"BlurNode {NodeID}: Processing texture with radius {BlurRadius}");
            Texture2D resultTexture = blurProcessor.ProcessTexture(inputTexture, blurParameters);
            if (resultTexture == null)
            {
                Debug.LogError($"BlurNode {NodeID}: Failed to process texture");
                return inputTexture;
            }

            previewTexture = resultTexture;
            return resultTexture;
        }

        public new void SetPreviewTexture(RenderTexture texture)
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
            Debug.Log($"BlurNode ProcessPreview called for node {NodeID}");
            blurProcessor.ProcessPreview(input, output, blurParameters);
        }
    }
} 