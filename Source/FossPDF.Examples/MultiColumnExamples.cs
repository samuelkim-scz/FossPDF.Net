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
    public class MultiColumnExamples
    {
        private const int PageWidth = 300;
        private const int PageHeight = 320;
        private const int PageMargin = 20;
        private const int ColumnGap = 16;

        private const string Paragraph =
            "Newspaper columns carry a single content stream from the top of one " +
            "column to the top of the next, keeping the reading order intact.";

        private static readonly string[] Palette =
        {
            "#e63946", "#2a9d8f", "#457b9d", "#e9c46a"
        };

        [Test]
        public void BalancedColumnsWithSeparator()
        {
            var document = CreateDocument(page => page.Content().MultiColumn(multiColumn =>
            {
                multiColumn.Columns(2);
                multiColumn.Spacing(ColumnGap);
                multiColumn.BalanceHeight();

                multiColumn.Spacer().AlignCenter().LineVertical(1).LineColor("#808080");

                multiColumn.Content().Column(column =>
                {
                    column.Spacing(8);

                    for (var index = 0; index < 3; index++)
                    {
                        column.Item().Height(30).Background(Palette[index]);
                        column.Item().Text(text => text.Span(Paragraph));
                    }
                });
            }));

            var pages = Generate(document, out var pdf);

            Assert.That(pages, Has.Length.EqualTo(1), "Balanced content fits on a single page.");
            Assert.That(pdf.Length, Is.GreaterThan(0));

            using var page = SKBitmap.Decode(pages.Single());
            var gapLeft = PageMargin + ColumnWidth;

            Assert.That(ColumnHasInk(page, PageMargin, gapLeft), Is.True,
                "The first column must contain content.");
            Assert.That(ColumnHasInk(page, gapLeft + ColumnGap, PageWidth - PageMargin), Is.True,
                "Balanced content must continue in the second column.");
            Assert.That(GapInkCount(page, gapLeft, gapLeft + ColumnGap), Is.GreaterThan(0),
                "The separator must be drawn inside the gap.");

            SaveArtifacts("multi-column-balanced", pdf, pages);
        }

        [Test]
        public void UnbalancedColumnsContinueOnTheNextPage()
        {
            var document = CreateDocument(page => page.Content().MultiColumn(multiColumn =>
            {
                multiColumn.Columns(2);
                multiColumn.Spacing(ColumnGap);

                multiColumn.Content().Column(column =>
                {
                    for (var index = 0; index < 40; index++)
                        column.Item().Height(20).Background(Palette[index % Palette.Length]);
                });
            }));

            var pages = Generate(document, out var pdf);

            Assert.That(pages.Length, Is.GreaterThanOrEqualTo(2),
                "Unbalanced columns fill the page and continue on the next one.");

            SaveArtifacts("multi-column-unbalanced", pdf, pages);
        }

        private static int ColumnWidth =>
            (PageWidth - 2 * PageMargin - ColumnGap) / 2;

        private static Document CreateDocument(Action<PageDescriptor> content)
        {
            return Document.Create(container => container.Page(page =>
            {
                page.Size(PageWidth, PageHeight);
                page.Margin(PageMargin);
                page.PageColor("#ffffff");
                page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(12));
                content(page);
            }));
        }

        private static byte[][] Generate(IDocument document, out byte[] pdf)
        {
            pdf = document.GeneratePdf();

            return document.GenerateImages(new ImageGenerationSettings
            {
                RasterDpi = 72,
                Format = ImageOutputFormat.Png
            }).ToArray();
        }

        private static bool ColumnHasInk(SKBitmap page, int from, int to)
        {
            for (var y = PageMargin; y < PageHeight - PageMargin; y++)
            for (var x = from; x < to; x++)
            {
                if (IsInk(page.GetPixel(x, y)))
                    return true;
            }

            return false;
        }

        private static int GapInkCount(SKBitmap page, int from, int to)
        {
            var count = 0;

            for (var y = PageMargin; y < PageHeight - PageMargin; y++)
            for (var x = from; x < to; x++)
            {
                if (IsInk(page.GetPixel(x, y)))
                    count++;
            }

            return count;
        }

        private static bool IsInk(SKColor color) =>
            color.Red != 255 || color.Green != 255 || color.Blue != 255;

        private static void SaveArtifacts(string name, byte[] pdf, byte[][] pages)
        {
            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "multi-column-example");
            Directory.CreateDirectory(directory);

            File.WriteAllBytes(Path.Combine(directory, name + ".pdf"), pdf);

            for (var index = 0; index < pages.Length; index++)
                File.WriteAllBytes(Path.Combine(directory, $"{name}-page-{index + 1}.png"), pages[index]);
        }
    }
}
