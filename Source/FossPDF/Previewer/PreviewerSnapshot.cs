using System;
using System.Collections.Generic;
using FossPDF.Drawing;
using FossPDF.Infrastructure;

namespace FossPDF.Previewer
{
    /// <summary>Owns native preview pictures. Dispose after use; inspection contains only managed values.</summary>
    public sealed class PreviewerSnapshot : IDisposable
    {
        public IReadOnlyCollection<PreviewerPicture> Pictures { get; }
        public InspectionSnapshot? Inspection { get; }
        private bool Disposed;
        internal PreviewerSnapshot(ICollection<PreviewerPicture> pictures, InspectionSnapshot? inspection)
        {
            Pictures = new List<PreviewerPicture>(pictures).AsReadOnly();
            Inspection = inspection;
        }
        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            foreach (var picture in Pictures) picture.Picture.Dispose();
        }
    }

    public static class PreviewerSnapshotExtensions
    {
        /// <summary>Generates a disposable local snapshot. Available on all core target frameworks.</summary>
        public static PreviewerSnapshot GeneratePreviewerSnapshot(this IDocument document, PreviewerOptions? options = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            options ??= new PreviewerOptions();
            options.Validate();
            return DocumentGenerator.GeneratePreviewerSnapshot(document, options);
        }
    }
}
