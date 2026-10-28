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
        private ImageGenerationSettings Settings { get; }
        private SKColor Background { get; }
        private SKSurface? Surface { get; set; }

        internal ICollection<byte[]> Images { get; } = new List<byte[]>();

        public ImageCanvas(DocumentMetadata metadata, ImageGenerationSettings settings)
        {
            Metadata = metadata;
            Settings = settings;
            Background = settings.BackgroundColor != null
                ? SKColor.Parse(settings.BackgroundColor)
                : settings.Format == ImageOutputFormat.Jpeg ? SKColors.White : SKColors.Transparent;
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
            DisposeZIndexLayers();
            Canvas?.Dispose();
            Canvas = null;
            Surface?.Dispose();
            Surface = null;
        }

        public override void BeginPage(Size size)
        {
            var scalingFactor = Metadata.RasterDpi / (float) PageSizes.PointsPerInch;
            ValidateDimensions(size.Width * scalingFactor, size.Height * scalingFactor);
            var imageInfo = new SKImageInfo((int) (size.Width * scalingFactor), (int) (size.Height * scalingFactor));

            Surface?.Dispose();
            Surface = SKSurface.Create(imageInfo) ?? throw new InvalidOperationException("Could not create the page image surface.");
            Canvas = Surface.Canvas;
            Canvas.Clear(Background);
            Canvas.Scale(scalingFactor);

            BeginZIndexLayers(size);
        }

        public override void EndPage()
        {
            EndZIndexLayers();
            ActiveCanvas.Save();
            var surface = Surface ?? throw new InvalidOperationException("No page image surface is available.");
            using var skImage = surface.Snapshot();
            using var skData = Encode(skImage)
                ?? throw new InvalidOperationException($"Could not encode the page as {Settings.Format}.");
            var image = skData.ToArray();
            Images.Add(image);

            Dispose();
        }

        private SKData? Encode(SKImage image)
        {
            if (Settings.Format == ImageOutputFormat.Webp)
            {
                using var pixels = image.PeekPixels()
                    ?? throw new InvalidOperationException("Could not access the page pixels for WebP encoding.");
                var mode = Settings.CompressionQuality == 100
                    ? SKWebpEncoderCompression.Lossless
                    : SKWebpEncoderCompression.Lossy;
                return pixels.Encode(new SKWebpEncoderOptions(mode, Settings.CompressionQuality));
            }

            return Settings.Format == ImageOutputFormat.Png
                ? image.Encode(SKEncodedImageFormat.Png, 100)
                : image.Encode(SKEncodedImageFormat.Jpeg, Settings.CompressionQuality);
        }

        private void ValidateDimensions(float width, float height)
        {
            if (float.IsNaN(width) || float.IsInfinity(width) || width < 1 || width >= int.MaxValue ||
                float.IsNaN(height) || float.IsInfinity(height) || height < 1 || height >= int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(Metadata.RasterDpi), "Page dimensions at this DPI must fit positive integer pixel dimensions.");
            if (Settings.Format == ImageOutputFormat.Webp && (width >= 16384 || height >= 16384))
                throw new ArgumentOutOfRangeException(nameof(Settings.Format), "WebP page dimensions cannot exceed 16383 pixels per axis. Reduce page size or DPI.");
            if ((long)(int)width * (int)height > int.MaxValue / 4)
                throw new ArgumentOutOfRangeException(nameof(Metadata.RasterDpi), "The page raster exceeds the supported pixel-buffer size. Reduce page size or DPI.");
        }
    }
}
