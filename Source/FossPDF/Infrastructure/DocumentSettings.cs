namespace FossPDF.Infrastructure
{
    /// <summary>
    /// Immutable overrides for one document. Null values inherit the legacy metadata or
    /// process defaults when generation starts. Configure documents before sharing them.
    /// </summary>
    public record DocumentSettings
    {
        public int? ImageRasterDpi { get; init; }
        /// <summary>0-100 for lossy image encoding; 101 preserves the legacy lossless default.</summary>
        public int? ImageCompressionQuality { get; init; }
        public ContentDirection? ContentDirection { get; init; }
        public PdfAConformance? PdfAConformance { get; init; }
        public PdfUaConformance PdfUaConformance { get; init; } = PdfUaConformance.None;

        /// <summary>The current renderer uses its normal compression. False requires a future backend.</summary>
        public bool CompressDocument { get; init; } = true;
        public bool EnableAdditionalCompression { get; init; }
        public bool Linearize { get; init; }

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
