using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FossPDF.Drawing;
using FossPDF.Drawing.Exceptions;
using FossPDF.Elements;
using FossPDF.Fluent;
using FossPDF.Helpers;
using FossPDF.Infrastructure;
using NUnit.Framework;
using SharedImage = FossPDF.Infrastructure.Image;

namespace FossPDF.UnitTests
{
    [TestFixture]
    [NonParallelizable]
    public class LazyLifecycleTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void DrawingCancellation_ReleasesDocumentFontsAndBorrowsSharedImage(bool cache)
        {
            using var image = SharedImage.FromBinaryData(Placeholders.Image(20, 20));
            DocumentSpecificFontManager manager = null;
            var cancel = true;
            try
            {
                FontManager.RegisterSubsetCallback((fonts, _) => manager = fonts);
                var document = Document.Create(container => container.Page(page =>
                {
                    page.Size(120, 100);
                    var content = page.Content();
                    void Compose(IContainer target) => target.Column(column =>
                    {
                        column.Item().Text("before cancellation");
                        column.Item().Width(20).Height(20).Image(image);
                        column.Item().Height(5).Canvas((_, _) =>
                        {
                            if (cancel)
                                throw new OperationCanceledException("Rendering cancelled.");
                        });
                    });

                    if (cache)
                        content.LazyWithCache(Compose);
                    else
                        content.Lazy(Compose);
                }));

                var failure = Assert.Throws<DocumentDrawingException>(() => document.GeneratePdf());
                Assert.That(failure.InnerException, Is.TypeOf<OperationCanceledException>());
                Assert.That(manager, Is.Not.Null);
                Assert.That(manager.SafeToDisposeAtSubsetTime.IsEmpty, Is.True);
                Assert.That(manager.Fonts.IsEmpty, Is.True);
                Assert.That(image.SkImage.Handle, Is.Not.EqualTo(IntPtr.Zero));

                cancel = false;
                Assert.That(document.GeneratePdf(), Is.Not.Empty);
                Assert.That(image.SkImage.Handle, Is.Not.EqualTo(IntPtr.Zero));
            }
            finally
            {
                FontManager.RegisterSubsetCallback(null);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DynamicContent_InBalancedColumnsMatchesEagerOutput(bool cache)
        {
            Document Create(bool deferred) => Document.Create(container => container.Page(page =>
            {
                page.Size(120, 100);
                page.Margin(10);
                page.PageColor("#ffffff");
                page.Content().MultiColumn(columns =>
                {
                    columns.Columns(2);
                    columns.Spacing(10);
                    columns.BalanceHeight();
                    void Compose(IContainer target) => target.Dynamic(new ColorSequence());

                    if (!deferred)
                        Compose(columns.Content());
                    else if (cache)
                        columns.Content().LazyWithCache(Compose);
                    else
                        columns.Content().Lazy(Compose);
                });
            }));

            var expected = Create(false).GenerateImages().ToArray();
            Assert.That(expected.Length, Is.EqualTo(2));
            Assert.That(Create(true).GenerateImages().ToArray(), Is.EqualTo(expected));
        }

        [Test]
        public void LargeReport_RecordsRetainedManagedMemoryAndNativeLifetime()
        {
            Document.Create(container => container.Page(page => page.Content().Text("warm up"))).GeneratePdf();
            var results = new[] { "eager", "lazy", "cached" }.Select(MeasureReport).ToArray();
            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "parity-artifacts", "LAY-106");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "retained-memory.json");
            File.WriteAllText(path, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            TestContext.AddTestAttachment(path, "Retained element-tree memory and process working-set observations.");

            foreach (var result in results)
            {
                TestContext.Progress.WriteLine(
                    $"{result.Mode}: retained managed {result.RetainedManagedBytes} bytes, " +
                    $"working set {result.WorkingSetBytes} bytes, retained elements {result.RetainedElements}.");
                Assert.That(result.FontResourcesAfterGeneration, Is.Zero);
                Assert.That(result.Pages, Is.EqualTo(results[0].Pages));
            }

            Assert.That(results.Single(x => x.Mode == "lazy").RetainedElements,
                Is.LessThan(results.Single(x => x.Mode == "cached").RetainedElements));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static MemoryObservation MeasureReport(string mode)
        {
            const int sections = 64;
            const int rows = 32;
            var roots = new List<Element>();
            DocumentSpecificFontManager manager = null;
            int pages;
            try
            {
                FontManager.RegisterSubsetCallback((fonts, _) => manager = fonts);
                var document = Document.Create(container => container.Page(page =>
                {
                    page.Size(240, 300);
                    page.Margin(10);
                    page.DefaultTextStyle(style => style.FontSize(6));
                    page.Content().Column(column =>
                    {
                        for (var section = 0; section < sections; section++)
                        {
                            var sectionIndex = section;
                            void Compose(IContainer target) => target.Column(items =>
                            {
                                for (var row = 0; row < rows; row++)
                                    items.Item().Height(12).Text($"Report section {sectionIndex}, row {row}: retained content");
                            });

                            if (mode == "eager")
                            {
                                var content = new Container();
                                roots.Add(content);
                                column.Item().Element(content);
                                Compose(content);
                            }
                            else
                            {
                                var content = new Lazy { IsCacheable = mode == "cached", ContentSource = Compose };
                                roots.Add(content);
                                column.Item().Element(content);
                            }
                        }
                    });
                }));

                document.GeneratePdf(out pages);
            }
            finally
            {
                FontManager.RegisterSubsetCallback(null);
            }

            var elements = 0;
            foreach (var root in roots)
                root.VisitChildren(_ => elements++);
            var retained = GC.GetTotalMemory(forceFullCollection: true);
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var workingSet = process.WorkingSet64;
            var nativeResources = manager.SafeToDisposeAtSubsetTime.Count + manager.Fonts.Count;
            roots.Clear();
            var released = GC.GetTotalMemory(forceFullCollection: true);
            return new MemoryObservation(mode, pages, Math.Max(0, retained - released), workingSet, elements, nativeResources);
        }

        private sealed record MemoryObservation(string Mode, int Pages, long RetainedManagedBytes,
            long WorkingSetBytes, int RetainedElements, int FontResourcesAfterGeneration);

        private sealed class ColorSequence : IDynamicComponent<int>
        {
            public int State { get; set; }

            public DynamicComponentComposeResult Compose(DynamicContext context)
            {
                var start = State;
                var count = Math.Min(12 - State, Math.Max(0, (int)(context.AvailableSize.Height / 20)));
                State += count;
                return new DynamicComponentComposeResult
                {
                    Content = context.CreateElement(content => content.Column(items =>
                    {
                        for (var index = start; index < start + count; index++)
                            items.Item().Height(20).Background(index % 2 == 0 ? "#ff0000" : "#0000ff");
                    })),
                    HasMoreContent = State < 12
                };
            }
        }
    }
}
