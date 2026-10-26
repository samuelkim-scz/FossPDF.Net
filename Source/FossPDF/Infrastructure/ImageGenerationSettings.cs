namespace FossPDF.Infrastructure
{
    /// <summary>Options for one page-image export. These do not mutate document/PDF defaults.</summary>
    public record ImageGenerationSettings
    {
        /// <summary>Null inherits the document DPI (legacy default: 72).</summary>
        public int? RasterDpi { get; init; }
        public ImageOutputFormat Format { get; init; } = ImageOutputFormat.Png;
        /// <summary>0-100. PNG is lossless and ignores quality; reserved for lossy encoders.</summary>
        public int CompressionQuality { get; init; } = 100;
        public static ImageGenerationSettings Default => new();
    }

    public enum ImageOutputFormat
    {
        Png,
        Jpeg,
        Webp
    }
}
