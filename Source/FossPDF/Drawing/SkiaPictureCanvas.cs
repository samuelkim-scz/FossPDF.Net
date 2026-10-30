using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FossPDF.Helpers;
using FossPDF.Infrastructure;
using SkiaSharp;

namespace FossPDF.Drawing
{
    public class PreviewerPicture
    {
        public SKPicture Picture { get; set; }
        public Size Size { get; set; }

        public PreviewerPicture(SKPicture picture, Size size)
        {
            Picture = picture;
            Size = size;
        }
    }

    internal class SkiaPictureCanvas : SkiaCanvasBase, IDisposable
    {
        internal Previewer.InspectionCollector? Inspection { get; init; }
        private bool _disposed;

        private SKPictureRecorder? PictureRecorder { get; set; }
        private Size? CurrentPageSize { get; set; }

        public ICollection<PreviewerPicture> Pictures { get; } = new List<PreviewerPicture>();

        public override void BeginDocument()
        {
            Pictures.Clear();
        }

        public override void BeginPage(Size size)
        {
            CurrentPageSize = size;
            PictureRecorder?.Dispose();
            PictureRecorder = new SKPictureRecorder();
            Canvas?.Dispose();
            Canvas = PictureRecorder.BeginRecording(new SKRect(0, 0, size.Width, size.Height));
            BeginZIndexLayers(size);
        }

        public override void EndPage()
        {
            EndZIndexLayers();
            var picture = PictureRecorder?.EndRecording();

            if (picture != null && CurrentPageSize.HasValue)
                Pictures.Add(new PreviewerPicture(picture, CurrentPageSize.Value));
            else
                picture?.Dispose();

            PictureRecorder?.Dispose();
            Canvas?.Dispose();
            PictureRecorder = null;
        }

        public override void EndDocument()
        {
            Canvas?.Dispose();
        }

        public override void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            DisposeZIndexLayers();
            PictureRecorder?.Dispose();
            Canvas?.Dispose();
            Canvas = null;
            _disposed = true;
        }

        protected virtual void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
    }
}
