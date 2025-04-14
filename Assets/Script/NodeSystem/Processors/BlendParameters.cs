namespace NodeImageEditor.Processors
{
    public enum BlendMode
    {
        Multiply,
        Add
    }

    public class BlendParameters
    {
        public BlendMode Mode { get; set; }
        public float Strength { get; set; }

        public BlendParameters(BlendMode mode = BlendMode.Multiply, float strength = 1.0f)
        {
            Mode = mode;
            Strength = strength;
        }
    }
} 