using System;

namespace FossPDF.Infrastructure
{
    /// <summary>Lossy compression quality presets for a single image.</summary>
    public enum ImageCompressionQuality
    {
        /// <summary>Quality 100; opaque images remain JPEG, transparent images use lossless WebP.</summary>
        Best,
        /// <summary>Quality 90.</summary>
        VeryHigh,
        /// <summary>Quality 75.</summary>
        High,
        /// <summary>Quality 50.</summary>
        Medium,
        /// <summary>Quality 25.</summary>
        Low,
        /// <summary>Quality 10.</summary>
        VeryLow
    }

    internal static class ImageCompressionQualityExtensions
    {
        public static int ToQuality(this ImageCompressionQuality quality)
        {
            return quality switch
            {
                ImageCompressionQuality.Best => 100,
                ImageCompressionQuality.VeryHigh => 90,
                ImageCompressionQuality.High => 75,
                ImageCompressionQuality.Medium => 50,
                ImageCompressionQuality.Low => 25,
                ImageCompressionQuality.VeryLow => 10,
                _ => throw new ArgumentOutOfRangeException(nameof(quality))
            };
        }
    }
}
