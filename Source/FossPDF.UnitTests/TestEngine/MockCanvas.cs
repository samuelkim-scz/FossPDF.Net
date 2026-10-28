using System;
using System.Numerics;
using FossPDF.Drawing;
using FossPDF.Infrastructure;
using SkiaSharp;

namespace FossPDF.UnitTests.TestEngine
{
    internal class MockCanvas : ICanvas
    {
        public Matrix4x4 Transform => Matrix4x4.Identity;

        public Action<Position> TranslateFunc { get; set; }
        public Action<float> RotateFunc { get; set; }
        public Action<float, float> ScaleFunc { get; set; }
        public Action<SKImage, Position, Size> DrawImageFunc { get; set; }
        public Action<Position, Size, string> DrawRectFunc { get; set; }
        public Action<Position, Size, RectangleStyle> DrawRectStyleFunc { get; set; }
        public Action<Position, Position, LineStyle> DrawLineFunc { get; set; }
        public Action<Position, Size, BorderRadius, BoxShadowStyle> DrawShadowFunc { get; set; }
        public Action<Position, Size, BorderRadius> ClipRectFunc { get; set; }
        public Action SaveStateFunc { get; set; }
        public Action RestoreStateFunc { get; set; }

        public void Translate(Position vector) => TranslateFunc(vector);
        public void Rotate(float angle) => RotateFunc(angle);
        public void Scale(float scaleX, float scaleY) => ScaleFunc(scaleX, scaleY);

        public void DrawRectangle(Position vector, Size size, string color) => DrawRectFunc(vector, size, color);
        public void DrawRectangle(Position vector, Size size, RectangleStyle style) => DrawRectStyleFunc(vector, size, style);
        public void DrawLine(Position start, Position end, LineStyle style) => DrawLineFunc(start, end, style);
        public void DrawShadow(Position vector, Size size, BorderRadius cornerRadius, BoxShadowStyle shadow) => DrawShadowFunc(vector, size, cornerRadius, shadow);
        public void DrawText(SKTextBlob skTextBlob, Position position, TextStyle style, DocumentSpecificFontManager fontManager) => throw new NotImplementedException();
        public void DrawImage(SKImage image, Position position, Size size) => DrawImageFunc(image, position, size);

        public void SaveState() => SaveStateFunc();
        public void RestoreState() => RestoreStateFunc();
        public void ClipRectangle(Position vector, Size size, BorderRadius cornerRadius) => ClipRectFunc(vector, size, cornerRadius);

        public void DrawHyperlink(string url, Size size) => throw new NotImplementedException();
        public void DrawSectionLink(string sectionName, Size size) => throw new NotImplementedException();
        public void DrawSection(string sectionName) => throw new NotImplementedException();
    }
}
