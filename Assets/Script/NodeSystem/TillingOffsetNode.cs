using UnityEngine;
using System.Collections.Generic;
using NodeImageEditor.Interfaces;
using NodeImageEditor.Processors;

namespace NodeImageEditor
{
    public class TillingOffsetNode : BaseNode
    {
        private IImageProcessor tillingProcessor;
        private TillingOffsetParameters tillingParameters;

        public float TillingX
        {
            get => tillingParameters.TillingX;
            set => tillingParameters.TillingX = value;
        }

        public float TillingY
        {
            get => tillingParameters.TillingY;
            set => tillingParameters.TillingY = value;
        }

        public float OffsetX
        {
            get => tillingParameters.OffsetX;
            set => tillingParameters.OffsetX = value;
        }

        public float OffsetY
        {
            get => tillingParameters.OffsetY;
            set => tillingParameters.OffsetY = value;
        }

        public TillingOffsetNode() : base()
        {
            Name = "Tilling & Offset";
            tillingProcessor = new TillingOffsetProcessor();
            tillingParameters = new TillingOffsetParameters();
        }

        public override Texture2D ProcessTexture(Dictionary<string, BaseNode> nodeMap)
        {
            Debug.Log($"TillingOffsetNode ProcessTexture called for node {NodeID}");
            Texture2D inputTexture = GetInputTexture(0, nodeMap);
            
            if (inputTexture == null)
            {
                Debug.LogWarning($"TillingOffsetNode {NodeID}: No input texture found");
                if (previewTexture == null)
                {
                    previewTexture = new Texture2D(1, 1);
                    previewTexture.SetPixel(0, 0, Color.black);
                    previewTexture.Apply();
                }
                return previewTexture;
            }

            Debug.Log($"TillingOffsetNode {NodeID}: Processing texture with tilling ({TillingX}, {TillingY}) and offset ({OffsetX}, {OffsetY})");
            Texture2D resultTexture = tillingProcessor.ProcessTexture(inputTexture, tillingParameters);
            if (resultTexture == null)
            {
                Debug.LogError($"TillingOffsetNode {NodeID}: Failed to process texture");
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
            Debug.Log($"TillingOffsetNode ProcessPreview called for node {NodeID}");
            tillingProcessor.ProcessPreview(input, output, tillingParameters);
        }
    }
} 