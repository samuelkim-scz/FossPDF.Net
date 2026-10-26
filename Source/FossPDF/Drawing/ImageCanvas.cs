using System;
using System.Collections.Generic;
using FossPDF.Helpers;
using FossPDF.Infrastructure;
using SkiaSharp;

namespace FossPDF.Drawing
{
    internal sealed class ImageCanvas : SkiaCanvasBase
    {
        private DocumentMetadata Metadata { get; }
        private SKSurface? Surface { get; set; }

        internal ICollection<byte[]> Images { get; } = new List<byte[]>();

        public ImageCanvas(DocumentMetadata metadata)
        {
            Metadata = metadata;
        }

        public override void BeginDocument()
        {

        }

        public override void EndDocument()
        {
            Dispose();
        }

        public override void Dispose()
        {
            Canvas?.Dispose();
            Canvas = null;
            Surface?.Dispose();
            Surface = null;
        }

        public override void BeginPage(Size size)
        {
            var scalingFactor = Metadata.RasterDpi / (float) PageSizes.PointsPerInch;
            var imageInfo = new SKImageInfo((int) (size.Width * scalingFactor), (int) (size.Height * scalingFactor));

            Surface?.Dispose();
            Surface = SKSurface.Create(imageInfo) ?? throw new InvalidOperationException("Could not create the page image surface.");
            Canvas = Surface.Canvas;

            Canvas.Scale(scalingFactor);
        }

        public override void EndPage()
        {
            ActiveCanvas.Save();
            var surface = Surface ?? throw new InvalidOperationException("No page image surface is available.");
            using var skImage = surface.Snapshot();
            using var skData = skImage.Encode(SKEncodedImageFormat.Png, 100);
            var image = skData.ToArray();
            Images.Add(image);

            Dispose();
        }
    }
}
