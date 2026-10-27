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
    public class ParagraphLayoutExamples
    {
        private const string ParagraphText =
            "Amber birch cedar dogwood elm fir maple oak. " +
            "The quick brown fox jumps over the lazy dog while the river " +
            "carries fallen leaves toward the quiet harbor.";

        [Test]
        public void ParagraphAlignmentIndentationAndWordSpacing()
        {
            var document = CreateDocument(page =>
            {
                page.Content().Column(column =>
                {
                    column.Spacing(6);

                    column.Item().Text("Justified with a 16-point first-line indent").SemiBold().FontSize(9);

                    column.Item().Width(240).Text(text =>
                    {
                        text.Justify();
                        text.ParagraphFirstLineIndentation(16);
                        text.Span(ParagraphText);
                    });

                    column.Item().Text("Right-to-left content aligned to its logical start").SemiBold().FontSize(9);

                    column.Item().Width(240).ContentFromRightToLeft().Text(text =>
                    {
                        text.AlignStart();
                        text.Span(ParagraphText).DirectionFromRightToLeft();
                    });

                    column.Item().Text("Word spacing as a factor of the font size").SemiBold().FontSize(9);

                    column.Item().Width(240).Text(text =>
                    {
                        text.Justify();
                        text.ParagraphFirstLineIndentation(16);
                        text.Span(ParagraphText).WordSpacing(1.5f);
                    });
                });
            });

            using var pdf = new MemoryStream();
            document.GeneratePdf(pdf);

            var pages = document.GenerateImages(new ImageGenerationSettings
            {
                RasterDpi = 72,
                Format = ImageOutputFormat.Png
            });

            Assert.That(pages, Has.Count.EqualTo(1));

            using var page = SKBitmap.Decode(pages.Single());
            Assert.That(page.Width, Is.EqualTo(300));
            Assert.That(page.Height, Is.EqualTo(320));

            var directory = CreateArtifactDirectory();
            File.WriteAllBytes(Path.Combine(directory, "paragraph-layout.pdf"), pdf.ToArray());
            File.WriteAllBytes(Path.Combine(directory, "paragraph-layout.png"), pages.Single());
        }

        [Test]
        public void ParagraphContinuesAcrossPagesWithoutRepeatingIndentation()
        {
            var longText = string.Join(" ", Enumerable.Repeat(ParagraphText, 12));

            var document = CreateDocument(page =>
            {
                page.Content().Width(240).Text(text =>
                {
                    text.Justify();
                    text.ParagraphFirstLineIndentation(16);
                    text.Span(longText);
                });
            });

            using var pdf = new MemoryStream();
            document.GeneratePdf(pdf);

            var pages = document.GenerateImages(new ImageGenerationSettings
            {
                RasterDpi = 72,
                Format = ImageOutputFormat.Png
            }).ToArray();

            Assert.That(pages.Length, Is.GreaterThanOrEqualTo(2));

            var directory = CreateArtifactDirectory();
            File.WriteAllBytes(Path.Combine(directory, "paragraph-multipage.pdf"), pdf.ToArray());

            for (var index = 0; index < pages.Length; index++)
                File.WriteAllBytes(Path.Combine(directory, $"paragraph-multipage-page-{index + 1}.png"), pages[index]);
        }

        private static Document CreateDocument(Action<PageDescriptor> content)
        {
            return Document.Create(container => container.Page(page =>
            {
                page.Size(300, 320);
                page.Margin(20);
                page.PageColor("#ffffff");
                page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(11));
                content(page);
            }));
        }

        private static string CreateArtifactDirectory()
        {
            var directory = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "paragraph-layout-example");

            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
