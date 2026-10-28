using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FossPDF.Infrastructure;
using SkiaSharp;

namespace FossPDF.Drawing
{
    internal sealed class SvgCanvas : SkiaCanvasBase, IDisposable
    {
        private MemoryStream? Stream { get; set; }
        internal ICollection<string> Images { get; } = new List<string>();

        public SvgCanvas()
        {
        }

        public override void Dispose()
        {
            Canvas?.Dispose();
            Canvas = null;
            Stream?.Dispose();
            Stream = null;
        }

        public override void BeginDocument()
        {

        }

        public override void EndDocument()
        {
            Dispose();
        }

        public override void DrawShadow(Position vector, Size size, BorderRadius cornerRadius, BoxShadowStyle shadow)
        {
            // The SVG backend has no mask-filter support, so a blurred shadow is rasterized at
            // 72 DPI and embedded as an image. Sharp shadows stay vector paths.
            if (shadow.Blur <= 0 || !TryCreateShadowShape(vector, size, cornerRadius, shadow, out var rect, out var radii))
            {
                base.DrawShadow(vector, size, cornerRadius, shadow);
                return;
            }

            var padding = shadow.Blur * 3;
            var imageRect = new SKRect(
                rect.Left - padding,
                rect.Top - padding,
                rect.Right + padding,
                rect.Bottom + padding);

            var pixelWidth = Math.Max(1, (int)Math.Ceiling(imageRect.Width));
            var pixelHeight = Math.Max(1, (int)Math.Ceiling(imageRect.Height));

            using var bitmap = new SKBitmap(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            bitmap.Erase(SKColors.Transparent);

            using (var canvas = new SKCanvas(bitmap))
            using (var roundRect = CreateRoundRect(rect, radii))
            using (var maskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, shadow.Blur / 2))
            using (var paint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColor.Parse(shadow.Color),
                MaskFilter = maskFilter
            })
            {
                canvas.Translate(-imageRect.Left, -imageRect.Top);
                canvas.DrawRoundRect(roundRect, paint);
            }

            using var image = SKImage.FromBitmap(bitmap);
            ActiveCanvas.DrawImage(image, imageRect);
        }

        public override void BeginPage(Size size)
        {
            Stream?.Dispose();
            Stream = new MemoryStream();
            Canvas?.Dispose(); // belt and braces
            Canvas = SKSvgCanvas.Create(SKRect.Create(size.Width, size.Height), Stream);
        }

        public override void EndPage()
        {
            ActiveCanvas.Save();
            Canvas?.Dispose();
            Canvas = null;

            if (Stream != null)
            {
                Stream.Position = 0;
                using (var streamReader = new StreamReader(Stream))
                {
                    Images.Add(streamReader.ReadToEnd());
                }
            }

            Stream?.Dispose();
        }
    }
}
