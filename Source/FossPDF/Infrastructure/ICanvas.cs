using System.Numerics;
using FossPDF.Drawing;
using SkiaSharp;

namespace FossPDF.Infrastructure
{
    internal interface ICanvas
    {
        Matrix4x4 Transform { get; }

        void Translate(Position vector);

        void DrawRectangle(Position vector, Size size, string color);

        /// <summary>Strokes a line with the given width, paint, gradient and dash pattern.</summary>
        void DrawLine(Position start, Position end, LineStyle style);

        /// <summary>
        /// Paints a blurred box shadow behind the given rectangle. The shadow shape follows the
        /// corner radii and is offset, spread and blurred according to the style.
        /// </summary>
        void DrawShadow(Position vector, Size size, BorderRadius cornerRadius, BoxShadowStyle shadow);

        /// <summary>
        /// Draws a decorated rectangle (fill and/or border) with optional rounded corners. This is
        /// the shared graphics contract used by backgrounds, borders and future decorations.
        /// </summary>
        void DrawRectangle(Position vector, Size size, RectangleStyle style);

        void DrawText(SKTextBlob skTextBlob, Position position, TextStyle style, DocumentSpecificFontManager fontManager);
        void DrawImage(SKImage image, Position position, Size size);

        void SaveState();
        void RestoreState();

        /// <summary>Intersects the current clip with a (possibly rounded) rectangle.</summary>
        void ClipRectangle(Position vector, Size size, BorderRadius cornerRadius);

        void DrawHyperlink(string url, Size size);
        void DrawSectionLink(string sectionName, Size size);
        void DrawSection(string sectionName);

        void Rotate(float angle);
        void Scale(float scaleX, float scaleY);

        int GetZIndex();
        void SetZIndex(int index);
    }
}
