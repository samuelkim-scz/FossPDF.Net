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
