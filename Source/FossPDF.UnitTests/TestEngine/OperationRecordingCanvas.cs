using System;
using System.Collections.Generic;
using FossPDF.Drawing;
using FossPDF.Infrastructure;
using FossPDF.UnitTests.TestEngine.Operations;
using SkiaSharp;

namespace FossPDF.UnitTests.TestEngine
{
    internal class OperationRecordingCanvas : ICanvas
    {
        public ICollection<OperationBase> Operations { get; } = new List<OperationBase>();

        public void Translate(Position vector) => Operations.Add(new CanvasTranslateOperation(vector));
        public void Rotate(float angle) => Operations.Add(new CanvasRotateOperation(angle));
        public void Scale(float scaleX, float scaleY) => Operations.Add(new CanvasScaleOperation(scaleX, scaleY));

        public void DrawRectangle(Position vector, Size size, string color) => Operations.Add(new CanvasDrawRectangleOperation(vector, size, color));

        public void DrawRectangle(Position vector, Size size, RectangleStyle style) => Operations.Add(new CanvasDrawRectangleStyleOperation(vector, size, style));

        public void SaveState() => Operations.Add(new CanvasSaveStateOperation());
        public void RestoreState() => Operations.Add(new CanvasRestoreStateOperation());
        public void ClipRectangle(Position vector, Size size, BorderRadius cornerRadius) => Operations.Add(new CanvasClipRectangleOperation(vector, size, cornerRadius));
        public void DrawText(SKTextBlob skTextBlob, Position position, TextStyle style, DocumentSpecificFontManager fontManager) => throw new NotImplementedException();
        public void DrawImage(SKImage image, Position position, Size size) => Operations.Add(new CanvasDrawImageOperation(position, size));

        public void DrawHyperlink(string url, Size size) => throw new NotImplementedException();
        public void DrawSectionLink(string sectionName, Size size) => throw new NotImplementedException();
        public void DrawSection(string sectionName) => throw new NotImplementedException();
    }
}
