using SkiaSharp;

namespace FossPDF.Previewer
{
    sealed class PreviewPage : IDisposable
    {
        private readonly object gate = new();
        private int readers;
        private bool retired;
        public SKPicture Picture { get; }
        public float Width { get; }
        public float Height { get; }

        public PreviewPage(SKPicture picture, float width, float height)
        { Picture = picture; Width = width; Height = height; }

        // Frames retain immutable pictures independently of document replacement on the UI thread.
        internal bool Retain()
        {
            lock (gate)
            {
                if (retired) return false;
                readers++;
                return true;
            }
        }

        internal void Release()
        {
            lock (gate)
            {
                readers--;
                if (retired && readers == 0) Picture.Dispose();
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (retired) return;
                retired = true;
                if (readers == 0) Picture.Dispose();
            }
        }
    }
}
