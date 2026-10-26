namespace FossPDF.Infrastructure
{
    /// <summary>Options for one page-image export. These do not mutate document/PDF defaults.</summary>
    public record ImageGenerationSettings
    {
        /// <summary>Null inherits the document DPI (legacy default: 72).</summary>
        public int? RasterDpi { get; init; }
        public ImageOutputFormat Format { get; init; } = ImageOutputFormat.Png;
        /// <summary>0-100. PNG ignores quality; WebP uses lossless encoding at 100 and lossy encoding below 100.</summary>
        public int CompressionQuality { get; init; } = 100;
        /// <summary>Unpainted page background. Null means white for JPEG and transparent for PNG/WebP.</summary>
        public string? BackgroundColor { get; init; }
        public static ImageGenerationSettings Default => new();
    }

    public enum ImageOutputFormat
    {
        Png,
        Jpeg,
        Webp
    }
}
