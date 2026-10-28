using System;
using FossPDF.Drawing;
using FossPDF.Helpers;
using FossPDF.Infrastructure;
using SkiaSharp;

namespace FossPDF.Elements
{
    internal class DynamicImage : Element
    {
        private SemanticNode? _semanticNode;
        public Func<Size, byte[]>? Source { get; set; }

        public int? TargetDpi { get; set; }
        public int? CompressionQuality { get; set; }
        public bool UseOriginalImage { get; set; }

        internal override SpacePlan Measure(Size availableSpace)
        {
            return availableSpace.IsNegative()
                ? SpacePlan.Wrap()
                : SpacePlan.FullRender(availableSpace);
        }

        internal override void Draw(Size availableSpace)
        {
            var imageData = Source?.Invoke(availableSpace);

            if (imageData == null)
                return;

            using var original = SKImage.FromEncodedData(imageData);
            if (original == null)
                return;

            using var semantic = new AutomaticSemanticScope(Canvas, ref _semanticNode, "Figure");
            // Layout passes do not paint images; skip expensive resampling and encoding.
            if (AutomaticSemanticScope.Unwrap(Canvas) is FreeCanvas)
            {
                Canvas.DrawImage(original, Position.Zero, availableSpace);
                return;
            }

            var image = ImageProcessing.GetImageToDraw(
                original,
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
