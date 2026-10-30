using System.Numerics;
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
        internal FossPDF.Previewer.InspectionCollector? Inspection { get; set; }
        internal ICanvas Target { get; set; } = null!;

        public Matrix4x4 Transform => Target.Transform;

        public void Translate(Position vector)
        {
            Target.Translate(vector);
        }

        public void DrawRectangle(Position vector, Size size, string color)
        {
            Target.DrawRectangle(vector, size, color);
        }

        public void DrawLine(Position start, Position end, LineStyle style)
        {
            Target.DrawLine(start, end, style);
        }

        public void DrawShadow(Position vector, Size size, BorderRadius cornerRadius, BoxShadowStyle shadow)
        {
            Target.DrawShadow(vector, size, cornerRadius, shadow);
        }

        public void DrawRectangle(Position vector, Size size, RectangleStyle style)
        {
            Target.DrawRectangle(vector, size, style);
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

        public void SaveState()
        {
            Target.SaveState();
        }

        public void RestoreState()
        {
            Target.RestoreState();
        }

        public void ClipRectangle(Position vector, Size size, BorderRadius cornerRadius)
        {
            Target.ClipRectangle(vector, size, cornerRadius);
        }

        public void Rotate(float angle)
        {
            Target.Rotate(angle);
        }

        public int GetZIndex()
        {
            return Target.GetZIndex();
        }

        public void SetZIndex(int index)
        {
            Target.SetZIndex(index);
        }

        public void Scale(float scaleX, float scaleY)
        {
            Target.Scale(scaleX, scaleY);
        }
    }
}
