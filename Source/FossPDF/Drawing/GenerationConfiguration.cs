using System;
using FossPDF.Helpers;
using FossPDF.Infrastructure;
using SkiaSharp;

namespace FossPDF.Drawing
{
    /// <summary>A private, per-invocation snapshot. Never writes to caller metadata or global settings.</summary>
    internal sealed class GenerationConfiguration
    {
        internal DocumentMetadata Metadata { get; }
        internal DocumentSettings Settings { get; }
        internal ContentDirection ContentDirection { get; }
        internal int PageLimit { get; }
        internal bool EnableCaching { get; }
        internal bool EnableDebugging { get; }
        internal bool CheckGlyphs { get; }

        private GenerationConfiguration(DocumentMetadata metadata, DocumentSettings settings)
        {
            Metadata = metadata;
            Settings = settings;
            ValidateOverrides(settings);
            ContentDirection = settings.ContentDirection ?? Infrastructure.ContentDirection.LeftToRight;
            PageLimit = settings.DocumentLayoutExceptionThreshold ?? FossPDF.Settings.DocumentLayoutExceptionThreshold;
            EnableCaching = settings.EnableCaching ?? FossPDF.Settings.EnableCaching;
            EnableDebugging = settings.EnableDebugging ?? FossPDF.Settings.EnableDebugging;
            CheckGlyphs = settings.CheckIfAllTextGlyphsAreAvailable ?? FossPDF.Settings.CheckIfAllTextGlyphsAreAvailable;
            metadata.RasterDpi = settings.ImageRasterDpi ?? metadata.RasterDpi;
            metadata.ImageQuality = settings.ImageCompressionQuality ?? metadata.ImageQuality;
            if (settings.PdfAConformance.HasValue)
                metadata.PdfA = settings.PdfAConformance == PdfAConformance.Legacy;
        }

        internal static GenerationConfiguration Resolve(IDocument document)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            var settings = (document as IDocumentSettingsProvider)?.GetSettings() ?? DocumentSettings.Default;
            var metadata = document.GetMetadata()
                ?? throw new ArgumentException("The document must return non-null metadata.", nameof(document));
            return new GenerationConfiguration(metadata.Snapshot(), settings);
        }

        private static void ValidateOverrides(DocumentSettings settings)
        {
            if (settings.ImageRasterDpi <= 0)
                throw new ArgumentOutOfRangeException(nameof(settings.ImageRasterDpi), "DPI must be positive.");
            if (settings.ImageCompressionQuality < 0 || settings.ImageCompressionQuality > 101)
                throw new ArgumentOutOfRangeException(nameof(settings.ImageCompressionQuality), "Quality must be in the range 0-101.");
            if (settings.DocumentLayoutExceptionThreshold <= 0)
                throw new ArgumentOutOfRangeException(nameof(settings.DocumentLayoutExceptionThreshold), "The page limit must be positive.");
            if (settings.ContentDirection.HasValue && !Enum.IsDefined(typeof(ContentDirection), settings.ContentDirection.Value))
                throw new ArgumentOutOfRangeException(nameof(settings.ContentDirection));
            if (settings.PdfAConformance.HasValue && !Enum.IsDefined(typeof(PdfAConformance), settings.PdfAConformance.Value))
                throw new ArgumentOutOfRangeException(nameof(settings.PdfAConformance));
            if (!Enum.IsDefined(typeof(PdfUaConformance), settings.PdfUaConformance))
                throw new ArgumentOutOfRangeException(nameof(settings.PdfUaConformance));
        }

        internal void ValidatePdf()
        {
            if (Settings.PdfAConformance.HasValue &&
                Settings.PdfAConformance != PdfAConformance.None &&
                Settings.PdfAConformance != PdfAConformance.Legacy)
                throw new NotSupportedException("Selectable PDF/A profiles require FP-402. Use Legacy only for the existing unqualified PdfA flag.");
            if (Settings.PdfUaConformance != PdfUaConformance.None)
                throw new NotSupportedException("PDF/UA output requires FP-401/FP-403 semantic tagging and conformance support.");
            if (!Settings.CompressDocument || Settings.EnableAdditionalCompression || Settings.Linearize)
                throw new NotSupportedException("Compression overrides and linearization require FP-306. The current renderer uses its normal compression.");
        }

        internal void ApplyImageOutput(ImageGenerationSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (settings.RasterDpi <= 0)
                throw new ArgumentOutOfRangeException(nameof(settings.RasterDpi), "DPI must be positive.");
            if (settings.CompressionQuality < 0 || settings.CompressionQuality > 100)
                throw new ArgumentOutOfRangeException(nameof(settings.CompressionQuality));
            if (!Enum.IsDefined(typeof(ImageOutputFormat), settings.Format))
                throw new ArgumentOutOfRangeException(nameof(settings.Format));
            if (settings.BackgroundColor != null)
            {
                ColorValidator.Validate(settings.BackgroundColor);
                if (settings.Format == ImageOutputFormat.Jpeg && SKColor.Parse(settings.BackgroundColor).Alpha != 255)
                    throw new ArgumentException("JPEG requires an opaque output background.", nameof(settings.BackgroundColor));
            }
            Metadata.RasterDpi = settings.RasterDpi ?? Metadata.RasterDpi;
        }
    }
}
