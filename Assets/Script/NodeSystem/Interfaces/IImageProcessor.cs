using UnityEngine;

namespace NodeImageEditor.Interfaces
{
    public interface IImageProcessor
    {
        Texture2D ProcessTexture(Texture2D inputTexture, object parameters);
        void ProcessPreview(RenderTexture input, RenderTexture output, object parameters);
    }
} 