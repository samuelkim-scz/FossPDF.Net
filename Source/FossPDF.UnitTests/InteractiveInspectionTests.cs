#if NET9_0
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using FossPDF.Fluent;
using FossPDF.Previewer;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.UnitTests;

[NonParallelizable]
public class InteractiveInspectionTests
{
    internal static InspectionSnapshot Snapshot() => new()
    {
        Nodes = new List<InspectionNode>
        {
            new() { Id = "root", Type = "Document" },
            new() { Id = "heading", ParentId = "root", Type = "Inspect", Label = "Report heading",
                Configuration = "currency=EUR", SourceFile = Path.GetFullPath("Inspection example.cs"), SourceLine = 42, SourceMember = "Compose" },
            new() { Id = "text", ParentId = "heading", Type = "TextBlock", Label = "Revenue" }
        },
        Occurrences = new List<InspectionOccurrence>
        {
            new() { NodeId = "heading", Page = 1, X = 10, Y = 10, Width = 100, Height = 20 },
            new() { NodeId = "text", Page = 1, X = 10, Y = 10, Width = 100, Height = 20, Text = "Revenue EUR 12.00" },
            new() { NodeId = "heading", Page = 2, X = 10, Y = 10, Width = 100, Height = 20 },
            new() { NodeId = "text", Page = 2, X = 10, Y = 10, Width = 100, Height = 20, Text = "Revenue EUR 12.00" }
        }
    };

    [TestCase(0.5)]
    [TestCase(1)]
    [TestCase(3)]
    public void PickingUsesTheSameTransformForMixedPageSizesPanAndZoom(double zoom)
    {
        var view = new PreviewViewport { Width = 800, Height = 350 };
        view.SetPages(new[] { new PreviewPageSize(200, 140), new PreviewPageSize(400, 240) });
        view.Zoom(new Point(400, 175), zoom);
        view.Pan(30, -40);
        foreach (var point in new[] { new PagePoint(1, 25, 50), new PagePoint(2, 200, 100) })
        {
            Assert.That(view.TryPick(view.ToViewport(point), out var actual), Is.True);
            Assert.That(actual.Page, Is.EqualTo(point.Page));
            Assert.That(actual.X, Is.EqualTo(point.X).Within(0.0001));
            Assert.That(actual.Y, Is.EqualTo(point.Y).Within(0.0001));
        }
        var gap = view.ToViewport(new PagePoint(1, 100, 150));
        Assert.That(view.TryPick(gap, out _), Is.False, "The inter-page gap is not page content.");
        Assert.That(view.TryPick(new Point(double.NaN, 0), out _), Is.False);
    }

    [Test]
    public void ZoomKeepsThePointUnderThePointerAndEmptyScrollIsFinite()
    {
        var view = new PreviewViewport { Width = 500, Height = 250 };
        Assert.That(view.Scroll, Is.Zero); Assert.That(view.ScrollViewport, Is.EqualTo(1));
        view.SetPages(new[] { new PreviewPageSize(600, 800) });
        var anchor = new Point(220, 150);
        Assert.That(view.TryPick(anchor, out var before), Is.True);
        view.Zoom(anchor, 2);
        Assert.That(view.TryPick(anchor, out var after), Is.True);
        Assert.That(after.X, Is.EqualTo(before.X).Within(0.0001));
        Assert.That(after.Y, Is.EqualTo(before.Y).Within(0.0001));
        view.SetPages(Array.Empty<PreviewPageSize>());
        view.Scroll = float.NaN;
        Assert.That(view.Scroll, Is.Zero); Assert.That(view.ScrollViewport, Is.EqualTo(1));
    }

    [Test]
    public async Task HierarchySearchSelectionAndSourceFallbackUseOccurrenceData()
    {
        using var model = new InspectionViewModel(action => action());
        model.Refresh(Snapshot(), 2);
        model.Query = "currency=EUR"; await model.SearchCompletion;
        Assert.That(model.Entries.Select(entry => entry.Node.Id), Is.EqualTo(new[] { "heading" }));
        var focused = 0;
        model.FocusRequested += _ => focused++;
        model.Selected = model.Entries[0];
        Assert.That(model.Highlights.Count, Is.EqualTo(2));
        model.SelectedOccurrence = model.Occurrences[1];
        Assert.That(model.SelectedOccurrence.Value.Page, Is.EqualTo(2));
        Assert.That(focused, Is.EqualTo(2));
        model.Query = "Revenue"; await model.SearchCompletion;
        Assert.That(model.Entries.Single().Node.Id, Is.EqualTo("text"));
        model.Selected = model.Entries[0];
        Assert.That(model.SourceNode!.Id, Is.EqualTo("heading"), "Unannotated descendants use their closest source boundary.");
        Assert.That(model.CopyLocation, Does.EndWith("Inspection example.cs:42"));
        model.Pick(new PagePoint(2, 20, 15));
        Assert.That(model.Selected!.Node.Id, Is.EqualTo("text"));
        Assert.That(model.Entries, Does.Contain(model.Selected), "Picking reveals collapsed or filtered ancestors.");
        Assert.That(model.SelectedOccurrence!.Value.Page, Is.EqualTo(2));
        model.SelectParent();
        Assert.That(model.Selected!.Node.Id, Is.EqualTo("heading"));
        model.ToggleSelected();
        Assert.That(model.Occurrences.Count, Is.EqualTo(2));
    }

