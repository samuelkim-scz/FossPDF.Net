using System;
using System.Collections.Generic;
using System.Linq;
using FossPDF.Drawing;
using FossPDF.Drawing.Exceptions;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.UnitTests
{
    [TestFixture]
    [NonParallelizable]
    public class MergedDocumentTests
    {
        [Test]
        public void Merge_OriginalPageNumbers_ResetPerSourceDocument()
        {
            var firstRecords = new List<(int Page, int? Total)>();
            var secondRecords = new List<(int Page, int? Total)>();

            var first = CreateTwoPageDocument((page, total) => firstRecords.Add((page, total)));
            var second = CreateTwoPageDocument((page, total) => secondRecords.Add((page, total)));

            var merged = Document.Merge(first, second).UseOriginalPageNumbers();
            var pages = RenderPages(merged, out var count);

            try
            {
                Assert.That(count, Is.EqualTo(4));
                Assert.That(pages, Has.Length.EqualTo(4));
                AssertPageColors(pages, "#ff0000", "#0000ff", "#ff0000", "#0000ff");

                Assert.That(Format(FinalRecords(firstRecords)), Is.EqualTo(new[] { "1/2", "2/2" }));
                Assert.That(Format(FinalRecords(secondRecords)), Is.EqualTo(new[] { "1/2", "2/2" }));
            }
            finally
            {
                DisposePages(pages);
            }
        }

        [Test]
        public void Merge_ContinuousPageNumbers_UseGlobalNumbersAndTotals()
        {
            var firstRecords = new List<(int Page, int? Total)>();
            var secondRecords = new List<(int Page, int? Total)>();

            var first = CreateTwoPageDocument((page, total) => firstRecords.Add((page, total)));
            var second = CreateTwoPageDocument((page, total) => secondRecords.Add((page, total)));

            var merged = Document.Merge(first, second).UseContinuousPageNumbers();
            var pages = RenderPages(merged, out var count);

            try
            {
                Assert.That(count, Is.EqualTo(4));
                Assert.That(pages, Has.Length.EqualTo(4));
                AssertPageColors(pages, "#ff0000", "#0000ff", "#ff0000", "#0000ff");

                Assert.That(Format(FinalRecords(firstRecords)), Is.EqualTo(new[] { "1/4", "2/4" }));
                Assert.That(Format(FinalRecords(secondRecords)), Is.EqualTo(new[] { "3/4", "4/4" }));
            }
            finally
            {
                DisposePages(pages);
            }
        }

        [Test]
        public void Merge_RetainsMixedPageSizes()
        {
            var first = CreateSolidPage(120, 100, "#ff0000");
            var second = CreateSolidPage(200, 150, "#0000ff");

            var pages = RenderPages(Document.Merge(first, second), out var count);

            try
            {
                Assert.That(count, Is.EqualTo(2));
                Assert.That(pages[0].Width, Is.EqualTo(120));
                Assert.That(pages[0].Height, Is.EqualTo(100));
                Assert.That(pages[1].Width, Is.EqualTo(200));
                Assert.That(pages[1].Height, Is.EqualTo(150));
            }
            finally
            {
                DisposePages(pages);
            }
        }

        [Test]
        public void Merge_AllowsTheSameDocumentInstanceToBeRepeated()
        {
            var source = CreateSolidPage(120, 100, "#ff0000");

            var pages = RenderPages(Document.Merge(source, source), out var count);

            try
            {
                Assert.That(count, Is.EqualTo(2));
                Assert.That(pages, Has.Length.EqualTo(2));
            }
            finally
            {
                DisposePages(pages);
            }
        }

        [Test]
        public void Merge_GlobalPageLimitAppliesToTheCombinedDocument()
        {
            var first = CreateTwoPageDocument();
            var second = CreateTwoPageDocument();

            var merged = Document.Merge(first, second)
                .WithSettings(new DocumentSettings { DocumentLayoutExceptionThreshold = 3 });

            Assert.Throws<DocumentLayoutException>(() => merged.GeneratePdf());
        }

        [Test]
        public void Merge_MergedMetadataAndSettingsOverrideSourceValues()
        {
            var source = CreateSolidPage(120, 100, "#ff0000")
                .WithMetadata(new DocumentMetadata { Title = "source" })
                .WithSettings(new DocumentSettings
                {
                    ImageRasterDpi = 144,
                    DocumentLayoutExceptionThreshold = 1
                });

            var merged = Document.Merge(source)
                .WithMetadata(new DocumentMetadata { Title = "merged" })
                .WithSettings(new DocumentSettings
                {
                    ImageRasterDpi = 72,
                    DocumentLayoutExceptionThreshold = 5
                });

            var configuration = GenerationConfiguration.Resolve(merged);

            Assert.That(merged.GetMetadata().Title, Is.EqualTo("merged"));
            Assert.That(configuration.Metadata.Title, Is.EqualTo("merged"));
            Assert.That(configuration.Metadata.RasterDpi, Is.EqualTo(72));
            Assert.That(configuration.PageLimit, Is.EqualTo(5));
        }

        [Test]
        public void Merge_RejectsNullAndEmptyInputs()
        {
            var document = CreateSolidPage(120, 100, "#ff0000");

            Assert.Throws<ArgumentNullException>(() =>
                Document.Merge((IEnumerable<IDocument>)null));
            Assert.Throws<ArgumentException>(() =>
                Document.Merge(Array.Empty<IDocument>()));
            Assert.Throws<ArgumentException>(() =>
                Document.Merge(new IDocument[] { document, null }));
        }

        [Test]
        public void Merge_FiresSubsetCallbackOnceAcrossAllSources()
        {
            var calls = 0;
            var fonts = 0;

            FontManager.RegisterSubsetCallback((manager, subsets) =>
            {
                calls++;
                fonts += subsets.Count();
            });

            try
            {
                var merged = Document.Merge(
                    CreateTextDocument("First source"),
                    CreateTextDocument("Second source"));

                Assert.DoesNotThrow(() => merged.GeneratePdf());
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(fonts, Is.GreaterThan(0));
            }
            finally
            {
                FontManager.RegisterSubsetCallback(null);
            }
        }

        [Test]
        public void Merge_SupportsSvgAndPreviewerCanvases()
        {
            var first = CreateSolidPage(120, 100, "#ff0000");
            var second = CreateSolidPage(200, 150, "#0000ff");
            var merged = Document.Merge(first, second);

            Assert.That(merged.GenerateSvg().Count(), Is.EqualTo(2));

            var pictures = DocumentGenerator.GeneratePreviewerPictures(merged);

            try
            {
                Assert.That(pictures.Count, Is.EqualTo(2));
            }
            finally
            {
                foreach (var picture in pictures)
                    picture.Picture.Dispose();
            }
        }

        [Test]
        public void Merge_ScopesRepeatedSectionNamesPerSource()
        {
            Document CreateSectionedDocument() => Document.Create(container => container.Page(page =>
            {
                page.Size(200, 150);
                page.Margin(10);
                page.PageColor("#ffffff");

                page.Content().Column(column =>
                {
                    column.Item().Section("chapter").Text("Chapter");
                    column.Item().Text(text => text.BeginPageNumberOfSection("chapter"));
                });

                page.Footer().Text(text => text.SectionLink("Back to chapter", "chapter"));
            }));

            var merged = Document.Merge(CreateSectionedDocument(), CreateSectionedDocument())
                .UseContinuousPageNumbers();

            Assert.DoesNotThrow(() => merged.GeneratePdf());
        }

        [Test]
        public void PageContext_DocumentIdsScopeSections()
        {
            var context = new PageContext();

            context.SetDocumentId(0);
            context.SetPageNumber(1);
            context.SetSectionPage("chapter");

            context.SetDocumentId(1);
            context.SetPageNumber(1);
            context.SetSectionPage("chapter");

            context.SetDocumentId(0);
            Assert.That(context.GetLocation("chapter")?.PageStart, Is.EqualTo(1));
            Assert.That(context.GetDocumentLocationName("chapter"), Is.EqualTo("0:chapter"));

            context.SetDocumentId(1);
            Assert.That(context.GetLocation("chapter")?.PageStart, Is.EqualTo(1));
            Assert.That(context.GetDocumentLocationName("chapter"), Is.EqualTo("1:chapter"));

            var unscoped = new PageContext();
            unscoped.SetPageNumber(1);
            unscoped.SetSectionPage("chapter");
            Assert.That(unscoped.GetDocumentLocationName("chapter"), Is.EqualTo("chapter"));
            Assert.That(unscoped.GetLocation("chapter"), Is.Not.Null);
        }

        private static Document CreateTwoPageDocument(Action<int, int?> record = null)
        {
            return Document.Create(container => container.Page(page =>
            {
                page.Size(120, 100);
                page.Margin(10);
                page.PageColor("#ffffff");

                page.Content().Column(column =>
                {
                    column.Item().Height(20).Background("#ff0000");
                    column.Item().PageBreak();
                    column.Item().Height(20).Background("#0000ff");
                });

                page.Footer().Height(20).Column(column =>
                {
                    column.Item().Text(text =>
                    {
                        text.CurrentPageNumber();
                        text.Span(" / ");
                        text.TotalPages();
                    });

                    if (record != null)
                    {
                        column.Item()
                            .ShowIf(context =>
                            {
                                record(context.PageNumber, context.TotalPages);
                                return false;
                            })
                            .Height(0);
                    }
                });
            }));
        }

        private static Document CreateTextDocument(string text)
        {
            return Document.Create(container => container.Page(page =>
            {
                page.Size(200, 150);
                page.Margin(10);
                page.Content().Text(text);
            }));
        }

        private static Document CreateSolidPage(int width, int height, string color)
        {
            return Document.Create(container => container.Page(page =>
            {
                page.Size(width, height);
                page.PageColor("#ffffff");
                page.Content().Background(color);
            }));
        }

        private static SKBitmap[] RenderPages(IDocument document, out int count)
        {
            document.GeneratePdf(out count);
            return document.GenerateImages()
                .Select(SKBitmap.Decode)
                .ToArray();
        }

        private static void AssertPageColors(SKBitmap[] pages, params string[] colors)
        {
            for (var index = 0; index < colors.Length; index++)
            {
                Assert.That(pages[index].GetPixel(50, 15), Is.EqualTo(SKColor.Parse(colors[index])),
                    $"Unexpected marker color on page {index + 1}.");
            }
        }

        private static List<(int Page, int? Total)> FinalRecords(
            IEnumerable<(int Page, int? Total)> records)
        {
            return records
                .Where(x => x.Total.HasValue)
                .Distinct()
                .OrderBy(x => x.Page)
                .ToList();
        }

        private static string[] Format(IEnumerable<(int Page, int? Total)> records)
        {
            return records.Select(x => $"{x.Page}/{x.Total}").ToArray();
        }

        private static void DisposePages(IEnumerable<SKBitmap> pages)
        {
            foreach (var page in pages)
                page.Dispose();
        }
    }
}
