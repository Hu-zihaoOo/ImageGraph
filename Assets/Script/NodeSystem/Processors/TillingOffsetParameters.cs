namespace NodeImageEditor.Processors
{
    public class TillingOffsetParameters
    {
        public float TillingX { get; set; }
        public float TillingY { get; set; }
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }

        public TillingOffsetParameters(float tillingX = 1, float tillingY = 1, float offsetX = 0, float offsetY = 0)
        {
            TillingX = tillingX;
            TillingY = tillingY;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }
    }
} 