    [Test]
    public void MeasurementCoordinatesAndLegacyRefreshClearStaleState()
    {
        using var model = new InspectionViewModel(action => action());
        model.Refresh(Snapshot(), 2);
        model.Tool = InspectionViewModel.Tools[2];
        model.CoordinatePage = "2"; model.CoordinateX = "10"; model.CoordinateY = "10"; model.UseCoordinates();
        model.CoordinateX = "110"; model.CoordinateY = "35"; model.UseCoordinates();
        Assert.That(model.Measurement!.Value.Points, Is.EqualTo(100).Within(0.25));
        Assert.That(model.MeasurementText, Does.Contain("35.28 mm"));
        model.Tool = InspectionViewModel.Tools[3];
        model.Pick(new PagePoint(1, 10, 10)); model.UpdatePointer(new PagePoint(1, 99, 40));
        Assert.That(model.Measurement!.Value.Points, Is.EqualTo(30).Within(0.25));
        model.Pick(new PagePoint(2, 10, 20));
        Assert.That(model.Measurement!.Value.Start.Page, Is.EqualTo(2), "Cross-page clicks start a new measurement.");
        model.CoordinateX = "NaN"; model.UseCoordinates();
        Assert.That(model.Status, Does.Contain("finite"));
        model.Refresh(null, 2);
        Assert.That(model.Selected, Is.Null); Assert.That(model.Highlights, Is.Empty);
        Assert.That(model.Measurement, Is.Null); Assert.That(model.Hovered, Is.Null);
        Assert.That(model.Diagnostic, Is.Empty);
        model.Pick(new PagePoint(1, 0, 0)); model.Pick(new PagePoint(1, 72, 72));
        Assert.That(model.Measurement!.Value.Points, Is.EqualTo(72), "Legacy previews still support measurement.");
    }

    [Test]
    public async Task LargeSearchIsBoundedAndCanceledResultsCannotOverwriteRefreshOrDisposal()
    {
        var snapshot = new InspectionSnapshot();
        for (var i = 0; i < 20000; i++) snapshot.Nodes.Add(new InspectionNode { Id = "node-" + i, Type = "Revenue" });
        var queued = new List<Action>();
        var model = new InspectionViewModel(action => queued.Add(action));
        model.Refresh(snapshot, 0);
        model.Query = "Revenue"; await model.SearchCompletion;
        Assert.That(queued.Count, Is.EqualTo(1));
        queued[0]();
        Assert.That(model.Entries.Count, Is.EqualTo(500));
        Assert.That(model.SearchStatus, Does.Contain("first 500"));
        model.Query = "node-19999"; await model.SearchCompletion;
        var stale = queued.Last();
        model.Refresh(null, 0);
        stale();
        Assert.That(model.Entries, Is.Empty);
        model.Query = "anything"; await model.SearchCompletion;
        var afterClose = queued.Last();
        model.Dispose(); model.Dispose(); afterClose();
        Assert.That(model.Entries, Is.Empty); Assert.That(model.Occurrences, Is.Empty);
    }

    [Test]
    public async Task DiagnosticAndRepeatedRefreshesReplaceAllPriorInspectionState()
    {
        using var model = new InspectionViewModel(action => action());
        for (var i = 0; i < 20; i++)
        {
            var snapshot = Snapshot(); snapshot.Diagnostic = "DocumentLayoutException: content exceeds available space";
            model.Refresh(snapshot, 2);
            model.Query = "Revenue"; await model.SearchCompletion;
            model.Selected = model.Entries.Single();
            Assert.That(model.HasDiagnostic, Is.True);
            Assert.That(model.Highlights.Count, Is.EqualTo(2));
            model.Query = "";
        }
        model.Refresh(new InspectionSnapshot(), 0);
        Assert.That(model.HasDiagnostic, Is.False); Assert.That(model.Highlights, Is.Empty);
    }

