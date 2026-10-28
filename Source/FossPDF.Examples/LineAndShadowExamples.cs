using System;
using System.IO;
using System.Linq;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.Examples
{
    /// <summary>
    /// FP-205: dash patterns and phases, gradient line paints, and box shadows. Dash geometry is
    /// rendered as vector quads in PDF and SVG, gradient lines use shader paints, and blurred
    /// shadows use the documented raster fallback where a backend has no blur support.
    /// </summary>
    public class LineAndShadowExamples
    {
        [Test]
        public void DashPatternsKeepTheirIntervalsAndPhase()
        {
            var document = CreateDocument(220, 40, 0, _ => { }, pages: 2, secondPagePhase: 5);

            var artifacts = CreateArtifactDirectory();
            var pdf = document.GeneratePdf();
            var images = document.GenerateImages(new ImageGenerationSettings { RasterDpi = 72 }).ToArray();
            var svg = document.GenerateSvg().ToArray();

            File.WriteAllBytes(Path.Combine(artifacts, "line-dashes.pdf"), pdf);
            File.WriteAllText(Path.Combine(artifacts, "line-dashes-1.svg"), svg[0]);
            File.WriteAllText(Path.Combine(artifacts, "line-dashes-2.svg"), svg[1]);

            for (var index = 0; index < images.Length; index++)
                File.WriteAllBytes(Path.Combine(artifacts, $"line-dashes-{index + 1}.png"), images[index]);

            using var firstPage = SKBitmap.Decode(images[0]);
            using var secondPage = SKBitmap.Decode(images[1]);

            var first = ScanDarkRuns(firstPage, y: 10);
            var second = ScanDarkRuns(secondPage, y: 10);

            Assert.That(first, Is.Not.Empty);
            Assert.That(first[0].Start, Is.EqualTo(10).Within(1));
            Assert.That(first[0].End, Is.EqualTo(22).Within(1));

            // Phase 5 starts five points inside the 12 point dash: the first dash ends at +7.
            Assert.That(second[0].Start, Is.EqualTo(10).Within(1));
            Assert.That(second[0].End, Is.EqualTo(17).Within(1));

            Assert.That(PdfText(pdf), Does.Not.Contain("/Subtype /Image"),
                "Dashes stay vector in PDF output.");
            Assert.That(svg.All(x => x.Contains("<path") && !x.Contains("<image")), Is.True,
                "Dashes stay vector in SVG output.");
        }

        [Test]
        public void GradientLinesInterpolateStartToEnd()
        {
            AssertGradientLine(
                new[] { "#ff0000", "#0000ff" },
                11, new SKColor(255, 0, 0),
                100, new SKColor(128, 0, 128),
                188, new SKColor(0, 0, 255));

            AssertGradientLine(
                new[] { "#ff0000", "#00ff00", "#0000ff" },
                11, new SKColor(255, 0, 0),
                100, new SKColor(0, 255, 0),
                188, new SKColor(0, 0, 255));

            // Transparent stops are interpolated without premultiplication: the transparent red
            // endpoint keeps its hue while only the alpha ramps up.
            AssertGradientLine(
                new[] { "#00ff0000", "#ff0000ff" },
                11, SKColors.White,
                100, new SKColor(191, 127, 191),
                188, new SKColor(0, 0, 255));
        }

        [Test]
        public void ShadowsAddDepthAndKeepTheirBackendFallbacks()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(320, 230);
                page.Margin(20);
                page.PageColor("#ffffff");

                page.Content().Column(column =>
                {
                    column.Spacing(16);

                    column.Item().Text("Box shadows: sharp, offset, spread and blurred")
                        .SemiBold().FontSize(12);

                    column.Item().Row(row =>
                    {
                        row.Spacing(14);

                        row.RelativeItem().Height(70).Background("#ffffff")
                            .CornerRadius(8)
                            .Shadow(new BoxShadowStyle
                            {
                                OffsetX = 3,
                                OffsetY = 4,
                                Color = "#80000000"
                            });

                        row.RelativeItem().Height(70).Background("#ffffff")
                            .CornerRadius(8)
                            .Shadow(new BoxShadowStyle
                            {
                                OffsetX = 0,
                                OffsetY = 6,
                                Spread = 4,
                                Blur = 8,
                                Color = "#552a9d8f"
                            });

                        row.RelativeItem().Height(70).Background("#ffffff")
                            .CornerRadius(8)
                            .Shadow(new BoxShadowStyle
                            {
                                OffsetX = 6,
                                OffsetY = 8,
                                Blur = 12,
                                Color = "#55336b9c"
                            });
                    });

                    column.Item().Text(
                            "The two blurred cards become a documented raster fallback: a PDF image " +
                            "at DocumentMetadata.RasterDpi and a 72 DPI image embedded in SVG. Sharp " +
                            "shadows stay vector.")
                        .FontSize(9)
                        .LineHeight(1.3f);
                });
            }));

            var artifacts = CreateArtifactDirectory();
            var pdf = document.GeneratePdf();
            var png = document.GenerateImages(new ImageGenerationSettings { RasterDpi = 96 }).Single();
            var svg = document.GenerateSvg().Single();

            File.WriteAllBytes(Path.Combine(artifacts, "shadows.pdf"), pdf);
            File.WriteAllBytes(Path.Combine(artifacts, "shadows.png"), png);
            File.WriteAllText(Path.Combine(artifacts, "shadows.svg"), svg);

            using var page = SKBitmap.Decode(png);

            Assert.That(CountPixels(page, IsShadowGray), Is.GreaterThan(100),
                "Sharp shadows paint a semi-transparent dark shape.");
            Assert.That(CountPixels(page, IsFaintInk), Is.GreaterThan(100),
                "Blurred shadows create partially covered falloff pixels.");

            var pdfText = PdfText(pdf);
            Assert.That(pdfText, Does.Contain("/Subtype /Image"),
                "Blurred shadows use the PDF raster fallback.");
            Assert.That(svg, Does.Contain("<image"),
                "Blurred shadows use the SVG raster fallback.");
            Assert.That(svg, Does.Not.Contain("<filter"),
                "The fallback does not claim vector filter support.");

            var secondSvg = document.GenerateSvg().Single();
            Assert.That(secondSvg, Is.EqualTo(svg), "The raster fallback is deterministic.");
        }

        [Test]
        public void SharpShadowsStayVector()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(160, 100);
                page.Margin(20);
                page.PageColor("#ffffff");

                page.Content().Height(40).Background("#ffffff").CornerRadius(8)
                    .Shadow(new BoxShadowStyle
                    {
                        OffsetX = 4,
                        OffsetY = 4,
                        Blur = 0,
                        Color = "#80000000"
                    });
            }));

            var pdf = document.GeneratePdf();
            var svg = document.GenerateSvg().Single();

            Assert.That(PdfText(pdf), Does.Not.Contain("/Subtype /Image"));
            Assert.That(svg, Does.Not.Contain("<image"));
            Assert.That(svg, Does.Contain("<path"));
        }

        private static void AssertGradientLine(
            string[] colors, int startX, SKColor start, int middleX, SKColor middle, int endX, SKColor end)
        {
            var document = CreateDocument(200, 20, 0, page => page.Content()
                .PaddingTop(10)
                .PaddingHorizontal(10)
                .LineHorizontal(8)
                .LineGradient(colors));

            var artifacts = CreateArtifactDirectory();
            var pdf = document.GeneratePdf();
            var png = document.GenerateImages(new ImageGenerationSettings { RasterDpi = 72 }).Single();
            var svg = document.GenerateSvg().Single();
            var name = string.Join("-", colors.Select(x => x.TrimStart('#')));

            File.WriteAllBytes(Path.Combine(artifacts, $"line-gradient-{name}.pdf"), pdf);
            File.WriteAllBytes(Path.Combine(artifacts, $"line-gradient-{name}.png"), png);
            File.WriteAllText(Path.Combine(artifacts, $"line-gradient-{name}.svg"), svg);

            using var bitmap = SKBitmap.Decode(png);

            AssertColor(bitmap.GetPixel(startX, 10), start, 8, "gradient start");
            AssertColor(bitmap.GetPixel(middleX, 10), middle, 10, "gradient middle");
            AssertColor(bitmap.GetPixel(endX, 10), end, 8, "gradient end");

            Assert.That(PdfText(pdf), Does.Contain("/ShadingType"),
                "PDF keeps the line gradient as a vector shading.");
            Assert.That(PdfText(pdf), Does.Not.Contain("/Subtype /Image"));
            Assert.That(svg, Does.Contain("linearGradient"));
            Assert.That(svg, Does.Not.Contain("<image"));
        }

        private static Document CreateDocument(
            int width,
            int height,
            int margin,
            Action<PageDescriptor> content,
            int pages = 1,
            float secondPagePhase = 0)
        {
            return Document.Create(container =>
            {
                for (var index = 0; index < pages; index++)
                {
                    var pageIndex = index;
                    container.Page(page =>
                    {
                        page.Size(width, height);
                        page.Margin(margin);
                        page.PageColor("#ffffff");

                        if (pages == 1)
                        {
                            content(page);
                            return;
                        }

                        page.Content()
                            .PaddingLeft(10)
                            .PaddingTop(10)
                            .Width(200)
                            .LineHorizontal(4)
                            .LineDashPattern(new[] { 12f, 6f }, pageIndex == 0 ? 0 : secondPagePhase);
                    });
                }
            });
        }

        private static System.Collections.Generic.List<(int Start, int End)> ScanDarkRuns(
            SKBitmap bitmap, int y)
        {
            var runs = new System.Collections.Generic.List<(int Start, int End)>();
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

        private static void AssertColor(SKColor actual, SKColor expected, int tolerance, string scenario)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual.Red, Is.EqualTo(expected.Red).Within(tolerance), scenario + " red");
                Assert.That(actual.Green, Is.EqualTo(expected.Green).Within(tolerance), scenario + " green");
                Assert.That(actual.Blue, Is.EqualTo(expected.Blue).Within(tolerance), scenario + " blue");
            });
        }

        private static bool IsShadowGray(SKColor color)
        {
            return color.Red is > 100 and < 160 &&
                   Math.Abs(color.Red - color.Green) < 8 &&
                   Math.Abs(color.Red - color.Blue) < 8;
        }

        private static bool IsFaintInk(SKColor color)
        {
            return (color.Red + color.Green + color.Blue) / 3 is > 170 and < 245;
        }

        private static int CountPixels(SKBitmap bitmap, Func<SKColor, bool> predicate)
        {
            var count = 0;

            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (predicate(bitmap.GetPixel(x, y)))
                    count++;
            }

            return count;
        }

        private static string PdfText(byte[] pdf) => System.Text.Encoding.Latin1.GetString(pdf);

        private static string CreateArtifactDirectory()
        {
            var directory = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "line-styles-example");

            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
