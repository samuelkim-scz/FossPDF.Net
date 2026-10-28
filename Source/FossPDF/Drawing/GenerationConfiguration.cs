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
        internal PdfOptimizationSettings? Optimization { get; }

        /// <summary>The selected PDF/A profile, or <see langword="null"/> when the settings did not decide.</summary>
        internal PdfAConformance? SelectedPdfAConformance => Settings.PdfAConformance;

        internal bool HasSelectablePdfA =>
            SelectedPdfAConformance is not null &&
            SelectedPdfAConformance != PdfAConformance.None &&
            SelectedPdfAConformance != PdfAConformance.Legacy;

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
            Optimization = ResolveOptimization(settings);
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
            if (HasSelectablePdfA)
                throw new NotSupportedException(
                    "PDF/A-2 and PDF/A-3 output is written by the optional FossPDF.PdfOperations package. " +
                    "Use GeneratePdfA or GenerateTaggedPdf instead of GeneratePdf; the core package cannot write the " +
                    "required output intent and PDF/A identification metadata.");
            if (Settings.PdfUaConformance != PdfUaConformance.None)
                throw new NotSupportedException("PDF/UA output requires FP-401/FP-403 semantic tagging and conformance support.");

            if (Optimization != null)
            {
                // Fail before composition when qpdf is not available, rather than after rendering.
                Optimization.Validate();
                PdfOptimizer.ResolveExecutable(Optimization.QpdfExecutablePath);
            }
        }

        /// <summary>
        /// Validates the settings for a semantic capture. Selectable PDF/A profiles are allowed here
        /// because the companion package writes the conformance structures; the core only captures
        /// the content.
        /// </summary>
        internal void ValidateTaggedCapture()
        {
            if (Settings.PdfUaConformance != PdfUaConformance.None)
                throw new NotSupportedException("PDF/UA output requires FP-403 semantic tagging and conformance support.");
        }

        private static PdfOptimizationSettings? ResolveOptimization(DocumentSettings settings)
        {
            if (settings.CompressDocument && !settings.EnableAdditionalCompression && !settings.Linearize)
                return null;

            return new PdfOptimizationSettings
            {
                DisableCompression = !settings.CompressDocument,
                EnableAdditionalCompression = settings.EnableAdditionalCompression,
                Linearize = settings.Linearize,
                QpdfExecutablePath = settings.QpdfExecutablePath
            };
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
