using System;
using System.Collections.Generic;
using System.Linq;
using FossPDF.Drawing;
using FossPDF.Elements;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.UnitTests.TestEngine;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.UnitTests
{
    [TestFixture]
    [NonParallelizable]
    public class PageControlTests
    {
        private const int PageWidth = 120;
        private const int PageHeight = 100;
        private const int Margin = 10;

        [Test]
        public void PreventPageBreak_Element_KeepsContentTogetherOnFirstPage()
        {
            TestPlan
                .For(x => new PreventPageBreak
                {
                    Child = x.CreateChild()
                })
                .MeasureElement(new Size(400, 300))
                .ExpectChildMeasure(new Size(400, 300), SpacePlan.PartialRender(200, 100))
                .CheckMeasureResult(SpacePlan.PartialRender(Size.Zero))

                .DrawElement(new Size(400, 300))
                .ExpectChildMeasure(new Size(400, 300), SpacePlan.PartialRender(200, 100))
                .CheckDrawResult()

                .MeasureElement(new Size(400, 300))
                .ExpectChildMeasure(new Size(400, 300), SpacePlan.FullRender(200, 100))
                .CheckMeasureResult(SpacePlan.FullRender(200, 100))

                .DrawElement(new Size(400, 300))
                .ExpectChildDraw(new Size(400, 300))
                .CheckDrawResult();
        }

        [Test]
        public void PreventPageBreak_Element_ResetStateRestoresFirstPageBehavior()
        {
            TestPlan
                .For(x => new PreventPageBreak
                {
                    Child = x.CreateChild()
                })
                .MeasureElement(new Size(400, 300))
                .ExpectChildMeasure(new Size(400, 300), SpacePlan.PartialRender(200, 100))
                .CheckMeasureResult(SpacePlan.PartialRender(Size.Zero))

                .DrawElement(new Size(400, 300))
                .ExpectChildMeasure(new Size(400, 300), SpacePlan.PartialRender(200, 100))
                .CheckDrawResult()

                .CheckState<PreventPageBreak>(x =>
                {
                    x.ResetState();
                    return true;
                })

                .MeasureElement(new Size(400, 300))
                .ExpectChildMeasure(new Size(400, 300), SpacePlan.PartialRender(200, 100))
                .CheckMeasureResult(SpacePlan.PartialRender(Size.Zero));
        }

        [Test]
        public void Repeat_Element_RendersOnEveryMeasurementWithoutConsumingContent()
        {
            TestPlan
                .For(x => new RepeatContent
                {
                    Child = x.CreateChild()
                })
                .MeasureElement(new Size(400, 300))
                .ExpectChildMeasure(new Size(400, 300), SpacePlan.PartialRender(200, 100))
                .CheckMeasureResult(SpacePlan.PartialRender(200, 100))

                .DrawElement(new Size(400, 300))
                .ExpectChildMeasure(new Size(400, 300), SpacePlan.PartialRender(200, 100))
                .ExpectChildDraw(new Size(400, 300))
                .CheckDrawResult()

                .MeasureElement(new Size(400, 300))
                .ExpectChildMeasure(new Size(400, 300), SpacePlan.PartialRender(200, 100))
                .CheckMeasureResult(SpacePlan.PartialRender(200, 100));
        }

        [Test]
        public void PreventPageBreak_MovesGroupToNextPage_WhenRemainderIsTooSmall()
        {
            var document = CreateDocument(page => page.Content().Column(column =>
            {
                column.Item().Height(60).Background("#808080");
                column.Item().PreventPageBreak().Column(group =>
                {
                    group.Item().Height(15).Background("#ff0000");
                    group.Item().Height(15).Background("#0000ff");
                });
            }));

            var pdf = document.GeneratePdf(out var count);
            var pages = DecodePages(document);

            try
            {
                Assert.That(pdf, Is.Not.Empty);
                Assert.That(count, Is.EqualTo(2));
                Assert.That(pages.Length, Is.EqualTo(2));

                AssertColor(pages[0], 50, 15, "#808080");
                AssertColor(pages[0], 50, 75, "#ffffff");

                AssertColor(pages[1], 50, 15, "#ff0000");
                AssertColor(pages[1], 50, 30, "#0000ff");
            }
            finally
            {
                DisposePages(pages);
            }
        }

        [Test]
        public void PreventPageBreak_LetsContentLongerThanAPageContinueNormally()
        {
            var document = CreateDocument(page => page.Content()
                .PreventPageBreak()
                .Column(column =>
                {
                    for (var index = 0; index < 10; index++)
                        column.Item().Height(15).Background("#ff0000");
                }));

            document.GeneratePdf(out var count);
            var pages = DecodePages(document);

            try
            {
                Assert.That(count, Is.EqualTo(3));
                Assert.That(pages.Length, Is.EqualTo(3));

                AssertColor(pages[0], 50, 15, "#ffffff");
                AssertColor(pages[1], 50, 15, "#ff0000");
                AssertColor(pages[1], 50, 75, "#ff0000");
                AssertColor(pages[2], 50, 15, "#ff0000");
            }
            finally
            {
                DisposePages(pages);
            }
        }

        [Test]
        public void Repeat_RendersMarkerOnEveryPageOfSpanningRow()
        {
            var document = CreateRepeatDocument(out var expectedMarkerCount);

            document.GeneratePdf(out var count);
            var pages = DecodePages(document);

            try
            {
                Assert.That(count, Is.EqualTo(2));
                Assert.That(pages.Length, Is.EqualTo(2));

                foreach (var page in pages)
                    Assert.That(CountRedPixels(page), Is.InRange(
                        expectedMarkerCount / 2, expectedMarkerCount * 2),
                        "The repeated marker must render once per page.");
            }
            finally
            {
                DisposePages(pages);
            }
        }

        [Test]
        public void Repeat_DoesNotConsumeOrDuplicateContentAcrossRepeatedGenerations()
        {
            var document = CreateRepeatDocument(out _);

            var first = DecodePages(document);
            var second = DecodePages(document);

            try
            {
                Assert.That(first.Length, Is.EqualTo(2));
                Assert.That(second.Length, Is.EqualTo(2));

                for (var index = 0; index < first.Length; index++)
                {
                    Assert.That(CountRedPixels(second[index]), Is.EqualTo(CountRedPixels(first[index])));
                    Assert.That(CountBluePixels(second[index]), Is.EqualTo(CountBluePixels(first[index])));
                }
            }
            finally
            {
                DisposePages(first);
                DisposePages(second);
            }
        }

        [Test]
        public void ShowIf_PredicateSeesCurrentPageAndFinalTotalPages()
        {
            var contexts = new List<(int Page, int? TotalPages)>();

            var document = CreateDocument(page => page.Content().Column(column =>
            {
                column.Item().PageBreak();
                column.Item()
                    .ShowIf(context =>
                    {
                        contexts.Add((context.PageNumber, context.TotalPages));
                        return context.PageNumber % 2 == 0 && context.TotalPages == 2;
                    })
                    .Height(20)
                    .Background("#00ff00");
                column.Item().Height(20).Background("#0000ff");
            }));

            document.GeneratePdf(out var count);
            var pages = DecodePages(document);

            try
            {
                Assert.That(count, Is.EqualTo(2));
                Assert.That(pages.Length, Is.EqualTo(2));

                AssertColor(pages[0], 50, 15, "#ffffff");
                AssertColor(pages[1], 50, 20, "#00ff00");
                AssertColor(pages[1], 50, 40, "#0000ff");

                Assert.That(contexts, Does.Contain((2, (int?)null)),
                    "The initial pass must expose an unknown total page count.");
                Assert.That(contexts, Does.Contain((2, (int?)2)),
                    "The final pass must expose the resolved total page count.");
            }
            finally
            {
                DisposePages(pages);
            }
        }

        [Test]
        public void ShowIf_NullPredicateIsRejected()
        {
            var document = Document.Create(container => container.Page(page =>
                page.Content().ShowIf((Predicate<ShowIfContext>)null)));

            Assert.Throws<ArgumentNullException>(() => document.GeneratePdf());
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

        private static Document CreateRepeatDocument(out int markerPixelCount)
        {
            const int markerSize = 20;

            markerPixelCount = markerSize * markerSize;

            return CreateDocument(page => page.Content().Row(row =>
            {
                row.ConstantItem(markerSize)
                    .Repeat()
                    .Height(markerSize)
                    .Background("#ff0000");

                row.RelativeItem().Column(column =>
                {
                    for (var index = 0; index < 8; index++)
                        column.Item().Height(15).Background("#0000ff");
                });
            }));
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
            return CountPixels(page, color =>
                color.Red > 200 && color.Green < 80 && color.Blue < 80);
        }

        private static int CountBluePixels(SKBitmap page)
        {
            return CountPixels(page, color =>
                color.Blue > 200 && color.Red < 80 && color.Green < 80);
        }

        private static int CountPixels(SKBitmap page, Func<SKColor, bool> predicate)
        {
            var count = 0;

            for (var y = 0; y < page.Height; y++)
            for (var x = 0; x < page.Width; x++)
            {
                if (predicate(page.GetPixel(x, y)))
                    count++;
            }

            return count;
        }
    }
}
