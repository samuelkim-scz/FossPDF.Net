using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.UnitTests.Parity
{
    /// <summary>
    /// FP-205 acceptance: dash intervals and phase, gradient line paints, and shadow geometry,
    /// blur profile and backend fallbacks. Dash geometry is measured from the raster output and
    /// compared with an independent implementation of the dash pattern, so the check does not
    /// depend on the renderer's own dash bookkeeping.
    /// </summary>
    public partial class ParityAcceptanceTests
    {
        [ParityFixtureRunner("GFX-205")]

        private static void RunLineStylesAndShadows(ParityFixture fixture)
        {
            AssertDashGeometry(fixture);
            AssertDashUnderTransform(fixture);
            AssertGradientLine(fixture);
            AssertSharpShadowGeometry(fixture);
            AssertShadowIsPaintedBehindContent(fixture);
            AssertBlurredShadowProfile(fixture);
            AssertShadowBackendFallbacks(fixture);
        }

        private static void AssertDashGeometry(ParityFixture fixture)
        {
            var line = fixture.Input.GetProperty("line");
            var from = ReadPoint(line, "from");
            var to = ReadPoint(line, "to");
            var width = line.GetProperty("width").GetInt32();
            var dash = ReadFloats(line, "dash");
            var phase = line.GetProperty("phase").GetSingle();
            var scale = fixture.Input.GetProperty("dpi").GetInt32() / 72f;

            var document = CreateLinePage(fixture, container => container
                .LineHorizontal(width)
                .LineDashPattern(dash, phase));

            var (pdf, png) = RenderLineScenario(fixture, "dash", document);
            using var bitmap = SKBitmap.Decode(png);

            Assert.That(bitmap, Is.Not.Null);
            Assert.That(bitmap!.Width, Is.EqualTo((int) (fixture.Input.GetProperty("page")[0].GetInt32() * scale)));
            Assert.That(bitmap.Height, Is.EqualTo((int) (fixture.Input.GetProperty("page")[1].GetInt32() * scale)));

            var runs = ScanDarkRuns(bitmap, (int) (from.Y * scale));
            var expected = ComputeDashIntervals(from.X, to.X, dash, phase);

            Assert.That(runs, Has.Count.EqualTo(expected.Count),
                "GFX-205: the dash count must match the declared pattern.");

            for (var index = 0; index < expected.Count; index++)
            {
                Assert.That(runs[index].Start / scale, Is.EqualTo(expected[index].Start).Within(0.5f),
                    $"GFX-205: dash {index} starts at the requested point.");
                Assert.That(runs[index].End / scale, Is.EqualTo(expected[index].End).Within(0.5f),
                    $"GFX-205: dash {index} ends at the requested point.");
            }

            // Phase 2 starts the pattern 2pt inside the first 6pt dash, so it ends at +4pt.
            Assert.That(runs[0].Start / scale, Is.EqualTo(from.X).Within(0.5f));
            Assert.That(runs[0].End / scale, Is.EqualTo(from.X + 4).Within(0.5f));

            var pdfText = Encoding.Latin1.GetString(pdf);
            Assert.That(pdfText, Does.Not.Contain("/Subtype /Image"),
                "GFX-205: dashes are vectors in PDF output.");
            Assert.That(Regex.Matches(DecodePdfStreams(pdf), " l\n").Count, Is.GreaterThan(20),
                "GFX-205: the PDF content stream contains one quad per dash.");

            var svg = document.GenerateSvg().Single();
            SaveArtifact(fixture.Id, "scenario-dash/document.svg", Encoding.UTF8.GetBytes(svg));
            Assert.That(svg, Does.Contain("<path"), "GFX-205: dashes are paths in SVG output.");
            Assert.That(svg, Does.Not.Contain("<image"), "GFX-205: dashes must not be rasterized for SVG.");
        }

        private static void AssertDashUnderTransform(ParityFixture fixture)
        {
            var line = fixture.Input.GetProperty("line");
            var from = ReadPoint(line, "from");
            var to = ReadPoint(line, "to");
            var width = line.GetProperty("width").GetInt32();
            var dash = ReadFloats(line, "dash");
            var phase = line.GetProperty("phase").GetSingle();
            var scale = fixture.Input.GetProperty("dpi").GetInt32() / 72f;

            var document = CreateLinePage(fixture, container => container
                .Height(20)
                .Scale(2)
                .LineHorizontal(width)
                .LineDashPattern(dash, phase));

            var (_, png) = RenderLineScenario(fixture, "dash-scaled", document);
            using var bitmap = SKBitmap.Decode(png);

            var runs = ScanDarkRuns(bitmap!, (int) (from.Y * scale));

            // A canvas scale of two doubles dash and gap lengths in device space, exactly like a
            // stroked dash would.
            var expected = ComputeDashIntervals(
                from.X, to.X, dash.Select(x => x * 2).ToArray(), phase * 2);

            Assert.That(runs, Has.Count.EqualTo(expected.Count),
                "GFX-205: the scaled dash count must stay stable.");

            for (var index = 0; index < expected.Count; index++)
            {
                Assert.That(runs[index].Start / scale, Is.EqualTo(expected[index].Start).Within(0.5f),
                    $"GFX-205: scaled dash {index} starts at the scaled position.");
                Assert.That(runs[index].End / scale, Is.EqualTo(expected[index].End).Within(0.5f),
                    $"GFX-205: scaled dash {index} keeps its scaled length.");
            }
        }

        private static void AssertGradientLine(ParityFixture fixture)
        {
            var line = fixture.Input.GetProperty("line");
            var from = ReadPoint(line, "from");
            var to = ReadPoint(line, "to");
            var width = line.GetProperty("width").GetInt32();
            var colors = ReadStrings(line, "gradient");
            var scale = fixture.Input.GetProperty("dpi").GetInt32() / 72f;

            var document = CreateLinePage(fixture, container => container
                .LineHorizontal(width)
                .LineGradient(colors));

            var (pdf, png) = RenderLineScenario(fixture, "gradient", document);
            using var bitmap = SKBitmap.Decode(png);

            var expectedStart = SKColor.Parse(colors[0]);
            var expectedEnd = SKColor.Parse(colors[1]);
            var expectedMiddle = new SKColor(
                (byte) ((expectedStart.Red + expectedEnd.Red) / 2),
                (byte) ((expectedStart.Green + expectedEnd.Green) / 2),
                (byte) ((expectedStart.Blue + expectedEnd.Blue) / 2));

            var row = (int) (from.Y * scale);

            AssertColorChannels(bitmap!.GetPixel((int) ((from.X + 2) * scale), row), expectedStart, 8,
                "gradient start");
            AssertColorChannels(bitmap.GetPixel((int) ((from.X + to.X) / 2 * scale), row), expectedMiddle, 8,
                "gradient midpoint");
            AssertColorChannels(bitmap.GetPixel((int) ((to.X - 2) * scale), row), expectedEnd, 8,
                "gradient end");

            var pdfText = Encoding.Latin1.GetString(pdf);
            Assert.That(pdfText, Does.Contain("/ShadingType"),
                "GFX-205: the PDF keeps the line gradient as a vector shading.");
            Assert.That(pdfText, Does.Not.Contain("/Subtype /Image"),
                "GFX-205: the line gradient must not be rasterized for PDF.");

            var svg = document.GenerateSvg().Single();
            SaveArtifact(fixture.Id, "scenario-gradient/document.svg", Encoding.UTF8.GetBytes(svg));
            Assert.That(svg, Does.Contain("linearGradient"),
                "GFX-205: SVG keeps the line gradient as a paint server.");
            Assert.That(svg, Does.Not.Contain("<image"),
                "GFX-205: the line gradient must not be rasterized for SVG.");
        }

        private static void AssertSharpShadowGeometry(ParityFixture fixture)
        {
            var shadow = fixture.Input.GetProperty("shadow");
            var box = ReadFloats(shadow, "box");
            var offset = ReadFloats(shadow, "offset");
            var spread = shadow.GetProperty("spread").GetSingle();
            var scale = fixture.Input.GetProperty("dpi").GetInt32() / 72f;

            var document = CreateShadowPage(fixture, blur: 0);

            var (pdf, png) = RenderLineScenario(fixture, "shadow-sharp", document);
            using var bitmap = SKBitmap.Decode(png);

            var left = (box[0] + offset[0] - spread) * scale;
            var top = (box[1] + offset[1] - spread) * scale;
            var right = (box[0] + box[2] + offset[0] + spread) * scale - 1;
            var bottom = (box[1] + box[3] + offset[1] + spread) * scale - 1;
            var bounds = ReadInkBounds(bitmap!);

            Assert.Multiple(() =>
            {
                Assert.That(bounds.Left, Is.EqualTo(left).Within(1.5f),
                    "GFX-205: the shadow left extent follows the offset and spread.");
                Assert.That(bounds.Top, Is.EqualTo(top).Within(1.5f),
                    "GFX-205: the shadow top extent follows the offset and spread.");
                Assert.That(bounds.Right, Is.EqualTo(right).Within(1.5f),
                    "GFX-205: the shadow right extent follows the offset and spread.");
                Assert.That(bounds.Bottom, Is.EqualTo(bottom).Within(1.5f),
                    "GFX-205: the shadow bottom extent follows the offset and spread.");
            });

            // The shadow color is #80000000, so the shadow is a half-transparent black over the
            // white page: about 127 in every channel.
            AssertColorChannels(
                bitmap.GetPixel((int) ((box[0] + box[2] / 2 + offset[0]) * scale),
                    (int) ((box[1] + box[3] / 2 + offset[1]) * scale)),
                new SKColor(127, 127, 127), 4, "shadow center");

            AssertColorChannels(
                bitmap.GetPixel((int) ((box[0] - 3) * scale), (int) ((box[1] + box[3] / 2) * scale)),
                SKColors.White, 3, "outside the shadow");

            var pdfText = Encoding.Latin1.GetString(pdf);
            Assert.That(pdfText, Does.Not.Contain("/Subtype /Image"),
                "GFX-205: a sharp shadow stays vector in PDF output.");

            var svg = document.GenerateSvg().Single();
            SaveArtifact(fixture.Id, "scenario-shadow-sharp/document.svg", Encoding.UTF8.GetBytes(svg));
            Assert.That(svg, Does.Not.Contain("<image"),
                "GFX-205: a sharp shadow stays vector in SVG output.");
            Assert.That(svg, Does.Contain("<path"),
                "GFX-205: the rounded shadow is a vector path in SVG output.");
        }

        private static void AssertShadowIsPaintedBehindContent(ParityFixture fixture)
        {
            var shadow = fixture.Input.GetProperty("shadow");
            var box = ReadFloats(shadow, "box");
            var offset = ReadFloats(shadow, "offset");
            var spread = shadow.GetProperty("spread").GetSingle();
            var scale = fixture.Input.GetProperty("dpi").GetInt32() / 72f;

            var document = CreateShadowPage(fixture, blur: 0, background: "#ff0000");
            var (_, png) = RenderLineScenario(fixture, "shadow-behind-content", document);
            using var bitmap = SKBitmap.Decode(png);

            var centerX = (box[0] + box[2] / 2) * scale;
            var centerY = (box[1] + box[3] / 2) * scale;

            AssertColorChannels(bitmap!.GetPixel((int) centerX, (int) centerY),
                new SKColor(255, 0, 0), 3, "background covers the shadow");

            // The shadow peeks out on the right and bottom, because the offset moves it outside
            // the background rectangle.
            AssertColorChannels(
                bitmap.GetPixel((int) ((box[0] + box[2] + offset[0] + spread - 1) * scale),
                    (int) centerY),
                new SKColor(127, 127, 127), 6, "shadow outside the content");
        }

        private static void AssertBlurredShadowProfile(ParityFixture fixture)
        {
            var shadow = fixture.Input.GetProperty("shadow");
            var box = ReadFloats(shadow, "box");
            var offset = ReadFloats(shadow, "offset");
            var spread = shadow.GetProperty("spread").GetSingle();
            var blur = shadow.GetProperty("blur").GetSingle();
            var scale = fixture.Input.GetProperty("dpi").GetInt32() / 72f;

            var document = CreateShadowPage(fixture, blur: blur);
            var (_, png) = RenderLineScenario(fixture, "shadow-blurred", document);
            using var bitmap = SKBitmap.Decode(png);

            var sigma = blur / 2;
            var leftEdge = box[0] + offset[0] - spread;
            var middleY = box[1] + box[3] / 2 + offset[1];

            // The shadow is #80000000 over white, so each channel is 255 - blurred alpha.
            AssertGaussianShadowPixel(bitmap!, leftEdge + 30, middleY, leftEdge, scale, sigma, 128,
                "shadow center");
            AssertGaussianShadowPixel(bitmap!, leftEdge, middleY, leftEdge, scale, sigma, 128,
                "shadow edge");
            AssertGaussianShadowPixel(bitmap!, leftEdge - 2.5f, middleY, leftEdge, scale, sigma, 128,
                "shadow falloff");
            AssertGaussianShadowPixel(bitmap!, leftEdge - 5, middleY, leftEdge, scale, sigma, 128,
                "shadow outer falloff");
            AssertGaussianShadowPixel(bitmap!, leftEdge - 9, middleY, leftEdge, scale, sigma, 128,
                "shadow extent");
        }

        private static void AssertShadowBackendFallbacks(ParityFixture fixture)
        {
            var document = CreateShadowPage(fixture, blur: 5);

            var pdf = document.GeneratePdf();
            var pdf72 = document
                .WithSettings(new DocumentSettings { ImageRasterDpi = 72 })
                .GeneratePdf();

            var text = Encoding.Latin1.GetString(pdf);
            var text72 = Encoding.Latin1.GetString(pdf72);

            Assert.That(text, Does.Contain("/Subtype /Image"),
                "GFX-205: blurred shadows use the PDF raster fallback.");
            Assert.That(text72, Does.Contain("/Subtype /Image"));

            var size = ReadFirstImageSize(text);
            var size72 = ReadFirstImageSize(text72);

            Assert.That(size, Is.Not.Null, "GFX-205: the blurred shadow image is embedded in the PDF.");
            Assert.That(size72, Is.Not.Null);

            // The fixture metadata uses 288 DPI, so the embedded image is about four times larger
            // than the 72-DPI fallback. Integer pixel dimensions round the exact ratio.
            var ratio = (float) size!.Value.Width / size72!.Value.Width;
            Assert.That(ratio, Is.InRange(288f / 72f * 0.94f, 288f / 72f * 1.06f),
                "GFX-205: DocumentMetadata.RasterDpi controls the PDF shadow resolution.");
            Assert.That(size.Value.Height, Is.GreaterThan(size72.Value.Height * 3),
                "GFX-205: the raster fallback scales in both dimensions.");

            var svg = document.GenerateSvg().Single();
            SaveArtifact(fixture.Id, "scenario-shadow-blurred/document.svg",
                Encoding.UTF8.GetBytes(svg));
            Assert.That(svg, Does.Contain("<image"),
                "GFX-205: SVG has no mask-filter support, so the blur is embedded as a raster image.");
            Assert.That(svg, Does.Not.Contain("<filter"),
                "GFX-205: the documented SVG fallback does not claim a vector filter.");

            var first = document.GenerateSvg().Single();
            for (var index = 0; index < 20; index++)
                Assert.That(document.GenerateSvg().Single(), Is.EqualTo(first),
                    "GFX-205: the raster fallback is deterministic and disposes its temporary surfaces.");

            Assert.That(document.GeneratePdf(), Is.EqualTo(document.GeneratePdf()),
                "GFX-205: equal documents produce identical PDF bytes.");
        }

        private static Document CreateLinePage(ParityFixture fixture, Action<IContainer> configure)
        {
            var line = fixture.Input.GetProperty("line");
            var from = ReadPoint(line, "from");
            var to = ReadPoint(line, "to");

            return CreatePage(fixture, page =>
            {
                var container = page.Content()
                    .PaddingLeft(from.X)
                    .PaddingTop(from.Y)
                    .Width(to.X - from.X);

                configure(container);
            });
        }

        private static Document CreateShadowPage(
            ParityFixture fixture, float blur, string background = null)
        {
            var shadow = fixture.Input.GetProperty("shadow");
            var box = ReadFloats(shadow, "box");
            var radius = shadow.GetProperty("radius").GetSingle();
            var offset = ReadFloats(shadow, "offset");
            var spread = shadow.GetProperty("spread").GetSingle();
            var color = shadow.GetProperty("color").GetString()!;

            return CreatePage(fixture, page =>
            {
                var container = page.Content()
                    .PaddingLeft(box[0])
                    .PaddingTop(box[1])
                    .Width(box[2])
                    .Height(box[3])
                    .CornerRadius(radius)
                    .Shadow(new BoxShadowStyle
                    {
                        OffsetX = offset[0],
                        OffsetY = offset[1],
                        Spread = spread,
                        Blur = blur,
                        Color = color
                    });

                if (background != null)
                    container.Background(background);
            });
        }

        private static (byte[] Pdf, byte[] Png) RenderLineScenario(
            ParityFixture fixture, string scenario, Document document)
        {
            var pdf = document.GeneratePdf(out var count);
            var pages = document.GenerateImages().ToArray();

            Assert.That(count, Is.EqualTo(1));
            Assert.That(pages, Has.Length.EqualTo(1));

            SaveArtifact(fixture.Id, $"scenario-{scenario}/document.pdf", pdf);
            SaveArtifact(fixture.Id, $"scenario-{scenario}/page-1.png", pages[0]);

            return (pdf, pages[0]);
        }

        private static List<(float Start, float End)> ComputeDashIntervals(
            float from, float to, float[] pattern, float phase)
        {
            var total = pattern.Sum();
            var offset = phase % total;

            if (offset < 0)
                offset += total;

            var index = 0;

            while (offset >= pattern[index])
            {
                offset -= pattern[index];
                index = (index + 1) % pattern.Length;
            }

            var remaining = pattern[index] - offset;
            var isDash = index % 2 == 0;
            var position = 0f;
            var length = to - from;
            var result = new List<(float Start, float End)>();

            while (position < length)
            {
                var segment = Math.Min(remaining, length - position);

                if (isDash && segment > 0)
                    result.Add((from + position, from + position + segment));

                position += segment;
                remaining -= segment;

                if (remaining <= 0.0001f)
                {
                    index = (index + 1) % pattern.Length;
                    remaining = pattern[index];
                    isDash = !isDash;
                }
            }

            return result;
        }

        private static List<(int Start, int End)> ScanDarkRuns(SKBitmap bitmap, int y)
        {
            var runs = new List<(int Start, int End)>();
            var start = -1;

            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                var dark = (color.Red + color.Green + color.Blue) / 3f < 128;

                if (dark && start < 0)
                    start = x;

                if (!dark && start >= 0)
                {
                    runs.Add((start, x));
                    start = -1;
                }
            }

            if (start >= 0)
                runs.Add((start, bitmap.Width));

            return runs;
        }

        private static (int Left, int Top, int Right, int Bottom) ReadInkBounds(SKBitmap bitmap)
        {
            var left = int.MaxValue;
            var top = int.MaxValue;
            var right = int.MinValue;
            var bottom = int.MinValue;

            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);

                if (color.Red >= 250 && color.Green >= 250 && color.Blue >= 250)
                    continue;

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }

            Assert.That(left, Is.Not.EqualTo(int.MaxValue), "GFX-205: the scenario must render ink.");

            return (left, top, right, bottom);
        }

        private static void AssertGaussianShadowPixel(
            SKBitmap bitmap,
            float pointX,
            float pointY,
            float edgeX,
            float scale,
            float sigma,
            float alpha,
            string scenario)
        {
            var pixelX = (int) (pointX * scale);
            var pixelY = (int) (pointY * scale);

            // Signed distance from the unblurred shadow edge, measured at the pixel center.
            var distance = (pixelX + 0.5f) / scale - edgeX;
            var expectedAlpha = alpha * 0.5f * (float) Erfc205(-distance / (sigma * (float) Math.Sqrt(2)));
            var expected = 255 - expectedAlpha;
            var actual = bitmap.GetPixel(pixelX, pixelY);

            foreach (var channel in new[] { actual.Red, actual.Green, actual.Blue })
            {
                Assert.That(channel, Is.EqualTo(expected).Within(8),
                    $"GFX-205 {scenario}: channel at ({pixelX},{pixelY}) was {actual}, expected about {expected:F1}.");
            }
        }

        private static void AssertColorChannels(
            SKColor actual, SKColor expected, int tolerance, string scenario)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual.Red, Is.EqualTo(expected.Red).Within(tolerance),
                    $"GFX-205 {scenario}: red channel was {actual}.");
                Assert.That(actual.Green, Is.EqualTo(expected.Green).Within(tolerance),
                    $"GFX-205 {scenario}: green channel was {actual}.");
                Assert.That(actual.Blue, Is.EqualTo(expected.Blue).Within(tolerance),
                    $"GFX-205 {scenario}: blue channel was {actual}.");
            });
        }

        private static double Erfc205(double value)
        {
            var sign = Math.Sign(value);
            var x = Math.Abs(value);
            var t = 1 / (1 + 0.3275911 * x);
            var polynomial = t * (0.254829592
                + t * (-0.284496736
                + t * (1.421413741
                + t * (-1.453152027
                + t * 1.061405429))));

            return 1 - sign * (1 - polynomial * Math.Exp(-x * x));
        }

        private static (int Width, int Height)? ReadFirstImageSize(string pdf)
        {
            var match = Regex.Match(pdf, @"/Subtype /Image[^>]*?/Width (\d+)[^>]*?/Height (\d+)");

            if (!match.Success)
                match = Regex.Match(pdf, @"/Width (\d+)[^>]*?/Height (\d+)[^>]*?/Subtype /Image");

            if (!match.Success)
                return null;

            return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
        }

        private static (float X, float Y) ReadPoint(JsonElement element, string property)
        {
            var values = ReadFloats(element, property);
            Assert.That(values, Has.Length.EqualTo(2));
            return (values[0], values[1]);
        }

        private static float[] ReadFloats(JsonElement element, string property)
        {
            return FixtureCatalog.ReadArray(element, property).Select(x => x.GetSingle()).ToArray();
        }

        private static string[] ReadStrings(JsonElement element, string property)
        {
            return FixtureCatalog.ReadArray(element, property).Select(x => x.GetString()!).ToArray();
        }
    }
}
