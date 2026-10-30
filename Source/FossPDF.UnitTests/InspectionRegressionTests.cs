using System;
using System.Linq;
using System.Runtime.CompilerServices;
using FossPDF.Drawing;
using FossPDF.Elements;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.Previewer;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.UnitTests
{
    [TestFixture]
    public class InspectionRegressionTests
    {
        [Test]
        public void BalancedColumnsCaptureDeferredOccurrencesWithoutSpeculativeDraws()
        {
            var document = Document.Create(d => d.Page(page =>
            {
                page.Size(200, 200);
                page.Margin(10);
                page.Content().MultiColumn(columns =>
                {
                    columns.Columns(2);
                    columns.BalanceHeight();
                    columns.Content().Column(column =>
                    {
                        for (var index = 0; index < 8; index++)
                        {
                            var id = "row-" + index;
                            column.Item().Lazy(child => child.Inspect(id).Height(20).Background("#ff0000"));
                        }
                    });
                });
            }));
            using var preview = document.GenerateInspectedPreview();
            var ids = preview.Inspection.Nodes.Where(node => node.Type == "InspectionMarker").Select(node => node.Id).ToArray();
            Assert.That(ids, Has.Length.EqualTo(8));
            var rows = preview.Inspection.Occurrences.Where(row => ids.Contains(row.NodeId)).ToArray();
            Assert.That(rows, Has.Length.EqualTo(8));
            Assert.That(rows.Select(row => row.NodeId), Is.Unique);
            Assert.That(rows.Select(row => row.X).Distinct().Count(), Is.EqualTo(2));
            Assert.That(rows.All(row => row.Page == 1 && Math.Abs(row.Width - 90) < 0.25f && Math.Abs(row.Height - 20) < 0.25f), Is.True);
            Assert.That(rows.Select(row => (row.X, row.Y)), Is.EqualTo(Enumerable.Range(0, 8)
                .Select(index => (10f + index / 4 * 90, 10f + index % 4 * 20))));
            preview.Inspection.Validate(preview.Pictures.Count);
            var ordinary = DocumentGenerator.GeneratePreviewerPictures(document);
            try
            {
                Assert.That(ordinary.Count, Is.EqualTo(preview.Pictures.Count));
                foreach (var pair in ordinary.Zip(preview.Pictures, (expected, actual) => (expected, actual)))
                {
                    using var expected = Raster(pair.expected);
                    using var actual = Raster(pair.actual);
                    Assert.That(actual.Bytes, Is.EqualTo(expected.Bytes));
                }
            }
            finally { foreach (var picture in ordinary) picture.Picture.Dispose(); }
        }

        [Test]
        public void MultipleDynamicLayoutHelpersHaveDistinctStableRuntimeSlots()
        {
            var document = Document.Create(d => d.Page(page =>
            {
                page.Size(200, 200);
                page.Content().Dynamic(new LayoutHelpers());
            }));
            using var first = document.GenerateInspectedPreview();
            using var second = document.GenerateInspectedPreview();
            Assert.That(first.Inspection.Occurrences.Count(row => row.Text == "Rendered row"), Is.EqualTo(1));
            Assert.That(first.Inspection.Occurrences.Any(row => row.Text == "Measurement helper"), Is.False);
            Assert.That(first.Inspection.Nodes.Select(node => node.Id), Is.EqualTo(second.Inspection.Nodes.Select(node => node.Id)));
        }

        private sealed class LayoutHelpers : IDynamicComponent<int>
        {
            public int State { get; set; }
            public DynamicComponentComposeResult Compose(DynamicContext context)
            {
                context.CreateElement(child => child.Height(10).Text("Measurement helper"));
                return new DynamicComponentComposeResult
                {
                    Content = context.CreateElement(child => child.Row(row => row.RelativeItem().Text("Rendered row"))),
                    HasMoreContent = false
                };
            }
        }

        private static SKBitmap Raster(PreviewerPicture picture)
        {
            var bitmap = new SKBitmap((int)picture.Size.Width, (int)picture.Size.Height);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            canvas.DrawPicture(picture.Picture);
            return bitmap;
        }

        [Test]
        public void ProtocolRejectsAParentThatReferencesItself()
        {
            var snapshot = new InspectionSnapshot();
            snapshot.Nodes.Add(new InspectionNode { Id = "self", ParentId = "self", Type = "Container" });
            Assert.Throws<ArgumentException>(() => snapshot.Validate(1));
        }

        [Test]
        public void CollectorDoesNotRetainReleasedRuntimeElementTrees()
        {
            var collector = new InspectionCollector();
            var reference = ApplyAndRelease(collector);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.That(reference.IsAlive, Is.False);
            GC.KeepAlive(collector);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference ApplyAndRelease(InspectionCollector collector)
        {
            var container = new Container();
            container.Child = new Padding { Left = 5, Child = new Container() };
            collector.Apply(container);
            return new WeakReference(container);
        }
    }
}
