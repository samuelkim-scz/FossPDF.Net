using System;
using FossPDF.Infrastructure;

namespace FossPDF.Fluent
{
    /// <summary>Configures rasterization and compression for one image.</summary>
    public class ImageDescriptor
    {
        internal Elements.Image ImageElement { get; }

        internal ImageDescriptor(Elements.Image imageElement)
        {
            ImageElement = imageElement;
        }

        /// <summary>
        /// Draws the source image without resampling or recompression. This is the default when no
        /// per-image settings are provided.
        /// </summary>
        public ImageDescriptor UseOriginalImage(bool value = true)
        {
            ImageElement.UseOriginalImage = value;
            return this;
        }

        /// <summary>
        /// Sets the target raster resolution for this image. The display size is converted to pixels
        /// at this DPI, capped at the source resolution so images are never needlessly upscaled.
        /// </summary>
        public ImageDescriptor WithRasterDpi(int dpi)
        {
            if (dpi <= 0)
                throw new ArgumentOutOfRangeException(nameof(dpi), "The image raster DPI must be positive.");

            ImageElement.TargetDpi = dpi;
            return this;
        }

        /// <summary>Enables lossy JPEG/WebP recompression at the selected quality preset.</summary>
        public ImageDescriptor WithCompressionQuality(ImageCompressionQuality quality)
        {
            if (!Enum.IsDefined(typeof(ImageCompressionQuality), quality))
                throw new ArgumentOutOfRangeException(nameof(quality));

            return WithCompressionQuality(quality.ToQuality());
        }

        /// <summary>Enables lossy JPEG/WebP recompression at a quality from 0 to 100.</summary>
        public ImageDescriptor WithCompressionQuality(int quality)
        {
            if (quality < 0 || quality > 100)
                throw new ArgumentOutOfRangeException(nameof(quality), "The image compression quality must be in the range 0-100.");

            ImageElement.CompressionQuality = quality;
            return this;
        }
    }

    /// <summary>Configures rasterization and compression for a dynamically generated image.</summary>
    public class DynamicImageDescriptor
    {
        internal Elements.DynamicImage ImageElement { get; }

        internal DynamicImageDescriptor(Elements.DynamicImage imageElement)
        {
            ImageElement = imageElement;
        }

        public DynamicImageDescriptor UseOriginalImage(bool value = true)
        {
            ImageElement.UseOriginalImage = value;
            return this;
        }

        public DynamicImageDescriptor WithRasterDpi(int dpi)
        {
            if (dpi <= 0)
                throw new ArgumentOutOfRangeException(nameof(dpi), "The image raster DPI must be positive.");

            ImageElement.TargetDpi = dpi;
            return this;
        }

        public DynamicImageDescriptor WithCompressionQuality(ImageCompressionQuality quality)
        {
            if (!Enum.IsDefined(typeof(ImageCompressionQuality), quality))
                throw new ArgumentOutOfRangeException(nameof(quality));

            return WithCompressionQuality(quality.ToQuality());
        }

        public DynamicImageDescriptor WithCompressionQuality(int quality)
        {
            if (quality < 0 || quality > 100)
                throw new ArgumentOutOfRangeException(nameof(quality), "The image compression quality must be in the range 0-100.");

            ImageElement.CompressionQuality = quality;
            return this;
        }
    }
}
