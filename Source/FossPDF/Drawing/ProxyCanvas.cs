using FossPDF.Infrastructure;
using SkiaSharp;

namespace FossPDF.Drawing
{
    /// <summary>
    /// Forwards drawing operations to a replaceable target. Container elements that perform
    /// speculative layout can redirect their subtree to a <see cref="FreeCanvas"/> while
    /// measuring, then point the same subtree at the real canvas when drawing.
    /// </summary>
    internal class ProxyCanvas : ICanvas
    {
        internal ICanvas Target { get; set; } = null!;

        public void Translate(Position vector)
        {
            Target.Translate(vector);
        }

        public void DrawRectangle(Position vector, Size size, string color)
        {
            Target.DrawRectangle(vector, size, color);
        }

        public void DrawText(SKTextBlob skTextBlob, Position position, TextStyle style, DocumentSpecificFontManager fontManager)
        {
            Target.DrawText(skTextBlob, position, style, fontManager);
        }

        public void DrawImage(SKImage image, Position position, Size size)
        {
            Target.DrawImage(image, position, size);
        }

        public void DrawHyperlink(string url, Size size)
        {
            Target.DrawHyperlink(url, size);
        }

        public void DrawSectionLink(string sectionName, Size size)
        {
            Target.DrawSectionLink(sectionName, size);
        }

        public void DrawSection(string sectionName)
        {
            Target.DrawSection(sectionName);
        }

        public void Rotate(float angle)
        {
            Target.Rotate(angle);
        }

        public void Scale(float scaleX, float scaleY)
        {
            Target.Scale(scaleX, scaleY);
        }
    }
}
