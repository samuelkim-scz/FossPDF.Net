using System;
using FossPDF.Infrastructure;
using SkiaSharp;

namespace FossPDF.Drawing
{
    internal static class ImageProcessing
    {
        public static SKImage GetImageToDraw(
            SKImage original,
            Size availableSpace,
            int? targetDpi,
            int? compressionQuality,
            bool useOriginalImage,
            out bool dispose)
        {
            dispose = false;

            if (useOriginalImage || (targetDpi == null && compressionQuality == null))
                return original;

            var targetWidth = original.Width;
            var targetHeight = original.Height;

            if (targetDpi.HasValue)
            {
                var scaling = targetDpi.Value / 72f;
                targetWidth = Math.Max(1, (int) Math.Ceiling(availableSpace.Width * scaling));
                targetHeight = Math.Max(1, (int) Math.Ceiling(availableSpace.Height * scaling));

                // Never upscale beyond the source resolution.
                targetWidth = Math.Min(targetWidth, original.Width);
                targetHeight = Math.Min(targetHeight, original.Height);
            }

            var needsResize = targetWidth != original.Width || targetHeight != original.Height;
            var quality = compressionQuality == 101 ? null : compressionQuality;

            if (!needsResize && !quality.HasValue)
                return original;

            using var sourceBitmap = SKBitmap.FromImage(original);
            SKBitmap? resized = null;

            try
            {
                var bitmap = sourceBitmap;

                if (needsResize)
                {
                    resized = sourceBitmap.Resize(
                                  new SKImageInfo(targetWidth, targetHeight),
                                  new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None))
                              ?? throw new InvalidOperationException("Could not resample the image.");

                    bitmap = resized;
                }

                if (!quality.HasValue)
                {
                    dispose = true;
                    return SKImage.FromBitmap(bitmap)
                           ?? throw new InvalidOperationException("Could not create the resampled image.");
                }

                // Resize may change the bitmap alpha type even when the source is opaque. Use the
                // source's alpha type so opaque images still take the JPEG passthrough path.
                var preserveAlpha = original.AlphaType != SKAlphaType.Opaque;

                using var encoded = Encode(bitmap, quality.Value, preserveAlpha)
                    ?? throw new InvalidOperationException("Could not encode the image.");
                dispose = true;
                return SKImage.FromEncodedData(encoded)
                       ?? throw new InvalidOperationException("Could not decode the encoded image.");
            }
            finally
            {
                resized?.Dispose();
            }
        }

        private static SKData? Encode(SKBitmap bitmap, int quality, bool preserveAlpha)
        {
            if (preserveAlpha)
            {
                using var pixels = bitmap.PeekPixels();
                if (pixels == null)
                    return null;

                var mode = quality >= 100
                    ? SKWebpEncoderCompression.Lossless
                    : SKWebpEncoderCompression.Lossy;

                return pixels.Encode(new SKWebpEncoderOptions(mode, quality));
            }

            using var image = SKImage.FromBitmap(bitmap);
            return image?.Encode(SKEncodedImageFormat.Jpeg, quality);
        }
    }
}
