namespace FossPDF.Infrastructure
{
    /// <summary>
    /// Immutable overrides for one document. Null values inherit the legacy metadata or
    /// process defaults when generation starts. Configure documents before sharing them.
    /// </summary>
    public record DocumentSettings
    {
        /// <summary>Default target DPI for images without a per-image override.</summary>
        public int? ImageRasterDpi { get; init; }

        /// <summary>
        /// 0-100 for lossy image encoding; 101 preserves the legacy lossless default. Also the
        /// default per-image quality for images without an override.
        /// </summary>
        public int? ImageCompressionQuality { get; init; }
        public ContentDirection? ContentDirection { get; init; }
        /// <summary>
        /// Selects an archival profile for GeneratePdfA in the optional .NET 9 PdfOperations package.
        /// That method validates the final output with veraPDF. Ordinary GeneratePdf accepts only
        /// None and Legacy; null inherits DocumentMetadata.PdfA.
        /// </summary>
        public PdfAConformance? PdfAConformance { get; init; }
        public PdfUaConformance PdfUaConformance { get; init; } = PdfUaConformance.None;

        /// <summary>
        /// When <see langword="false"/>, the PDF is post-processed to decompress decodable streams.
        /// The output is larger but easier to inspect. Requires qpdf 11 or newer.
        /// </summary>
        public bool CompressDocument { get; init; } = true;

        /// <summary>
        /// Recompresses streams at the maximum level and generates object streams. Usually reduces
        /// the file size at the cost of processing time. Requires qpdf 11 or newer and cannot be
        /// combined with <see cref="CompressDocument"/> set to <see langword="false"/>.
        /// </summary>
        public bool EnableAdditionalCompression { get; init; }

        /// <summary>Produces a linearized (web-optimized) PDF. Requires qpdf 11 or newer.</summary>
        public bool Linearize { get; init; }

        /// <summary>
        /// The qpdf executable used by compression and linearization settings. When omitted,
        /// <see cref="FossPDF.Settings.QpdfExecutablePath"/> and then the <c>PATH</c> environment
        /// variable are used.
        /// </summary>
        public string? QpdfExecutablePath { get; init; }

        public int? DocumentLayoutExceptionThreshold { get; init; }
        public bool? EnableCaching { get; init; }
        public bool? EnableDebugging { get; init; }
        public bool? CheckIfAllTextGlyphsAreAvailable { get; init; }

        public static DocumentSettings Default => new();
    }

    public enum PdfAConformance
    {
        None,
        /// <summary>Maps the existing DocumentMetadata.PdfA flag; no specific validated profile is claimed.</summary>
        Legacy,
        PdfA2A,
        PdfA2B,
        PdfA2U,
        PdfA3A,
        PdfA3B,
        PdfA3U
    }

    public enum PdfUaConformance
    {
        None,
        PdfUa1
    }

    internal interface IDocumentSettingsProvider
    {
        DocumentSettings? GetSettings();
    }
}
