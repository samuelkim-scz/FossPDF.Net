using System;
using System.IO;
using System.Xml.Linq;
using FossPDF.Infrastructure;
using SkiaSharp;
using Svg.Skia;

namespace FossPDF.Svg
{
    /// <summary>
    /// A parsed, reusable SVG image. The SVG document is validated and parsed once; the resulting
    /// picture can then be drawn any number of times, including on multiple pages of the same
    /// document. Use a single instance wherever the same SVG is repeated so parsing is not repeated.
    /// </summary>
    /// <remarks>
    /// The image owns the parsed picture. Call <see cref="Dispose"/> when the image is no longer
    /// needed; the fluent convenience overloads that accept text, bytes or a stream leave the parsed
    /// picture to the garbage collector, like the corresponding image overloads.
    /// </remarks>
    public sealed class SvgImage : IDisposable
    {
        private readonly SKSvg _document;
        private readonly SKRect _viewBox;
        private readonly object _lifetimeLock = new();
        private bool _disposed;

        private SvgImage(SKSvg document, SKRect viewBox)
        {
            _document = document;
            _viewBox = viewBox;
        }

        /// <summary>
        /// The natural size of the image in points. SVG user units are treated as points, matching
        /// the image APIs, where one source pixel occupies one point.
        /// </summary>
        public Size Size { get; private init; }

        /// <summary>
        /// Parses a self-contained SVG document from text.
        /// </summary>
        /// <exception cref="SvgParseException">
        /// The input is malformed, is not an SVG document, or declares a construct that is not
        /// accepted (scripts, DTDs, external resources, XML stylesheets).
        /// </exception>
        public static SvgImage FromText(string svgText)
        {
            if (svgText == null)
                throw new ArgumentNullException(nameof(svgText));

            return Load(SvgInputValidator.Parse(svgText));
        }

        /// <summary>
        /// Parses a self-contained SVG document from bytes.
        /// </summary>
        /// <exception cref="SvgParseException">
        /// The input is malformed, is not an SVG document, or declares a construct that is not
        /// accepted (scripts, DTDs, external resources, XML stylesheets).
        /// </exception>
        public static SvgImage FromBytes(byte[] svgData)
        {
            if (svgData == null)
                throw new ArgumentNullException(nameof(svgData));

            using var stream = new MemoryStream(svgData, writable: false);

            return FromStream(stream);
        }

        /// <summary>
        /// Parses a self-contained SVG document from a borrowed stream. The stream is read from its
        /// current position and is left open.
        /// </summary>
        /// <exception cref="SvgParseException">
        /// The input is malformed, is not an SVG document, or declares a construct that is not
        /// accepted (scripts, DTDs, external resources, XML stylesheets).
        /// </exception>
        public static SvgImage FromStream(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            using var content = ReadIntoMemory(stream);

            return Load(SvgInputValidator.Parse(content));
        }

        /// <summary>
        /// Parses a self-contained SVG document from a file.
        /// </summary>
        /// <exception cref="SvgParseException">
        /// The input is malformed, is not an SVG document, or declares a construct that is not
        /// accepted (scripts, DTDs, external resources, XML stylesheets).
        /// </exception>
        public static SvgImage FromFile(string filePath)
        {
            if (filePath == null)
                throw new ArgumentNullException(nameof(filePath));

            if (filePath.Length == 0)
                throw new ArgumentException("The file path cannot be empty.", nameof(filePath));

            using var stream = File.OpenRead(filePath);

            return FromStream(stream);
        }

        /// <summary>
        /// Draws the image into the provided space, preserving vector output and clipping to the
        /// destination area. Non-uniform scaling stretches the picture; the fluent fit modes keep
        /// the aspect ratio by measuring first.
        /// </summary>
        internal void Draw(SKCanvas canvas, Size availableSpace)
        {
            var picture = GetPicture();

            if (availableSpace.Width <= 0 || availableSpace.Height <= 0)
                return;

            canvas.Save();

            try
            {
                canvas.ClipRect(new SKRect(0, 0, availableSpace.Width, availableSpace.Height));
                canvas.Scale(availableSpace.Width / _viewBox.Width, availableSpace.Height / _viewBox.Height);
                canvas.Translate(-_viewBox.Left, -_viewBox.Top);
                canvas.DrawPicture(picture);
            }
            finally
            {
                canvas.Restore();
            }
        }

        /// <summary>
        /// Rasterizes the image for the explicit raster fallback. The caller owns the returned image.
        /// </summary>
        internal SKImage RenderRaster(Size availableSpace, float dpi, SKColor background)
        {
            var picture = GetPicture();

            if (!float.IsFinite(dpi) || dpi <= 0)
                throw new ArgumentOutOfRangeException(nameof(dpi), "The DPI value must be a positive finite number.");

            if (availableSpace.Width <= 0 || availableSpace.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(availableSpace), "The destination size must be positive.");

            var scale = dpi / 72f;
            var width = Math.Max(1, (int)Math.Ceiling(availableSpace.Width * scale));
            var height = Math.Max(1, (int)Math.Ceiling(availableSpace.Height * scale));

            var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(imageInfo);

            surface.Canvas.Clear(background);
            surface.Canvas.Scale(width / availableSpace.Width, height / availableSpace.Height);
            Draw(surface.Canvas, availableSpace);

            return surface.Snapshot();
        }

        private SKPicture GetPicture()
        {
            lock (_lifetimeLock)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(SvgImage));

                return _document.Picture
                    ?? throw new ObjectDisposedException(nameof(SvgImage));
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (_lifetimeLock)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _document.Dispose();
            }
        }

        private static SvgImage Load(XDocument document)
        {
            var svg = new SKSvg();

            try
            {
                svg.Settings.EnableJavaScript = false;
                svg.Settings.EnableExternalJavaScript = false;
                svg.Settings.EnableSvgFonts = false;
                svg.Settings.TypefaceProviders!.Add(new FossPdfTypefaceProvider());

                using var reader = document.CreateReader();
                var loadedPicture = svg.Load(reader)
                    ?? throw new SvgParseException("The SVG input does not contain a drawable document.");
                var viewBox = loadedPicture.CullRect;

                if (!IsFinite(viewBox.Left) || !IsFinite(viewBox.Top) ||
                    !IsFinite(viewBox.Width) || !IsFinite(viewBox.Height) ||
                    viewBox.Width <= 0 || viewBox.Height <= 0)
                {
                    throw new SvgParseException(
                        "The SVG document has no drawable area. A positive width and height or a viewBox is required.");
                }

                return new SvgImage(svg, viewBox)
                {
                    Size = new Size(viewBox.Width, viewBox.Height)
                };
            }
            catch (SvgParseException)
            {
                svg.Dispose();
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                svg.Dispose();
                throw new SvgParseException("The SVG input could not be parsed.", exception);
            }
        }

        private static MemoryStream ReadIntoMemory(Stream stream)
        {
            var content = new MemoryStream();
            var buffer = new byte[81920];
            long totalBytes = 0;

            while (true)
            {
                var bytesRead = stream.Read(buffer, 0, buffer.Length);

                if (bytesRead == 0)
                    break;

                totalBytes += bytesRead;

                if (totalBytes > SvgInputValidator.MaxInputSizeBytes)
                {
                    content.Dispose();
                    throw new SvgParseException(
                        "The SVG input exceeds the maximum supported size of " +
                        $"{SvgInputValidator.MaxInputSizeBytes / (1024 * 1024)} MiB.");
                }

                content.Write(buffer, 0, bytesRead);
            }

            content.Position = 0;

            return content;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
