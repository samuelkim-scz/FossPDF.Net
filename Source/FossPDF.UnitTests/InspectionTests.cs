using System;
using System.Linq;
using System.Text.Json;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.Previewer;
using NUnit.Framework;

namespace FossPDF.UnitTests
{
    [NonParallelizable]
    public class InspectionTests
    {
        internal static Document CreateDocument() => Document.Create(container => container.Page(page =>
        {
            page.Size(200, 140); page.Margin(10);
            page.Header().Inspect("heading", "Report heading").Text("Quarterly report");
            page.Content().Column(column =>
            {
                for (var index = 0; index < 10; index++)
                    column.Item().Inspect("row-" + index, configuration: "currency=EUR").Height(22).Text("Revenue " + index);
            });
        })).WithSettings(new DocumentSettings { EnableCaching = true });

        [Test]
        public void HierarchySourceAndRepeatedGeometrySurviveReleaseRendering()
        {
            using var preview = CreateDocument().GenerateInspectedPreview();
            var snapshot = preview.Inspection;
            snapshot.Validate(preview.Pictures.Count);
            Assert.That(preview.Pictures.Count, Is.GreaterThan(1));
            var heading = snapshot.Nodes.Single(node => node.Id.EndsWith("/id:heading", StringComparison.Ordinal));
            Assert.That(heading.SourceFile, Does.EndWith("InspectionTests.cs"));
            Assert.That(heading.SourceLine, Is.GreaterThan(0));
            Assert.That(heading.SourceMember, Is.EqualTo(nameof(CreateDocument)));
            Assert.That(snapshot.Occurrences.Count(x => x.NodeId == heading.Id), Is.EqualTo(preview.Pictures.Count));
            Assert.That(snapshot.Occurrences.Where(x => x.NodeId == heading.Id).All(x => Math.Abs(x.X - 10) < 0.1f && Math.Abs(x.Y - 10) < 0.1f), Is.True);
            Assert.That(snapshot.Occurrences.Any(x => x.Text?.Contains("Revenue 9") == true), Is.True);
            using var second = CreateDocument().GenerateInspectedPreview();
            Assert.That(second.Inspection.Nodes.Select(x => x.Id), Is.EqualTo(snapshot.Nodes.Select(x => x.Id)));
            var roundTrip = JsonSerializer.Deserialize<InspectionSnapshot>(JsonSerializer.Serialize(snapshot));
            Assert.DoesNotThrow(() => roundTrip!.Validate(preview.Pictures.Count));
        }

        [Test]
        public void LazyAndMergedContentIsInspectedWithPhysicalPageNumbers()
        {
            var document = Document.Create(container => container.Page(page =>
                page.Content().Lazy(child => child.Inspect("lazy").Text("Deferred revenue"))));
            using var preview = Document.Merge(document, document).GenerateInspectedPreview();
            Assert.That(preview.Pictures.Count, Is.EqualTo(2));
            Assert.That(preview.Inspection.Nodes.Count(x => x.Id.EndsWith("/id:lazy", StringComparison.Ordinal)), Is.EqualTo(2));
            Assert.That(preview.Inspection.Occurrences.Where(x => x.Text == "Deferred revenue").Select(x => x.Page), Is.EquivalentTo(new[] { 1, 2 }));
        }

        [Test]
        public void DisposalAndRefreshDoNotKeepNativePicturesAlive()
        {
            for (var index = 0; index < 20; index++)
            {
                var preview = CreateDocument().GenerateInspectedPreview();
                preview.Dispose(); preview.Dispose();
                Assert.That(preview.Pictures.All(x => x.Picture.Handle == IntPtr.Zero), Is.True);
            }
        }

        [Test]
        public void LargeDocumentsRemainWithinTheDeclaredProtocolBounds()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(300, 500); page.Margin(12);
                page.Content().Column(column =>
                {
                    for (var index = 0; index < 500; index++)
                        column.Item().Inspect("line-" + index).Height(24).Text("Revenue line " + index);
                });
            }));
            using var preview = document.GenerateInspectedPreview();
            Assert.That(preview.Pictures.Count, Is.GreaterThan(20));
            Assert.That(preview.Inspection.Nodes.Count, Is.GreaterThan(1000));
            Assert.DoesNotThrow(() => preview.Inspection.Validate(preview.Pictures.Count));
        }

        [Test]
        public void MalformedProtocolAndInvalidReferencesAreRejected()
        {
            var snapshot = new InspectionSnapshot { Version = 2 };
            Assert.Throws<ArgumentException>(() => snapshot.Validate(1));
            snapshot.Version = 1;
            snapshot.Nodes.Add(new InspectionNode { Id = "a", ParentId = "missing" });
            Assert.Throws<ArgumentException>(() => snapshot.Validate(1));
            snapshot.Nodes[0].ParentId = null;
            snapshot.Occurrences.Add(new InspectionOccurrence { NodeId = "a", Page = 2, Width = 1, Height = 1 });
            Assert.Throws<ArgumentException>(() => snapshot.Validate(1));
            snapshot.Occurrences[0].Page = 1;
            snapshot.Occurrences[0].X = float.NaN;
            Assert.Throws<ArgumentException>(() => snapshot.Validate(1));
            snapshot.Occurrences[0].X = 0;
            Assert.DoesNotThrow(() => snapshot.Validate(1));
            snapshot.Nodes.Add(new InspectionNode { Id = "a" });
            Assert.Throws<ArgumentException>(() => snapshot.Validate(1));
        }
    }
}
