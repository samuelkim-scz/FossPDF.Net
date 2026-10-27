using System;
using System.Linq;
using System.Text;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.UnitTests.Parity
{
    /// <summary>
    /// FP-204 acceptance: rounded containers, border alignment, gradients and clipping survive the
    /// PDF, PNG and SVG output paths. Geometry tolerances are derived from the fixture DPI: the
    /// contract allows 0.25 point, which is one pixel at 288 DPI.
    /// </summary>
    public partial class ParityAcceptanceTests
    {
        [ParityFixtureRunner("GFX-204")]

        private static void RunContainerStyling(ParityFixture fixture)
        {
            AssertBorderAlignment(fixture, "inside", BorderAlignment.Inside);
            AssertBorderAlignment(fixture, "center", BorderAlignment.Center);
            AssertBorderAlignment(fixture, "outside", BorderAlignment.Outside);

            AssertRoundedCornerCutout(fixture);
            AssertGradientInterpolation(fixture);
            AssertNestedClipping(fixture);
            AssertTransformedClipping(fixture);
        }

        private static void AssertBorderAlignment(
            ParityFixture fixture, string name, BorderAlignment alignment)
        {
            var input = fixture.Input;
            var box = ReadBox(fixture);
            var width = input.GetProperty("border_width").GetInt32();
            var scale = input.GetProperty("dpi").GetInt32() / 72f;

            var document = CreateStyledPage(fixture, container => container
                .Border(width)
                .BorderAlignment(alignment)
                .BorderColor("#000000"));

            var (pdf, png) = RenderStyledScenario(fixture, "border-" + name, document);

            var expansion = alignment switch
            {
                BorderAlignment.Inside => 0f,
                BorderAlignment.Center => width / 2f,
                _ => width
            };

            var expectedLeft = (box.Left - expansion) * scale;
            var expectedTop = (box.Top - expansion) * scale;
            var expectedRight = (box.Left + box.Width + expansion) * scale - 1;
            var expectedBottom = (box.Top + box.Height + expansion) * scale - 1;

            using var bitmap = SKBitmap.Decode(png);
            Assert.That(bitmap, Is.Not.Null);
            var bounds = GetInkBounds(bitmap);

            Assert.Multiple(() =>
            {
                Assert.That(bounds.Left, Is.InRange(expectedLeft - 1, expectedLeft + 1),
                    $"GFX-204 {name}: the border must start at the requested horizontal placement.");
                Assert.That(bounds.Top, Is.InRange(expectedTop - 1, expectedTop + 1),
                    $"GFX-204 {name}: the border must start at the requested vertical placement.");
                Assert.That(bounds.Right, Is.InRange(expectedRight - 1, expectedRight + 1),
                    $"GFX-204 {name}: the border must end at the requested horizontal placement.");
                Assert.That(bounds.Bottom, Is.InRange(expectedBottom - 1, expectedBottom + 1),
                    $"GFX-204 {name}: the border must end at the requested vertical placement.");
            });

            // Alignment changes pixels only through geometry: the ring must stay inside the page.
            Assert.That(bitmap.Width, Is.EqualTo(160 * scale));
            Assert.That(bitmap.Height, Is.EqualTo(120 * scale));

            var pdfText = Encoding.Latin1.GetString(pdf);
            Assert.That(pdfText, Does.Not.Contain("/Subtype /Image"),
                $"GFX-204 {name}: the border is painted as vector geometry, not rasterized.");
        }

        private static void AssertRoundedCornerCutout(ParityFixture fixture)
        {
            var input = fixture.Input;
            var box = ReadBox(fixture);
            var radius = input.GetProperty("radius").GetInt32();
            var borderWidth = input.GetProperty("border_width").GetInt32();
            var scale = input.GetProperty("dpi").GetInt32() / 72f;

            var document = CreateStyledPage(fixture, container => container
                .Border(borderWidth)
                .BorderAlignment(BorderAlignment.Inside)
                .BorderColor("#000000")
                .CornerRadius(radius));

            var (_, png) = RenderStyledScenario(fixture, "corner-cutout", document);

            using var bitmap = SKBitmap.Decode(png);

            // The corner arc starts at (top + radius). A point between the box corner and the arc is
            // outside the shape; a point one point further in is inside the border ring; the area
            // inside the inner radius stays empty.
            AssertPixel(bitmap, (box.Left + 2) * scale, (box.Top + 2) * scale, SKColors.White,
                "corner cutout");
            AssertPixel(bitmap, (box.Left + 4) * scale, (box.Top + 4) * scale, SKColors.Black,
                "border ring after the arc");
            AssertPixel(bitmap, (box.Left + radius + 1) * scale, (box.Top + radius - 1) * scale,
                SKColors.White, "transparent hole");

            // The rounded rectangle still reaches the straight edges of the box.
            AssertPixel(bitmap, (box.Left + radius) * scale, box.Top * scale + 1, SKColors.Black,
                "straight top edge");
        }

        private static void AssertGradientInterpolation(ParityFixture fixture)
        {
            var input = fixture.Input;
            var box = ReadBox(fixture);
            var gradient = input.GetProperty("gradient");
            var from = gradient.GetProperty("from").GetString();
            var to = gradient.GetProperty("to").GetString();
            var scale = input.GetProperty("dpi").GetInt32() / 72f;

            var document = CreateStyledPage(fixture, container => container
                .BackgroundGradient(from, to, GradientDirection.Horizontal));

            var (pdf, png) = RenderStyledScenario(fixture, "gradient-horizontal", document);

            using var bitmap = SKBitmap.Decode(png);
            var middleY = (box.Top + box.Height / 2f) * scale;

            AssertPixel(bitmap, (box.Left + 1) * scale, middleY, new SKColor(255, 0, 0),
                "gradient start", tolerance: 8);
            AssertPixel(bitmap, (box.Left + box.Width / 2f) * scale, middleY,
                new SKColor(128, 0, 128), "gradient midpoint", tolerance: 8);
            AssertPixel(bitmap, (box.Left + box.Width - 1) * scale, middleY, new SKColor(0, 0, 255),
                "gradient end", tolerance: 8);

            var pdfText = Encoding.Latin1.GetString(pdf);
            Assert.That(pdfText, Does.Contain("/ShadingType"),
                "GFX-204: the PDF keeps the gradient as a vector shading.");
            Assert.That(pdfText, Does.Not.Contain("/Subtype /Image"),
                "GFX-204: the gradient must not be rasterized for PDF output.");

            var svg = document.GenerateSvg().Single();
            SaveArtifact(fixture.Id, "scenario-gradient-horizontal/document.svg",
                Encoding.UTF8.GetBytes(svg));
            Assert.That(svg, Does.Contain("linearGradient"),
                "GFX-204: SVG keeps the gradient as a vector paint server.");
        }

        private static void AssertNestedClipping(ParityFixture fixture)
        {
            var box = ReadBox(fixture);
            var radius = fixture.Input.GetProperty("radius").GetInt32();
            var scale = fixture.Input.GetProperty("dpi").GetInt32() / 72f;

            var document = CreateStyledPage(fixture, container => container
                .CornerRadius(radius)
                .Clip()
                .Scale(2)
                .Background("#00ff00"));

            var (pdf, png) = RenderStyledScenario(fixture, "clip-nested", document);

            using var bitmap = SKBitmap.Decode(png);
            AssertPixel(bitmap, (box.Left + 1) * scale, (box.Top + 1) * scale, SKColors.White,
                "clipped corner");
            AssertPixel(bitmap, (box.Left + box.Width / 2f) * scale,
                (box.Top + box.Height / 2f) * scale, new SKColor(0, 255, 0), "clipped center");

            var content = DecodePdfStreams(pdf);
            Assert.That(content, Does.Match(@"W\s*n"),
                "GFX-204: the PDF content stream clips the nested child.");

            var svg = document.GenerateSvg().Single();
            SaveArtifact(fixture.Id, "scenario-clip-nested/document.svg",
                Encoding.UTF8.GetBytes(svg));
            Assert.That(svg, Does.Contain("clipPath"),
                "GFX-204: SVG keeps the rounded clip.");
        }

        private static void AssertTransformedClipping(ParityFixture fixture)
        {
            var box = ReadBox(fixture);
            var radius = fixture.Input.GetProperty("radius").GetInt32();
            var scale = fixture.Input.GetProperty("dpi").GetInt32() / 72f;

            var document = CreateStyledPage(fixture, container => container
                .CornerRadius(radius)
                .Clip()
                .Scale(2)
                .Rotate(15)
                .Background("#00ff00"));

            var (_, png) = RenderStyledScenario(fixture, "clip-transformed", document);

            using var bitmap = SKBitmap.Decode(png);
            AssertPixel(bitmap, (box.Left + 1) * scale, (box.Top + 1) * scale, SKColors.White,
                "transformed clipped corner");
            AssertPixel(bitmap, (box.Left + box.Width / 2f) * scale,
                (box.Top + box.Height / 2f) * scale, new SKColor(0, 255, 0),
                "transformed clipped center");

            var svg = document.GenerateSvg().Single();
            SaveArtifact(fixture.Id, "scenario-clip-transformed/document.svg",
                Encoding.UTF8.GetBytes(svg));
            Assert.That(svg, Does.Contain("clipPath"),
                "GFX-204: SVG keeps the rounded clip around the transformed child.");
            Assert.That(svg, Does.Match("matrix\\(|rotate\\("),
                "GFX-204: SVG keeps the child transform inside the clip.");
        }

        private static Document CreateStyledPage(ParityFixture fixture, Action<IContainer> style)
        {
            var box = ReadBox(fixture);

            return CreatePage(fixture, page =>
            {
                var container = page.Content()
                    .PaddingLeft(box.Left)
                    .PaddingTop(box.Top)
                    .Width(box.Width)
                    .Height(box.Height);

                style(container);
            });
        }

        private static (int Left, int Top, int Width, int Height) ReadBox(ParityFixture fixture)
        {
            var box = FixtureCatalog.ReadArray(fixture.Input, "box").Select(x => x.GetInt32()).ToArray();

            Assert.That(box, Has.Length.EqualTo(4), "GFX-204: the box is declared as left, top, width, height.");

            return (box[0], box[1], box[2], box[3]);
        }

        private static (byte[] Pdf, byte[] Png) RenderStyledScenario(
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

        private static void AssertPixel(
            SKBitmap bitmap, float x, float y, SKColor expected, string scenario, int tolerance = 2)
        {
            var actual = bitmap.GetPixel((int) x, (int) y);

            Assert.Multiple(() =>
            {
                Assert.That(actual.Red, Is.InRange(expected.Red - tolerance, expected.Red + tolerance),
                    $"GFX-204 {scenario}: red channel at ({x},{y}) was {actual}.");
                Assert.That(actual.Green, Is.InRange(expected.Green - tolerance, expected.Green + tolerance),
                    $"GFX-204 {scenario}: green channel at ({x},{y}) was {actual}.");
                Assert.That(actual.Blue, Is.InRange(expected.Blue - tolerance, expected.Blue + tolerance),
                    $"GFX-204 {scenario}: blue channel at ({x},{y}) was {actual}.");
            });
        }

        private static (int Left, int Top, int Right, int Bottom) GetInkBounds(SKBitmap bitmap)
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

            Assert.That(left, Is.Not.EqualTo(int.MaxValue),
                "GFX-204: the styled container must render visible ink.");

            return (left, top, right, bottom);
        }
    }
}
