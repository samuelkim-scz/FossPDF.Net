using System;
using System.Numerics;
using FossPDF.Infrastructure;
using SkiaSharp;

namespace FossPDF.Drawing
{
    internal class FreeCanvas : ICanvas, IRenderingCanvas
    {
        private Matrix4x4 CurrentTransform { get; set; } = Matrix4x4.Identity;

        #region IRenderingCanvas

        public void BeginDocument()
        {
            CurrentTransform = Matrix4x4.Identity;
        }

        public void EndDocument()
        {

        }

        public void BeginPage(Size size)
        {
            CurrentTransform = Matrix4x4.Identity;
        }

        public void EndPage()
        {

        }

        #endregion

        #region ICanvas

        public Matrix4x4 Transform => CurrentTransform;

        public void Translate(Position vector)
        {
            CurrentTransform = Matrix4x4.Multiply(
                Matrix4x4.CreateTranslation(vector.X, vector.Y, 0),
                CurrentTransform);
        }

        public void DrawRectangle(Position vector, Size size, string color)
        {

        }

        public void DrawLine(Position start, Position end, LineStyle style)
        {

        }

        public void DrawShadow(Position vector, Size size, BorderRadius cornerRadius, BoxShadowStyle shadow)
        {

        }

        public void DrawRectangle(Position vector, Size size, RectangleStyle style)
        {

        }

        public void DrawText(SKTextBlob skTextBlob, Position position, TextStyle style, DocumentSpecificFontManager fontManager)
        {

        }

        public void DrawImage(SKImage image, Position position, Size size)
        {

        }

        public void SaveState()
        {

        }

        public void RestoreState()
        {

        }

        public void ClipRectangle(Position vector, Size size, BorderRadius cornerRadius)
        {

        }

        public void DrawHyperlink(string url, Size size)
        {

        }

        public void DrawSectionLink(string sectionName, Size size)
        {

        }

        public void DrawSection(string sectionName)
        {

        }

        public void Rotate(float angle)
        {
            CurrentTransform = Matrix4x4.Multiply(
                Matrix4x4.CreateRotationZ((float) (angle * Math.PI / 180.0)),
                CurrentTransform);
        }

        public void Scale(float scaleX, float scaleY)
        {
            CurrentTransform = Matrix4x4.Multiply(
                Matrix4x4.CreateScale(scaleX, scaleY, 1),
                CurrentTransform);
        }

        public int GetZIndex()
        {
            return 0;
        }

        public void SetZIndex(int index)
        {

        }

        #endregion
    }
}
