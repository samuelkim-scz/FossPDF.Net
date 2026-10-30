#if NET9_0
using System;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using FossPDF.Fluent;
using FossPDF.Previewer;
using NUnit.Framework;

namespace FossPDF.UnitTests
{
    [NonParallelizable]
    public class InspectionViewModelTests
    {
        [Test]
        public void SearchSelectionSourceAndRefreshUseTheCurrentSnapshot()
        {
            using var preview = InspectionTests.CreateDocument().GenerateInspectedPreview();
            using var model = new InspectionViewModel();
            model.Load(preview.Inspection, preview.Pictures.Count);
            model.Search = "Revenue";
            Assert.That(model.Matches.Count, Is.EqualTo(10));
            model.SelectedEntry = model.Matches.Last();
            Assert.That(model.Highlight!.Page, Is.GreaterThan(1));
            Assert.That(model.Details, Does.Contain("InspectionTests.cs").And.Contain("currency=EUR"));
            model.InteractionMode = 1;
            var item = model.Highlight;
            model.Pick(item.Page, item.X + 1, item.Y + 1);
            Assert.That(model.SelectedEntry, Is.Not.Null);
            Assert.That(model.Matches.Contains(model.SelectedEntry!), Is.True);
            model.Load(new InspectionSnapshot { Nodes = { new InspectionNode { Id = "hidden", Type = "Conditional" } } }, 1);
            model.SelectedTreeItem = model.Roots[0];
            Assert.That(model.Highlight, Is.Null);
            Assert.That(model.Details, Does.Contain("no rendered occurrence"));
            model.Load(null, 0);
            Assert.That(model.Available, Is.False);
            Assert.That(model.Highlight, Is.Null);
            Assert.That(model.Roots, Is.Empty);
            Assert.That(model.Matches, Is.Empty);
        }

        [Test]
        public void MeasurementsAreAxisSpecificAndSourceLinksAreEscaped()
        {
            using var preview = InspectionTests.CreateDocument().GenerateInspectedPreview();
            using var model = new InspectionViewModel();
            model.Load(preview.Inspection, preview.Pictures.Count);
            model.InteractionMode = 2;
            model.Pick(1, 10, 20); model.Pick(1, 46, 100);
            Assert.That(model.Measurement, Is.EqualTo(new InspectionMeasurement(1, 10, 20, 46, 20)));
            model.InteractionMode = 3;
            model.Pick(2, 20, 20); model.Pick(2, 100, 92);
            Assert.That(model.Measurement, Is.EqualTo(new InspectionMeasurement(2, 20, 20, 20, 92)));
            var uri = InspectionViewModel.SourceUri(new InspectionNode { SourceFile = "C:\\Example Folder\\a#b.cs", SourceLine = 42 });
            Assert.That(uri, Does.StartWith("vscode://file/").And.Contain("%20").And.Contain("%23").And.EndWith(":42"));
            Assert.That(InspectionViewModel.SourceUri(new InspectionNode { SourceFile = "/home/example/a b.cs", SourceLine = 9 }),
                Is.EqualTo("vscode://file/home/example/a%20b.cs:9"));
            model.Search = "Revenue"; model.SelectedEntry = model.Matches[0];
            var source = preview.Inspection.Nodes.First(node => node.Id.Contains("id:row-0"));
            source.SourceFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "missing-" + Guid.NewGuid() + ".cs");
            model.OpenSourceCommand.Execute().Wait();
            Assert.That(model.Status, Does.Contain("not present locally"));
        }

        [Test]
        public void LargeSearchesAreBoundedAndRefreshesReleaseOldSelection()
        {
            var snapshot = new InspectionSnapshot();
            for (var index = 0; index < 2000; index++)
            {
                var id = "node-" + index;
                snapshot.Nodes.Add(new InspectionNode { Id = id, Type = "Text", Label = "Revenue " + index });
                snapshot.Occurrences.Add(new InspectionOccurrence { NodeId = id, Page = 1, X = index, Width = 10, Height = 10, Text = "Revenue " + index });
            }
            using var model = new InspectionViewModel();
            for (var index = 0; index < 20; index++)
            {
                model.Load(snapshot, 1); model.Search = "Revenue";
                Assert.That(model.Matches.Count, Is.EqualTo(500));
                model.SelectedEntry = model.Matches[499];
                model.Load(null, 0);
                Assert.That(model.SelectedEntry, Is.Null);
                Assert.That(model.Roots, Is.Empty);
            }
        }

        [Test]
        public void CoordinateMappingTracksPanZoomAndPageSpacing()
        {
            using var preview = InspectionTests.CreateDocument().GenerateInspectedPreview();
            var pages = Enumerable.Range(0, 3).Select(_ => new PreviewPage(preview.Pictures[0].Picture, 200, 100)).ToArray();
            var canvas = new InteractiveCanvas { Pages = pages, Bounds = new Rect(0, 0, 800, 200) };
            Assert.That(canvas.PagePoint(310, 35), Is.EqualTo((1, 10f, 10f)));
            canvas.TranslateWithCurrentScale(0, 125);
            Assert.That(canvas.PagePoint(310, 35), Is.EqualTo((2, 10f, 10f)));
            canvas.ZoomToPoint(0, 0, 2);
            Assert.That(canvas.PagePoint(220, 45), Is.EqualTo((2, 10f, 10f)));
            Assert.That(canvas.PagePoint(-1000, -1000), Is.Null);
            canvas = new InteractiveCanvas { Pages = pages.Take(1).ToArray(), Bounds = new Rect(0, 0, 800, 400) };
            canvas.ScrollPercentY = 0;
            Assert.That(canvas.PagePoint(310, 185), Is.EqualTo((1, 10f, 10f)), "Picking must use the same centered transform as rendering even when the scrollbar clamps to zero.");
        }
    }
}
#endif
