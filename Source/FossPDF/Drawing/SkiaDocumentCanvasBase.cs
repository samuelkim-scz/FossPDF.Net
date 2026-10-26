using System;
using FossPDF.Infrastructure;
using SkiaSharp;

namespace FossPDF.Drawing
{
    internal class SkiaDocumentCanvasBase : SkiaCanvasBase
    {
        private SKDocument Document { get; }
        private bool Disposed { get; set; }

        protected SkiaDocumentCanvasBase(Func<SKDocument> createDocument)
        {
            Document = createDocument();
        }

        public override void Dispose()
        {
            if (Disposed)
                return;

            Canvas?.Dispose();
            Canvas = null;
            Document.Dispose();
            Disposed = true;
        }

        public override void BeginDocument()
        {

        }

        public override void EndDocument()
        {
            Document.Close();
            Dispose();
        }

        public override void BeginPage(Size size)
        {
            Canvas?.Dispose();
            Canvas = Document.BeginPage(size.Width, size.Height);
        }

        public override void EndPage()
        {
            Document.EndPage();
            Canvas?.Dispose();
            Canvas = null;
        }
    }
}
