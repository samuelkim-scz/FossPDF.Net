using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace FossPDF.Previewer;

internal sealed class InteractiveCanvas : ICustomDrawOperation
{
    public Rect Bounds { get; set; }
    internal PreviewViewport Viewport { get; set; } = new();
    internal IReadOnlyList<PreviewPage> Pages { get; set; } = Array.Empty<PreviewPage>();
    internal InspectionOccurrence[] Highlights { get; set; } = Array.Empty<InspectionOccurrence>();
    internal InspectionMeasurement? Measurement { get; set; }
    internal PagePoint? Pointer { get; set; }
    internal bool Magnify { get; set; }
    private PreviewPage[] retained = Array.Empty<PreviewPage>();

    internal void RetainPictures()
    {
        retained = Pages.Where(page => page.Retain()).ToArray();
    }

    public void Render(ImmediateDrawingContext context)
    {
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (feature == null) return;
        using var lease = feature.Lease();
        Draw(lease.SkCanvas);
    }

    // Shared with raster regression tests. Coordinates are device-independent pixels.
    internal void Draw(SKCanvas canvas)
    {
        canvas.Save();
        try
        {
            canvas.ClipRect(new SKRect(0, 0, (float)Bounds.Width, (float)Bounds.Height));
            for (var index = 0; index < Pages.Count; index++)
            {
                var bounds = Viewport.PageBounds(index + 1);
                if (bounds.Bottom < 0 || bounds.Top > Bounds.Height || Pages[index].Picture.Handle == IntPtr.Zero) continue;
                DrawPage(canvas, index, bounds);
            }
            using var highlight = new SKPaint { Color = SKColors.DeepSkyBlue, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
            using var fill = new SKPaint { Color = SKColors.DeepSkyBlue.WithAlpha(35) };
            foreach (var occurrence in Highlights)
            {
                if (occurrence.Page < 1 || occurrence.Page > Pages.Count) continue;
                var start = Viewport.ToViewport(new PagePoint(occurrence.Page, occurrence.X, occurrence.Y));
                var rect = new SKRect((float)start.X, (float)start.Y,
                    (float)(start.X + occurrence.Width * Viewport.Scale), (float)(start.Y + occurrence.Height * Viewport.Scale));
                canvas.DrawRect(rect, fill); canvas.DrawRect(rect, highlight);
            }
            if (Measurement is { } measurement && measurement.Start.Page <= Pages.Count)
            {
                var end = measurement.Horizontal ? measurement.End with { Y = measurement.Start.Y } : measurement.End with { X = measurement.Start.X };
                var a = Viewport.ToViewport(measurement.Start); var b = Viewport.ToViewport(end);
                using var paint = new SKPaint { Color = SKColors.OrangeRed, StrokeWidth = 2, IsAntialias = true };
                canvas.DrawLine((float)a.X, (float)a.Y, (float)b.X, (float)b.Y, paint);
                foreach (var point in new[] { a, b })
                    if (measurement.Horizontal) canvas.DrawLine((float)point.X, (float)point.Y - 6, (float)point.X, (float)point.Y + 6, paint);
                    else canvas.DrawLine((float)point.X - 6, (float)point.Y, (float)point.X + 6, (float)point.Y, paint);
            }
            if (Magnify && Pointer is { } pointer && pointer.Page <= Pages.Count) DrawMagnifier(canvas, pointer);
        }
        finally { canvas.Restore(); }
    }

    private void DrawPage(SKCanvas canvas, int index, Rect bounds)
    {
        canvas.Save();
        canvas.Translate((float)bounds.X, (float)bounds.Y);
        canvas.Scale((float)Viewport.Scale);
        using var white = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(0, 0, Pages[index].Width, Pages[index].Height, white);
        canvas.DrawPicture(Pages[index].Picture);
        canvas.Restore();
    }

    private void DrawMagnifier(SKCanvas canvas, PagePoint pointer)
    {
        const float radius = 65;
        var position = Viewport.ToViewport(pointer);
        var cx = (float)Math.Clamp(position.X + 90, radius + 4, Math.Max(radius + 4, Bounds.Width - radius - 4));
        var cy = (float)Math.Clamp(position.Y - 90, radius + 4, Math.Max(radius + 4, Bounds.Height - radius - 4));
        using var circle = new SKPath(); circle.AddCircle(cx, cy, radius);
        canvas.Save(); canvas.ClipPath(circle, antialias: true);
        canvas.DrawColor(SKColors.White);
        canvas.Translate(cx, cy); canvas.Scale((float)Viewport.Scale * 3);
        canvas.Translate(-(float)pointer.X, -(float)pointer.Y);
        if (Pages[pointer.Page - 1].Picture.Handle != IntPtr.Zero) canvas.DrawPicture(Pages[pointer.Page - 1].Picture);
        canvas.Restore();
        using var outline = new SKPaint { Color = SKColors.DeepSkyBlue, StrokeWidth = 2, Style = SKPaintStyle.Stroke, IsAntialias = true };
        canvas.DrawCircle(cx, cy, radius, outline);
        canvas.DrawLine(cx - 8, cy, cx + 8, cy, outline); canvas.DrawLine(cx, cy - 8, cx, cy + 8, outline);
    }

    public void Dispose()
    {
        foreach (var page in Interlocked.Exchange(ref retained, Array.Empty<PreviewPage>())) page.Release();
    }
    public bool Equals(ICustomDrawOperation? other) => false;
    public bool HitTest(Point p) => Bounds.Contains(p);
}
