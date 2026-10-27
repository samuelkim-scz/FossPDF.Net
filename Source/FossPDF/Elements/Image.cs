using FossPDF.Drawing;
using FossPDF.Helpers;
using FossPDF.Infrastructure;
using SkiaSharp;

namespace FossPDF.Elements
{
    internal class Image : Element, ICacheable
    {
        public SKImage? InternalImage { get; set; }

        public int? TargetDpi { get; set; }
        public int? CompressionQuality { get; set; }
        public bool UseOriginalImage { get; set; }

        // InternalImage is borrowed, including images shared through Infrastructure.Image.

        internal override SpacePlan Measure(Size availableSpace)
        {
            return availableSpace.IsNegative()
                ? SpacePlan.Wrap()
                : SpacePlan.FullRender(availableSpace);
        }

        internal override void Draw(Size availableSpace)
        {
            if (InternalImage == null)
                return;

            // Layout passes do not paint images; skip expensive resampling and encoding.
            if (Canvas is FreeCanvas)
            {
                Canvas.DrawImage(InternalImage, Position.Zero, availableSpace);
                return;
            }

            var image = ImageProcessing.GetImageToDraw(
                InternalImage,
                availableSpace,
                TargetDpi ?? PageContext?.ImageRasterDpi,
                CompressionQuality ?? PageContext?.ImageCompressionQuality,
                UseOriginalImage,
                out var dispose);

            try
            {
                Canvas.DrawImage(image, Position.Zero, availableSpace);
            }
            finally
            {
                if (dispose)
                    image.Dispose();
            }
        }
    }
}
