using System;
using System.Collections.Generic;
using System.Linq;
using FossPDF.Drawing;
using FossPDF.Drawing.Exceptions;
using FossPDF.Elements;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.UnitTests
{
    [TestFixture]
    [NonParallelizable]
    public class MultiColumnTests
    {
        private const int PageWidth = 120;
        private const int PageHeight = 100;
        private const int Margin = 10;

        // Two columns with 10pt spacing inside a 100x80pt content area:
        // first column [10, 55), gap [55, 65), second column [65, 110).
        private const int FirstColumnCenter = 32;
        private const int SecondColumnCenter = 87;

        private static readonly string[] Palette =
        {
            "#ff0000", "#00ff00", "#0000ff", "#ffff00",
            "#ff00ff", "#00ffff", "#808080", "#000000"
        };

        #region Descriptor validation

        [Test]
        public void Columns_RejectsNonPositiveCount()
        {
            var document = CreateDocument(page => page.Content().MultiColumn(multiColumn =>
            {
                multiColumn.Columns(0);
            }));

            Assert.Throws<ArgumentOutOfRangeException>(() => document.GeneratePdf());
        }

        [Test]
        public void Spacing_RejectsNonFiniteValue()
        {
            var document = CreateDocument(page => page.Content().MultiColumn(multiColumn =>
            {
                multiColumn.Spacing(float.NaN);
            }));

            Assert.Throws<ArgumentOutOfRangeException>(() => document.GeneratePdf());
        }

        [Test]
        public void Content_CannotBeDefinedTwice()
        {
            var document = CreateDocument(page => page.Content().MultiColumn(multiColumn =>
            {
                multiColumn.Content().Height(10);
                multiColumn.Content().Height(10);
            }));

            Assert.Throws<DocumentComposeException>(() => document.GeneratePdf());
        }

        [Test]
        public void Spacer_CannotBeDefinedTwice()
        {
            var document = CreateDocument(page => page.Content().MultiColumn(multiColumn =>
            {
                multiColumn.Spacer().Height(10);
                multiColumn.Spacer().Height(10);
                multiColumn.Content().Height(10);
            }));

            Assert.Throws<DocumentComposeException>(() => document.GeneratePdf());
        }

        #endregion

        #region Unbalanced flow

        [Test]
        public void Unbalanced_FillsEachColumnTopToBottom()
        {
            var document = CreateDocument(page => page.Content().MultiColumn(multiColumn =>
            {
                multiColumn.Columns(2);
                multiColumn.Spacing(10);
                multiColumn.Content().Column(column => AddItems(column, colors: 6, height: 20));
            }));

            var pages = DecodePages(document);

            try
            {
                Assert.That(pages.Length, Is.EqualTo(1));

                // Six items of 20pt: four fill the first column, two continue in the second.
                AssertColor(pages[0], FirstColumnCenter, 20, Palette[0]);
                AssertColor(pages[0], FirstColumnCenter, 40, Palette[1]);
                AssertColor(pages[0], FirstColumnCenter, 60, Palette[2]);
                AssertColor(pages[0], FirstColumnCenter, 80, Palette[3]);

                AssertColor(pages[0], SecondColumnCenter, 20, Palette[4]);
                AssertColor(pages[0], SecondColumnCenter, 40, Palette[5]);
                AssertColor(pages[0], SecondColumnCenter, 60, "#ffffff");
                AssertColor(pages[0], SecondColumnCenter, 80, "#ffffff");
            }
            finally
            {
                DisposePages(pages);
            }
        }

        [Test]
        public void Unbalanced_OccupiesFullHeightAndContinuesOnNextPage()
        {
            var document = CreateDocument(page => page.Content().Column(column =>
            {
                column.Item().MultiColumn(multiColumn =>
                {
                    multiColumn.Columns(2);
                    multiColumn.Spacing(10);
                    multiColumn.Content().Column(inner => AddItems(inner, colors: 8, height: 15));
                });

                column.Item().Height(10).Background("#123456");
            }));

            var pages = DecodePages(document);

            try
            {
                Assert.That(pages.Length, Is.EqualTo(2));

                // Without balancing the element spans the whole page, so the sibling starts on page 2.
                AssertColor(pages[0], FirstColumnCenter, 75, Palette[4]);
                AssertColor(pages[1], FirstColumnCenter, 15, "#123456");
            }
            finally
            {
                DisposePages(pages);
            }
        }

        [Test]
        public void SingleColumn_FlowsAcrossPages()
        {
            var document = CreateDocument(page => page.Content().MultiColumn(multiColumn =>
            {
                multiColumn.Columns(1);
                multiColumn.Content().Column(inner => AddItems(inner, colors: 12, height: 15, repeating: "#ff0000"));
            }));

            var pages = DecodePages(document);

            try
            {
                Assert.That(pages.Length, Is.EqualTo(3));
                Assert.That(CountRedPixels(pages[0]), Is.EqualTo(5 * ColumnPixelArea(100, 15)));
                Assert.That(CountRedPixels(pages[1]), Is.EqualTo(5 * ColumnPixelArea(100, 15)));
                Assert.That(CountRedPixels(pages[2]), Is.EqualTo(2 * ColumnPixelArea(100, 15)));
            }
            finally
            {
                DisposePages(pages);
            }
        }

        #endregion

        #region Balanced flow

        [Test]
        public void Balanced_SpreadsContentEvenlyAcrossColumns()
        {
            var document = CreateDocument(page => page.Content().MultiColumn(multiColumn =>
            {
                multiColumn.Columns(2);
                multiColumn.Spacing(10);
                multiColumn.BalanceHeight();
                multiColumn.Content().Column(inner => AddItems(inner, colors: 8, height: 15));
            }));

            var pages = DecodePages(document);

            try
            {
                Assert.That(pages.Length, Is.EqualTo(1));

                // Eight items of 15pt balance into two columns of four items (60pt each).
                AssertColor(pages[0], FirstColumnCenter, 17, Palette[0]);
                AssertColor(pages[0], FirstColumnCenter, 62, Palette[3]);
                AssertColor(pages[0], FirstColumnCenter, 75, "#ffffff");

                AssertColor(pages[0], SecondColumnCenter, 17, Palette[4]);
                AssertColor(pages[0], SecondColumnCenter, 62, Palette[7]);
                AssertColor(pages[0], SecondColumnCenter, 75, "#ffffff");
            }
            finally
            {
                DisposePages(pages);
            }
        }

        [Test]
        public void Balanced_ReportsOnlyBalancedHeightToFollowingContent()
        {
            var document = CreateDocument(page => page.Content().Column(column =>
            {
                column.Item().MultiColumn(multiColumn =>
                {
                    multiColumn.Columns(2);
                    multiColumn.Spacing(10);
                    multiColumn.BalanceHeight();
                    multiColumn.Content().Column(inner => AddItems(inner, colors: 8, height: 15));
                });

                column.Item().Height(10).Background("#123456");
            }));

            var pages = DecodePages(document);

            try
            {
                Assert.That(pages.Length, Is.EqualTo(1));

                // The balanced columns use 60pt, so the sibling fits below them on the same page.
                AssertColor(pages[0], FirstColumnCenter, 75, "#123456");
                AssertColor(pages[0], SecondColumnCenter, 75, "#123456");
            }
            finally
            {
                DisposePages(pages);
            }
        }

        [Test]
        public void Balanced_MinimizesHeightWhenTheLastColumnIsPartiallyFilled()
        {
            var document = CreateDocument(page => page.Content().MultiColumn(multiColumn =>
            {
                multiColumn.Columns(2);
                multiColumn.Spacing(10);
                multiColumn.BalanceHeight();
                multiColumn.Content().Column(inner => AddItems(inner, colors: 3, height: 15));
            }));

            var pages = DecodePages(document);

            try
            {
                Assert.That(pages.Length, Is.EqualTo(1));

                // The minimal height that fits three items is 40pt: two items fill the first
                // column and the third starts a partially filled second column.
                AssertColor(pages[0], FirstColumnCenter, 17, Palette[0]);
                AssertColor(pages[0], FirstColumnCenter, 32, Palette[1]);
                AssertColor(pages[0], FirstColumnCenter, 47, "#ffffff");
                AssertColor(pages[0], SecondColumnCenter, 17, Palette[2]);
                AssertColor(pages[0], SecondColumnCenter, 32, "#ffffff");
            }
            finally
            {
                DisposePages(pages);
            }
        }

        [Test]
        public void EmptyContent_ProducesBlankPage()
        {
            var document = CreateDocument(page => page.Content().MultiColumn(multiColumn =>
            {
                multiColumn.Columns(2);
                multiColumn.Spacing(10);
            }));

            var pages = DecodePages(document);

            try
            {
                Assert.That(pages.Length, Is.EqualTo(1));
                AssertColor(pages[0], FirstColumnCenter, PageHeight / 2, "#ffffff");
                AssertColor(pages[0], SecondColumnCenter, PageHeight / 2, "#ffffff");
            }
            finally
            {
                DisposePages(pages);
            }
        }

        #endregion

        #region Multi-page flow and determinism

        [Test]
        public void MultiPage_ContinuesContentWithoutDuplication()
        {
            var document = CreateDocument(page => page.Content().MultiColumn(multiColumn =>
            {
                multiColumn.Columns(2);
                multiColumn.Spacing(10);
                multiColumn.Content().Column(inner => AddItems(inner, colors: 12, height: 15, repeating: "#ff0000"));
            }));

            var pages = DecodePages(document);

            try
            {
                Assert.That(pages.Length, Is.EqualTo(2));

                // Ten items fit on the first page (five per column), two continue on the second.
                Assert.That(CountRedPixels(pages[0]), Is.EqualTo(10 * ColumnPixelArea(45, 15)));
                Assert.That(CountRedPixels(pages[1]), Is.EqualTo(2 * ColumnPixelArea(45, 15)));
            }
            finally
            {
                DisposePages(pages);
            }
        }

        [Test]
        public void MultiPage_IsDeterministicAcrossRepeatedGenerations()
        {
            var document = CreateDocument(page => page.Content().MultiColumn(multiColumn =>
            {
                multiColumn.Columns(2);
                multiColumn.Spacing(10);
                multiColumn.Content().Column(inner => AddItems(inner, colors: 12, height: 15, repeating: "#ff0000"));
            }));

            var first = DecodePages(document);
            var second = DecodePages(document);

            try
            {
                Assert.That(first.Length, Is.EqualTo(second.Length));

                for (var index = 0; index < first.Length; index++)
                    Assert.That(CountRedPixels(second[index]), Is.EqualTo(CountRedPixels(first[index])));
            }
            finally
            {
                DisposePages(first);
                DisposePages(second);
            }
        }

        #endregion

        #region Reading order

        [Test]
        public void RightToLeft_FillsColumnsFromTheRight()
        {
            var document = CreateDocument(page =>
            {
                page.ContentFromRightToLeft();
                page.Content().MultiColumn(multiColumn =>
                {
                    multiColumn.Columns(2);
                    multiColumn.Spacing(10);
                    multiColumn.Content().Column(inner => AddItems(inner, colors: 6, height: 20));
                });
            });

            var pages = DecodePages(document);

            try
            {
                Assert.That(pages.Length, Is.EqualTo(1));

                // Content starts in the right column and continues in the left one.
                AssertColor(pages[0], SecondColumnCenter, 20, Palette[0]);
                AssertColor(pages[0], SecondColumnCenter, 40, Palette[1]);
                AssertColor(pages[0], SecondColumnCenter, 60, Palette[2]);
                AssertColor(pages[0], SecondColumnCenter, 80, Palette[3]);

                AssertColor(pages[0], FirstColumnCenter, 20, Palette[4]);
                AssertColor(pages[0], FirstColumnCenter, 40, Palette[5]);
            }
            finally
            {
                DisposePages(pages);
            }
        }

        #endregion

        #region Spacer

        [Test]
        public void Spacer_IsDrawnInTheGapBetweenColumns()
        {
            var document = CreateDocument(page => page.Content().MultiColumn(multiColumn =>
            {
                multiColumn.Columns(2);
                multiColumn.Spacing(20);
                multiColumn.Spacer().AlignCenter().LineVertical(2).LineColor("#ff0000");
                multiColumn.Content().Column(inner => AddItems(inner, colors: 6, height: 20));
            }));

            var pages = DecodePages(document);

            try
            {
                Assert.That(pages.Length, Is.EqualTo(1));

                // Columns are 40pt wide with a 20pt gap; the separator is centered inside it.
                Assert.That(CountPixelsInRow(pages[0], 50, 55, 65), Is.InRange(1, 4),
                    "The separator must be visible in the middle of the gap.");
                AssertColor(pages[0], 56, 50, "#ffffff");
                AssertColor(pages[0], 63, 50, "#ffffff");
            }
            finally
            {
                DisposePages(pages);
            }
        }

        #endregion

        #region Stateful content

        [Test]
        public void ShowOnce_IsRenderedExactlyOnceAcrossSpeculativeMeasurement()
        {
            var document = CreateDocument(page => page.Content().MultiColumn(multiColumn =>
            {
                multiColumn.Columns(2);
                multiColumn.Spacing(10);
                multiColumn.BalanceHeight();
                multiColumn.Content().Column(inner =>
                {
                    inner.Item().ShowOnce().Height(15).Background("#ff0000");

                    for (var index = 0; index < 10; index++)
                        inner.Item().Height(15).Background("#0000ff");
                });
            }));

            var pages = DecodePages(document);

            try
            {
                Assert.That(pages.Length, Is.EqualTo(2));

                Assert.That(CountRedPixels(pages[0]), Is.EqualTo(ColumnPixelArea(45, 15)),
                    "The ShowOnce marker must render exactly once on the first page.");
                Assert.That(CountRedPixels(pages[1]), Is.EqualTo(0));
            }
            finally
            {
                DisposePages(pages);
            }
        }

        [Test]
        public void Table_ContinuesAcrossColumnsWithoutDuplicatingRows()
        {
            var document = CreateDocument(page => page.Content().MultiColumn(multiColumn =>
            {
                multiColumn.Columns(2);
                multiColumn.Spacing(10);
                multiColumn.Content().Table(table =>
                {
                    table.ColumnsDefinition(columns => columns.RelativeColumn());

                    for (var row = 0; row < 12; row++)
                        table.Cell().Height(10).Background("#ff0000");
                });
            }));

            var pages = DecodePages(document);

            try
            {
                Assert.That(pages.Length, Is.EqualTo(1));
                Assert.That(CountRedPixels(pages[0]), Is.EqualTo(12 * ColumnPixelArea(45, 10)),
                    "Every table row must be rendered exactly once.");
            }
            finally
            {
                DisposePages(pages);
            }
        }

        [Test]
        public void Dynamic_FlowsThroughColumnsWithoutLosingContent()
        {
            var document = CreateDocument(page => page.Content().MultiColumn(multiColumn =>
            {
                multiColumn.Columns(2);
                multiColumn.Spacing(10);
                multiColumn.Content().Dynamic(new SingleItemComponent(Palette[0], Palette[1], Palette[4], Palette[5]));
            }));

            var pages = DecodePages(document);

            try
            {
                Assert.That(pages.Length, Is.EqualTo(2));

                AssertColor(pages[0], FirstColumnCenter, 17, Palette[0]);
                AssertColor(pages[0], SecondColumnCenter, 17, Palette[1]);
                AssertColor(pages[1], FirstColumnCenter, 17, Palette[4]);
                AssertColor(pages[1], SecondColumnCenter, 17, Palette[5]);
            }
            finally
            {
                DisposePages(pages);
            }
        }

        [Test]
        public void Text_FlowsAcrossColumnsWithoutOverflow()
        {
            var paragraph = "The quick brown fox jumps over the lazy dog and keeps on running through the meadow.";

            var document = CreateDocument(page => page.Content().MultiColumn(multiColumn =>
            {
                multiColumn.Columns(2);
                multiColumn.Spacing(10);
                multiColumn.Content().Text(paragraph);
            }));

            var pages = DecodePages(document);

            try
            {
                Assert.That(CountDarkPixels(pages[0], 10, 55), Is.GreaterThan(0),
                    "The first column must contain text.");
                Assert.That(CountDarkPixels(pages[0], 65, 110), Is.GreaterThan(0),
                    "The remaining text must continue in the second column.");
                Assert.That(CountDarkPixels(pages[0], 55, 65), Is.EqualTo(0),
                    "Text must not overflow into the spacing between columns.");
            }
            finally
            {
                DisposePages(pages);
            }
        }

        #endregion

        private class SingleItemComponent : IDynamicComponent<int>
        {
            private readonly string[] _colors;

            public SingleItemComponent(params string[] colors)
            {
                _colors = colors;
            }

            public int State { get; set; }

            public DynamicComponentComposeResult Compose(DynamicContext context)
            {
                var index = State;

                if (index >= _colors.Length)
                    return new DynamicComponentComposeResult { HasMoreContent = false };

                State = index + 1;

                return new DynamicComponentComposeResult
                {
                    Content = context.CreateElement(element => element.Height(15).Background(_colors[index])),
                    HasMoreContent = index + 1 < _colors.Length
                };
            }
        }

        private static void AddItems(ColumnDescriptor column, int colors, float height, string repeating = null)
        {
            for (var index = 0; index < colors; index++)
                column.Item().Height(height).Background(repeating ?? Palette[index % Palette.Length]);
        }

        private static int ColumnPixelArea(int width, int height)
        {
            return width * height;
        }

        private static Document CreateDocument(Action<PageDescriptor> content)
        {
            return Document.Create(container => container.Page(page =>
            {
                page.Size(PageWidth, PageHeight);
                page.Margin(Margin);
                page.PageColor("#ffffff");
                content(page);
            })).WithMetadata(new DocumentMetadata
            {
                RasterDpi = 72,
                CreationDate = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ModifiedDate = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        }

        private static SKBitmap[] DecodePages(IDocument document)
        {
            return document.GenerateImages()
                .Select(SKBitmap.Decode)
                .ToArray();
        }

        private static void DisposePages(IEnumerable<SKBitmap> pages)
        {
            foreach (var page in pages)
                page.Dispose();
        }

        private static void AssertColor(SKBitmap page, int x, int y, string expected)
        {
            Assert.That(page.GetPixel(x, y), Is.EqualTo(SKColor.Parse(expected)),
                $"Unexpected color at ({x}, {y}).");
        }

        private static int CountRedPixels(SKBitmap page)
        {
            return CountPixels(page, IsRed);
        }

        private static bool IsRed(SKColor color)
        {
            return color.Red > 200 && color.Green < 80 && color.Blue < 80;
        }

        private static int CountPixelsInRow(SKBitmap page, int y, int fromX, int toX)
        {
            var count = 0;

            for (var x = fromX; x < toX; x++)
            {
                if (IsRed(page.GetPixel(x, y)))
                    count++;
            }

            return count;
        }

        private static int CountDarkPixels(SKBitmap page, int fromX, int toX)
        {
            return CountPixels(page, color =>
                color.Red < 100 && color.Green < 100 && color.Blue < 100, fromX, toX);
        }

        private static int CountPixels(SKBitmap page, Func<SKColor, bool> predicate, int fromX = 0, int toX = -1)
        {
            if (toX < 0)
                toX = page.Width;

            var count = 0;

            for (var y = 0; y < page.Height; y++)
            for (var x = fromX; x < toX; x++)
            {
                if (predicate(page.GetPixel(x, y)))
                    count++;
            }

            return count;
        }
    }
}
