using System;
using FossPDF.Infrastructure;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace FossPDF.Drawing
{
    internal abstract class SkiaCanvasBase : ICanvas, IRenderingCanvas, IDisposable
    {
        internal SKCanvas? Canvas { get; set; }
        protected SKCanvas ActiveCanvas => Canvas ?? throw new InvalidOperationException("No page is being rendered.");

        public abstract void Dispose();

        public abstract void BeginDocument();
        public abstract void EndDocument();

        public abstract void BeginPage(Size size);
        public abstract void EndPage();

        public void Translate(Position vector)
        {
            ActiveCanvas.Translate(vector.X, vector.Y);
        }

        public void DrawRectangle(Position vector, Size size, string color)
        {
            if (size.Width < Size.Epsilon || size.Height < Size.Epsilon)
                return;

            var paint = color.ColorToPaint();
            ActiveCanvas.DrawRect(vector.X, vector.Y, size.Width, size.Height, paint);
        }

        public void DrawText(SKTextBlob skTextBlob, Position position, TextStyle style, DocumentSpecificFontManager fontManager)
        {
            ActiveCanvas.DrawText(skTextBlob, position.X, position.Y, fontManager.ToPaint(style));
        }

        public void DrawImage(SKImage image, Position vector, Size size)
        {
            ActiveCanvas.DrawImage(image, new SKRect(vector.X, vector.Y, size.Width, size.Height));
        }

        public void DrawHyperlink(string url, Size size)
        {
            ActiveCanvas.DrawUrlAnnotation(new SKRect(0, 0, size.Width, size.Height), url).Dispose();
        }

        public void DrawSectionLink(string sectionName, Size size)
        {
            ActiveCanvas.DrawLinkDestinationAnnotation(new SKRect(0, 0, size.Width, size.Height), sectionName).Dispose();
        }

        public void DrawSection(string sectionName)
        {
            ActiveCanvas.DrawNamedDestinationAnnotation(new SKPoint(0, 0), sectionName).Dispose();
        }

        public void Rotate(float angle)
        {
            ActiveCanvas.RotateDegrees(angle);
        }

        public void Scale(float scaleX, float scaleY)
        {
            ActiveCanvas.Scale(scaleX, scaleY);
        }
    }
}
