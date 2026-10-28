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
    /// FP-204: rounded containers, border alignment, gradients and rounded clipping. Every fluent
    /// styling call targets one decoration, so background, border, radius and clip can be chained in
    /// any order and still describe the same rectangle.
    /// </summary>
    public class ContainerStylingExamples
    {
        private const int PageWidth = 300;
        private const int PageHeight = 360;
        private const int PageMargin = 20;

        [Test]
        public void BorderAlignmentPositionsTheBorderAroundTheContentBox()
        {
            var document = CreateDocument(page => page.Content().Column(column =>
            {
                column.Spacing(14);

                column.Item().Text("Inside, center and outside border alignment")
                    .SemiBold().FontSize(12);

                column.Item().Row(row =>
                {
                    row.Spacing(10);

                    AddCard(row.RelativeItem(), BorderAlignment.Inside, "#e63946");
                    AddCard(row.RelativeItem(), BorderAlignment.Center, "#2a9d8f");
                    AddCard(row.RelativeItem(), BorderAlignment.Outside, "#457b9d");
                });

                column.Item().Text(
                        "Each card has a 6 point border and a 10 point corner radius. The three " +
                        "alignments paint the same width inside, on and outside the layout bounds.")
                    .FontSize(10)
                    .LineHeight(1.3f);

                column.Item().Row(row =>
                {
                    row.Spacing(10);

                    row.RelativeItem().Height(50).Background("#f1faee")
                        .Border(4).BorderColor("#1d3557")
                        .BorderAlignment(BorderAlignment.Inside)
                        .CornerRadius(8);

                    row.RelativeItem().Height(50).Background("#a8dadc")
                        .Border(4).BorderColor("#1d3557")
                        .BorderAlignment(BorderAlignment.Outside)
                        .CornerRadius(20);
                });
            }));

            var artifacts = CreateArtifactDirectory();
            var pdf = document.GeneratePdf();
            var pages = document.GenerateImages(new ImageGenerationSettings { RasterDpi = 96 });

            File.WriteAllBytes(Path.Combine(artifacts, "container-border-alignment.pdf"), pdf);
            File.WriteAllBytes(Path.Combine(artifacts, "container-border-alignment.png"), pages.Single());

            using var page = SKBitmap.Decode(pages.Single());

            Assert.That(CountPixels(page, color => IsNear(color, 0xe6, 0x39, 0x46)), Is.GreaterThan(0),
                "The inside-aligned border is visible.");
            Assert.That(CountPixels(page, color => IsNear(color, 0x2a, 0x9d, 0x8f)), Is.GreaterThan(0),
                "The center-aligned border is visible.");
            Assert.That(CountPixels(page, color => IsNear(color, 0x45, 0x7b, 0x9d)), Is.GreaterThan(0),
                "The outside-aligned border is visible.");
        }

        [Test]
        public void GradientBackgroundsFillTheContainerInFourDirections()
        {
            var document = CreateDocument(page => page.Content().Column(column =>
            {
                column.Spacing(14);

                column.Item().Text("Two-stop linear gradients").SemiBold().FontSize(12);

                column.Item().Height(60).BackgroundGradient(
                    "#ff0000", "#0000ff", GradientDirection.Horizontal);

                column.Item().Height(60).BackgroundGradient(
                    "#00ff00", "#ffff00", GradientDirection.Vertical);

                column.Item().Row(row =>
                {
                    row.Spacing(10);

                    row.RelativeItem().Height(60).BackgroundGradient(
                        "#6a00ff", "#ff8800", GradientDirection.DiagonalTopLeftToBottomRight);

                    row.RelativeItem().Height(60).BackgroundGradient(
                        "#6a00ff", "#ff8800", GradientDirection.DiagonalTopRightToBottomLeft);
                });

                column.Item().Text(
                        "Gradients are drawn as vector shaders in PDF and SVG output, so they stay " +
                        "sharp at any zoom level.")
                    .FontSize(10)
                    .LineHeight(1.3f);
            }));

            var artifacts = CreateArtifactDirectory();
            var pdf = document.GeneratePdf();
            var pages = document.GenerateImages(new ImageGenerationSettings { RasterDpi = 96 });
            var svg = document.GenerateSvg().Single();

            File.WriteAllBytes(Path.Combine(artifacts, "container-gradients.pdf"), pdf);
            File.WriteAllBytes(Path.Combine(artifacts, "container-gradients.png"), pages.Single());
            File.WriteAllText(Path.Combine(artifacts, "container-gradients.svg"), svg);

            using var page = SKBitmap.Decode(pages.Single());

            Assert.That(CountPixels(page, color => color.Red > 200 && color.Blue < 60), Is.GreaterThan(0),
                "The horizontal gradient keeps its red end.");
            Assert.That(CountPixels(page, color => color.Blue > 200 && color.Red < 60), Is.GreaterThan(0),
                "The horizontal gradient keeps its blue end.");
            Assert.That(CountPixels(page, color => color.Green > 200 && color.Red < 60), Is.GreaterThan(0),
                "The vertical gradient keeps its green end.");
            Assert.That(svg, Does.Contain("linearGradient"),
                "SVG output keeps gradients as vector paint servers.");
        }

        [Test]
        public void ClipCutsOverflowingContentToTheRoundedBounds()
        {
            const int scaleFactor = 4;
            const int cardWidth = 80;
            const int cardHeight = 40;

            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(cardWidth, cardHeight);
                page.Margin(0);
                page.PageColor("#ffffff");

                page.Content()
                    .CornerRadius(12)
                    .Clip()
                    .Scale(scaleFactor)
                    .Rotate(20)
                    .Background("#00ff00");
            }));

            var artifacts = CreateArtifactDirectory();
            var pdf = document.GeneratePdf();
            var pages = document.GenerateImages(new ImageGenerationSettings { RasterDpi = 72 });
            var svg = document.GenerateSvg().Single();

            File.WriteAllBytes(Path.Combine(artifacts, "container-clip.pdf"), pdf);
            File.WriteAllBytes(Path.Combine(artifacts, "container-clip.png"), pages.Single());
            File.WriteAllText(Path.Combine(artifacts, "container-clip.svg"), svg);

            using var page = SKBitmap.Decode(pages.Single());

            Assert.That(page.GetPixel(1, 1), Is.EqualTo(SKColors.White),
                "The rounded corner is cut out of the overflowing child.");
            Assert.That(page.GetPixel(cardWidth / 2, cardHeight / 2),
                Is.EqualTo(new SKColor(0, 255, 0)),
                "The clipped child covers the center.");
            Assert.That(svg, Does.Contain("clipPath"),
                "SVG output keeps the rounded clip around the transformed child.");
        }

        private static void AddCard(IContainer container, BorderAlignment alignment, string color)
        {
            container
                .Height(60)
                .Background("#f1faee")
                .Border(6)
                .BorderColor(color)
                .BorderAlignment(alignment)
                .CornerRadius(10);
        }

        private static Document CreateDocument(Action<PageDescriptor> content)
        {
            return Document.Create(container => container.Page(page =>
            {
                page.Size(PageWidth, PageHeight);
                page.Margin(PageMargin);
                page.PageColor("#ffffff");
                page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(10));
                content(page);
            }));
        }

        private static string CreateArtifactDirectory()
        {
            var directory = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "container-styling-example");

            Directory.CreateDirectory(directory);
            return directory;
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

        private static bool IsNear(SKColor color, byte red, byte green, byte blue)
        {
            return Math.Abs(color.Red - red) <= 8 &&
                   Math.Abs(color.Green - green) <= 8 &&
                   Math.Abs(color.Blue - blue) <= 8;
        }
    }
}
