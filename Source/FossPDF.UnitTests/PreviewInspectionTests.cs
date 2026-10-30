using System;
using System.Linq;
using System.Runtime.CompilerServices;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.Previewer;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.UnitTests
{
    [TestFixture]
    public class PreviewInspectionTests
    {
        internal static Document Sample(int rows = 12) => Document.Create(document => document.Page(page =>
        {
            page.Size(200, 180);
            page.Margin(10);
            page.Header().Inspect("header", "Repeated heading").Height(20).Text("Revenue");
            page.Content().Column(column =>
            {
                for (var index = 0; index < rows; index++)
                    Row(column.Item(), "row-" + index);
            });
        }));

        private static void Row(IContainer container, string id) =>
            container.Inspect(id).Height(30).PaddingLeft(5).Text("Revenue row");

        internal static PreviewerOptions Enabled(int maxElements = 20000, int maxOccurrences = 100000) =>
            new() { EnableInspection = true, MaxElements = maxElements, MaxOccurrences = maxOccurrences };

        [Test]
        public void InspectionIsOptIn_AndDoesNotChangePixels()
        {
            using var ordinary = Sample().GeneratePreviewerSnapshot();
            using var inspected = Sample().GeneratePreviewerSnapshot(Enabled());
            Assert.That(ordinary.Inspection, Is.Null);
            Assert.That(inspected.Pictures.Count, Is.EqualTo(ordinary.Pictures.Count));
            foreach (var pair in ordinary.Pictures.Zip(inspected.Pictures, (a, b) => (a, b)))
            {
                using var expected = Raster(pair.a);
                using var actual = Raster(pair.b);
                Assert.That(actual.Bytes, Is.EqualTo(expected.Bytes));
            }
        }

        internal static SKBitmap Raster(FossPDF.Drawing.PreviewerPicture picture)
        {
            var bitmap = new SKBitmap((int)picture.Size.Width, (int)picture.Size.Height);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            canvas.DrawPicture(picture.Picture);
            return bitmap;
        }

        [Test]
        public void IDsHierarchySourceAndRepeatedGeometryAreStableInRelease()
        {
            using var first = Sample().GeneratePreviewerSnapshot(Enabled());
            using var second = Sample().GeneratePreviewerSnapshot(Enabled());
            var data = first.Inspection!;
            Assert.That(data.Elements.Select(x => x.Id), Is.EqualTo(second.Inspection!.Elements.Select(x => x.Id)));
            Assert.That(data.Elements.Select(x => x.Id), Is.Unique);
            PreviewerProtocol.Validate(data, first.Pictures.Count);
            var heading = data.Elements.Single(x => x.Id == "id:header");
            Assert.That(heading.Source!.File, Does.EndWith("PreviewInspectionTests.cs"));
            Assert.That(heading.Source.Line, Is.GreaterThan(0));
            Assert.That(heading.Source.Member, Is.EqualTo(nameof(Sample)));
            Assert.That(data.Elements.Single(x => x.Id == "id:row-0").Source!.Member, Is.EqualTo(nameof(Row)));
            var occurrences = data.Occurrences.Where(x => x.ElementId == heading.Id).ToArray();
            Assert.That(occurrences.Length, Is.EqualTo(first.Pictures.Count));
            Assert.That(occurrences.Select(x => x.PageNumber), Is.EqualTo(Enumerable.Range(1, first.Pictures.Count)));
            Assert.That(occurrences.All(x => Math.Abs(x.X - 10) < 0.25 && Math.Abs(x.Y - 10) < 0.25 &&
                Math.Abs(x.Width - 180) < 0.25 && Math.Abs(x.Height - 20) < 0.25), Is.True);
            Assert.That(data.Elements.Any(x => x.Type == "Padding" && x.Configuration["Left"] == "5"), Is.True);
        }

        [Test]
        public void TransformsDescribeAllFourCorners()
        {
            var document = Document.Create(d => d.Page(p =>
            {
                p.Size(200, 200);
                p.Content().Padding(20).Width(40).Height(60).Rotate(30).Inspect("rotated").Background("#ff0000");
            }));
            using var snapshot = document.GeneratePreviewerSnapshot(Enabled());
            var occurrence = snapshot.Inspection!.Occurrences.Single(x => x.ElementId == "id:rotated");
            Assert.That(occurrence.Transform, Has.Length.EqualTo(6));
            Assert.That(occurrence.Width, Is.GreaterThan(occurrence.LocalWidth));
            PreviewerProtocol.Validate(snapshot.Inspection, 1);
        }

        [Test]
        public void LazyDescendantsAndInlineElementsAreInspected()
        {
            var document = Document.Create(d => d.Page(p =>
            {
                p.Size(200, 200);
                p.Content().Lazy(c => c.Inspect("deferred").Text(t =>
                {
                    t.Span("before ");
                    t.Element().Inspect("inline").Width(10).Height(10).Background("#ff0000");
                }));
            }));
            using var snapshot = document.GeneratePreviewerSnapshot(Enabled());
            Assert.That(snapshot.Inspection!.Occurrences.Any(x => x.ElementId == "id:deferred"), Is.True);
            Assert.That(snapshot.Inspection.Occurrences.Any(x => x.ElementId == "id:inline"), Is.True);
            PreviewerProtocol.Validate(snapshot.Inspection, snapshot.Pictures.Count);
        }

        [Test]
        public void BalancedColumnsExcludeSpeculativeDrawsAndKeepDeferredGeometry()
        {
            var document = Document.Create(d => d.Page(p =>
            {
                p.Size(200, 200);
                p.Margin(10);
                p.Content().MultiColumn(columns =>
                {
                    columns.Columns(2);
                    columns.BalanceHeight();
                    columns.Content().Column(c =>
                    {
                        for (var index = 0; index < 8; index++)
                        {
                            var id = "column-row-" + index;
                            c.Item().Lazy(item => item.Inspect(id).Height(20).Background("#ff0000"));
                        }
                    });
                });
            }));
            using var snapshot = document.GeneratePreviewerSnapshot(Enabled());
            var rows = snapshot.Inspection!.Occurrences.Where(x => x.ElementId.StartsWith("id:column-row-") && !x.ElementId.Contains("/")).ToArray();
            Assert.That(rows, Has.Length.EqualTo(8));
            Assert.That(rows.Select(x => x.ElementId), Is.Unique);
            Assert.That(rows.Select(x => x.X).Distinct().Count(), Is.EqualTo(2));
            PreviewerProtocol.Validate(snapshot.Inspection, snapshot.Pictures.Count);
        }

        [Test]
        public void DynamicComponentsCaptureFinalContent()
        {
            var document = Document.Create(d => d.Page(p =>
            {
                p.Size(200, 200);
                p.Content().Dynamic(new InspectionDynamicComponent());
            }));
            using var snapshot = document.GeneratePreviewerSnapshot(Enabled());
            Assert.That(snapshot.Inspection!.Occurrences.Count(x => x.ElementId == "id:dynamic"), Is.EqualTo(1));
            PreviewerProtocol.Validate(snapshot.Inspection, snapshot.Pictures.Count);
        }

        private sealed class InspectionDynamicComponent : IDynamicComponent<int>
        {
            public int State { get; set; }
            public DynamicComponentComposeResult Compose(FossPDF.Elements.DynamicContext context) => new()
            {
                Content = context.CreateElement(c => c.Inspect("dynamic").Height(20).Text("Dynamic revenue")),
                HasMoreContent = false
            };
        }

        [Test]
        public void DuplicateIDsAndResourceLimitsFailClearly()
        {
            var duplicate = Document.Create(d => d.Page(p => p.Content().Column(c =>
            {
                c.Item().Inspect("same").Text("one");
                c.Item().Inspect("same").Text("two");
            })));
            Assert.That(() => duplicate.GeneratePreviewerSnapshot(Enabled()), Throws.Exception.With.Message.Contains("Duplicate"));
            Assert.That(() => Sample().GeneratePreviewerSnapshot(Enabled(maxElements: 2)), Throws.Exception.With.Message.Contains("element limit"));
            Assert.That(() => Sample().GeneratePreviewerSnapshot(Enabled(maxOccurrences: 2)), Throws.Exception);
            Assert.That(() => Sample().GeneratePreviewerSnapshot(new PreviewerOptions { MaxElements = 0 }), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void LargeDocumentIsBoundedAndTwentyRefreshesReleaseNativePictures()
        {
            using (var large = Sample(400).GeneratePreviewerSnapshot(Enabled()))
            {
                Assert.That(large.Pictures.Count, Is.GreaterThan(50));
                Assert.That(large.Inspection!.Elements.Count, Is.LessThan(20000));
                PreviewerProtocol.Validate(large.Inspection, large.Pictures.Count);
            }
            for (var index = 0; index < 20; index++)
            {
                var snapshot = Sample(1).GeneratePreviewerSnapshot(Enabled());
                var pictures = snapshot.Pictures.Select(x => x.Picture).ToArray();
                snapshot.Dispose();
                snapshot.Dispose();
                Assert.That(pictures.All(x => x.Handle == IntPtr.Zero), Is.True);
            }
        }

        [Test]
        public void DisposedSnapshotsDoNotRetainDocumentOrComponentClosures()
        {
            var reference = GenerateAndRelease();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.That(reference.IsAlive, Is.False);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference GenerateAndRelease()
        {
            var document = Sample();
            using var snapshot = document.GeneratePreviewerSnapshot(Enabled());
            return new WeakReference(document);
        }
    }
}
