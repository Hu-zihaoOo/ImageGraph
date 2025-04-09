using UnityEngine;
using System.Collections.Generic;

namespace NodeImageEditor
{
    public class InputImageNode : BaseNode
    {
        public Texture2D Texture;
        
        public InputImageNode() : base()
        {
            Name = "Input Image";
        }
        
        public override Texture2D ProcessTexture(Dictionary<string, BaseNode> nodeMap)
        {
            if (Texture != null)
            {
                // Cache the preview texture
                previewTexture = Texture;
                return Texture;
            }
            
            // If no texture is loaded, create a default 1x1 black texture
            if (previewTexture == null)
            {
                previewTexture = new Texture2D(1, 1);
                previewTexture.SetPixel(0, 0, Color.black);
                previewTexture.Apply();
            }
            
            return previewTexture;
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
            if (Texture != null)
            {
                Graphics.Blit(Texture, output);
            }
        }
    }
} 