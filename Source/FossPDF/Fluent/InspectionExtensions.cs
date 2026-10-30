using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using FossPDF.Drawing;
using FossPDF.Infrastructure;
using FossPDF.Previewer;

namespace FossPDF.Fluent
{
    public static class InspectionExtensions
    {
        /// <summary>
        /// Names a logical inspection boundary and captures caller source without PDBs or stack
        /// walking. Source data is exported only by explicit inspection generation/preview.
        /// </summary>
        public static IContainer Inspect(this IContainer container, string id, string? label = null,
            string? configuration = null, [CallerFilePath] string sourceFile = "",
            [CallerLineNumber] int sourceLine = 0, [CallerMemberName] string sourceMember = "")
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || label?.Length > 4096 || configuration?.Length > 4096 ||
                sourceFile.Length > 4096 || sourceMember.Length > 512 || sourceLine < 0)
                throw new ArgumentException("Inspection ID/source/labels must be non-empty where required and within protocol limits.");
            return container.Element(new InspectionMarker { Id = id, Label = label, Configuration = configuration,
                SourceFile = sourceFile, SourceLine = sourceLine, SourceMember = sourceMember });
        }

        /// <summary>
        /// Renders pictures plus an optional local hierarchy in page points. Dispose the result
        /// after use. Inspection disables layout caching for this render and exports source/text.
        /// Available on all core target frameworks. Ordinary output never collects this data.
        /// </summary>
        public static InspectedPreview GenerateInspectedPreview(this IDocument document)
        {
            var collector = new InspectionCollector();
            var configuration = GenerationConfiguration.Resolve(document);
            configuration = configuration.WithSettings(configuration.Settings with { EnableCaching = false });
            using var canvas = new SkiaPictureCanvas { Inspection = collector };
            try
            {
                DocumentGenerator.RenderDocument(canvas, document, configuration);
                collector.Snapshot.Validate(canvas.Pictures.Count);
                return new InspectedPreview(canvas.Pictures.ToArray(), collector.Snapshot);
            }
            catch
            {
                foreach (var picture in canvas.Pictures)
                    picture.Picture.Dispose();
                throw;
            }
        }
    }

    /// <summary>Owns generated native pictures; the inspection DTO contains only managed data.</summary>
    public sealed class InspectedPreview : IDisposable
    {
        private bool disposed;
        public IReadOnlyList<PreviewerPicture> Pictures { get; }
        public InspectionSnapshot Inspection { get; }
        internal InspectedPreview(IReadOnlyList<PreviewerPicture> pictures, InspectionSnapshot inspection)
        {
            Pictures = pictures; Inspection = inspection;
        }
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            foreach (var picture in Pictures)
                picture.Picture.Dispose();
        }
    }
}
