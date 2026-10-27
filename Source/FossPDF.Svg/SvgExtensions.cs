using System;
using System.IO;
using FossPDF.Elements;
using FossPDF.Infrastructure;
using FossPDF.Svg;
using SkiaSharp;

namespace FossPDF.Fluent
{
    /// <summary>
    /// Places SVG images in the document. The vector form is the default: SVG paths are replayed
    /// into the document canvas, so PDF output keeps vector geometry. The raster form is an explicit
    /// opt-in for documents that contain effects the vector path cannot preserve; it renders the
    /// picture at the requested DPI before drawing it.
    /// </summary>
    public static class SvgExtensions
    {
        /// <summary>
        /// Places a parsed, reusable SVG image. The same instance can be used on many pages.
        /// </summary>
        public static void Svg(this IContainer parent, SvgImage svgImage, ImageScaling scaling = ImageScaling.FitWidth)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            if (svgImage == null)
                throw new ArgumentNullException(nameof(svgImage));

            Draw(parent, svgImage, scaling, raster: null);
        }

        /// <summary>
        /// Parses and places an SVG document from text.
        /// </summary>
        public static void Svg(this IContainer parent, string svgText, ImageScaling scaling = ImageScaling.FitWidth)
        {
            Svg(parent, SvgImage.FromText(svgText), scaling);
        }

        /// <summary>
        /// Parses and places an SVG document from bytes.
        /// </summary>
        public static void Svg(this IContainer parent, byte[] svgData, ImageScaling scaling = ImageScaling.FitWidth)
        {
            Svg(parent, SvgImage.FromBytes(svgData), scaling);
        }

        /// <summary>
        /// Parses and places an SVG document from a borrowed stream. The stream is read from its
        /// current position and is left open.
        /// </summary>
        public static void Svg(this IContainer parent, Stream svgStream, ImageScaling scaling = ImageScaling.FitWidth)
        {
            Svg(parent, SvgImage.FromStream(svgStream), scaling);
        }

        /// <summary>
        /// Places a parsed SVG image as a raster snapshot at an explicit DPI. Use this option only
        /// when the vector form cannot represent a visual effect faithfully; the default
        /// <see cref="Svg(IContainer, SvgImage, ImageScaling)"/> keeps vector output.
        /// </summary>
        public static void SvgRasterized(
            this IContainer parent,
            SvgImage svgImage,
            SvgRasterizationOptions? options = null)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            if (svgImage == null)
                throw new ArgumentNullException(nameof(svgImage));

            var resolvedOptions = options ?? new SvgRasterizationOptions();

            if (!float.IsFinite(resolvedOptions.Dpi) || resolvedOptions.Dpi <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "The DPI value must be a positive finite number.");

            if (resolvedOptions.Background == null)
                throw new ArgumentException("The background color cannot be null.", nameof(options));

            SKColor background;

            try
            {
                background = SKColor.Parse(resolvedOptions.Background);
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException(
                    "The background color is not a valid color value. Use a value accepted by SkiaSharp, " +
                    "for example '#00000000' or '#FFFFFFFF'.",
                    nameof(options),
                    exception);
            }

            Draw(parent, svgImage, resolvedOptions.Scaling, raster =>
            {
                using var image = svgImage.RenderRaster(raster.Size, resolvedOptions.Dpi, background);
                raster.Canvas.DrawImage(image, new SKRect(0, 0, raster.Size.Width, raster.Size.Height));
            });
        }

        private static void Draw(IContainer parent, SvgImage svgImage, ImageScaling scaling, Action<RasterDrawRequest>? raster)
        {
            if (scaling is not (ImageScaling.FitWidth or ImageScaling.FitHeight or ImageScaling.FitArea or ImageScaling.Resize))
                throw new ArgumentOutOfRangeException(nameof(scaling), "Unsupported image scaling mode.");

            if (scaling != ImageScaling.Resize)
            {
                var aspectRatio = svgImage.Size.Width / svgImage.Size.Height;
                parent = parent.AspectRatio(aspectRatio, Map(scaling));
            }

            if (raster == null)
            {
                parent.Canvas((canvas, size) => svgImage.Draw(canvas, size));
            }
            else
            {
                parent.Canvas((canvas, size) => raster(new RasterDrawRequest(canvas, size)));
            }

            static AspectRatioOption Map(ImageScaling scaling)
            {
                return scaling switch
                {
                    ImageScaling.FitWidth => AspectRatioOption.FitWidth,
                    ImageScaling.FitHeight => AspectRatioOption.FitHeight,
                    ImageScaling.FitArea => AspectRatioOption.FitArea,
                    _ => throw new ArgumentOutOfRangeException(nameof(scaling))
                };
            }
        }

        private readonly struct RasterDrawRequest
        {
            public RasterDrawRequest(SKCanvas canvas, Size size)
            {
                Canvas = canvas;
                Size = size;
            }

            public SKCanvas Canvas { get; }
            public Size Size { get; }
        }
    }

    /// <summary>
    /// Options for the explicit raster fallback of SVG images.
    /// </summary>
    public sealed class SvgRasterizationOptions
    {
        /// <summary>
        /// The raster resolution in dots per inch. The SVG is rendered at this resolution and the
        /// result is scaled to the layout size. Defaults to 144 DPI.
        /// </summary>
        public float Dpi { get; set; } = 144;

        /// <summary>
        /// The background color applied before the SVG is rendered, in a format accepted by
        /// SkiaSharp (for example <c>#00000000</c> for transparent). Defaults to transparent.
        /// </summary>
        public string Background { get; set; } = "#00000000";

        /// <summary>
        /// How the raster result is fitted into the available space. Defaults to
        /// <see cref="ImageScaling.FitWidth"/>.
        /// </summary>
        public ImageScaling Scaling { get; set; } = ImageScaling.FitWidth;
    }
}