    [Test]
    public void SourceNavigationUsesSeparateArgumentsAndHasUsefulMissingEditorErrors()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "source-navigation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "Example & $ source.cs");
        var editorPath = Path.Combine(directory, "Code.exe");
        try
        {
            File.WriteAllText(sourcePath, "// source"); File.WriteAllText(editorPath, "");
            var source = new InspectionNode { SourceFile = sourcePath, SourceLine = 42 };
            var start = SourceNavigation.CreateStartInfo(editorPath, source);
            Assert.That(start.UseShellExecute, Is.False);
            Assert.That(start.CreateNoWindow, Is.True);
            Assert.That(start.ArgumentList, Is.EqualTo(new[] { "--goto", sourcePath + ":42" }));
            Assert.Throws<InvalidOperationException>(() => SourceNavigation.CreateStartInfo("", source));
            source.SourceFile = "relative.cs";
            Assert.Throws<InvalidOperationException>(() => SourceNavigation.CreateStartInfo(editorPath, source));
        }
        finally { File.Delete(sourcePath); File.Delete(editorPath); Directory.Delete(directory); }
    }

    [Test]
    public void RenderingDefersScrollBindingNotificationsUntilAfterTheRenderPass()
    {
        Avalonia.Skia.SkiaPlatform.Initialize();
        var control = new PreviewerControl();
        control.Arrange(new Rect(0, 0, 500, 300));
        var notifications = 0;
        control.PropertyChanged += (_, change) =>
        {
            if (change.Property == PreviewerControl.CurrentScrollProperty || change.Property == PreviewerControl.ScrollViewportSizeProperty) notifications++;
        };
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(500, 300), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext()) control.Render(context);
        Assert.That(notifications, Is.Zero, "Changing a scroll binding during Render invalidates the visual during Avalonia's render pass.");
    }

    [Test]
    public void QueuedFramesKeepRetiredPicturesAliveWithoutBlockingUiUpdates()
    {
        using var recorder = new SKPictureRecorder();
        recorder.BeginRecording(new SKRect(0, 0, 100, 100));
        var picture = recorder.EndRecording();
        var page = new PreviewPage(picture, 100, 100);
        var view = new PreviewViewport { Width = 200, Height = 200 };
        view.SetPages(new[] { new PreviewPageSize(100, 100) });
        var frame = new InteractiveCanvas { Bounds = new Rect(0, 0, 200, 200), Pages = new[] { page }, Viewport = view.Snapshot() };
        frame.RetainPictures();
        var originalScale = frame.Viewport.Scale;
        view.Zoom(new Point(100, 100), 2);
        Assert.That(frame.Viewport.Scale, Is.EqualTo(originalScale), "Queued frame transforms must be immutable.");
        page.Dispose(); page.Dispose();
        Assert.That(picture.Handle, Is.Not.EqualTo(IntPtr.Zero));
        using var bitmap = new SKBitmap(200, 200); using var canvas = new SKCanvas(bitmap);
        Assert.DoesNotThrow(() => frame.Draw(canvas));
        frame.Dispose(); frame.Dispose();
        Assert.That(picture.Handle, Is.EqualTo(IntPtr.Zero));
        Assert.That(page.Retain(), Is.False);
    }

    [Test]
    public void CanvasHighlightsAndMagnifierUseCapturedGeometry()
    {
        using var recorder = new SKPictureRecorder();
        var recording = recorder.BeginRecording(new SKRect(0, 0, 200, 140));
        using var red = new SKPaint { Color = SKColors.Red };
        recording.DrawRect(20, 20, 40, 20, red);
        using var picture = recorder.EndRecording();
        var canvas = new InteractiveCanvas
        {
            Bounds = new Rect(0, 0, 400, 300),
            Pages = new[] { new PreviewPage(picture, 200, 140) },
            Highlights = new[] { new InspectionOccurrence { Page = 1, X = 20, Y = 20, Width = 40, Height = 20 } }
        };
        canvas.Viewport.Width = 400; canvas.Viewport.Height = 300;
        canvas.Viewport.SetPages(new[] { new PreviewPageSize(200, 140) });
        using var bitmap = new SKBitmap(400, 300); using var target = new SKCanvas(bitmap);
        canvas.Draw(target);
        var corner = canvas.Viewport.ToViewport(new PagePoint(1, 20, 20));
        var color = bitmap.GetPixel((int)corner.X, (int)corner.Y);
        Assert.That(color.Blue, Is.GreaterThan(100), "Selection border is painted at the captured corner.");
        var center = canvas.Viewport.ToViewport(new PagePoint(1, 40, 30));
        Assert.That(bitmap.GetPixel((int)center.X, (int)center.Y).Red, Is.GreaterThan(150));
        canvas.Magnify = true; canvas.Pointer = new PagePoint(1, 40, 30);
        Assert.DoesNotThrow(() => canvas.Draw(target));
    }
}
#endif
