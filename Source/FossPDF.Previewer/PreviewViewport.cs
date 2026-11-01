using Avalonia;

namespace FossPDF.Previewer;

internal readonly record struct PagePoint(int Page, double X, double Y);
internal readonly record struct PreviewPageSize(double Width, double Height);

/// <summary>Shared page-to-viewport transform for drawing, picking, focus and measurement.</summary>
internal sealed class PreviewViewport
{
    internal const double PageSpacing = 25;
    internal const double SafeZone = 25;
    private PreviewPageSize[] pages = Array.Empty<PreviewPageSize>();
    private double[] tops = Array.Empty<double>();
    internal double Width { get; set; }
    internal double Height { get; set; }
    internal double Scale { get; private set; } = 1;
    internal double TranslateX { get; private set; }
    internal double TranslateY { get; private set; }
    internal double TotalPageHeight { get; private set; }
    internal double MaxPageWidth { get; private set; }
    internal PreviewViewport Snapshot() => (PreviewViewport)MemberwiseClone();
    private double TotalHeight => TotalPageHeight + 2 * SafeZone / Scale;
    private double MaxTranslate => Math.Max(0, TotalHeight - Height / Scale);
    internal double Scroll
    {
        get => MaxTranslate > 0 ? Math.Clamp(TranslateY / MaxTranslate, 0, 1) : 0;
        set { TranslateY = (double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0) * MaxTranslate; Clamp(); }
    }
    internal double ScrollViewport => pages.Length == 0 || TotalHeight <= 0 ? 1 : Math.Clamp(Height / Scale / TotalHeight, 0, 1);

    internal void SetPages(IEnumerable<PreviewPageSize> sizes)
    {
        pages = sizes.ToArray();
        tops = new double[pages.Length];
        var top = 0d;
        for (var index = 0; index < pages.Length; index++)
        {
            tops[index] = top;
            top += pages[index].Height + PageSpacing;
        }
        TotalPageHeight = pages.Length == 0 ? 0 : top - PageSpacing;
        MaxPageWidth = pages.Length == 0 ? 0 : pages.Max(page => page.Width);
        Clamp();
    }

    internal void Clamp()
    {
        if (pages.Length == 0) { TranslateX = TranslateY = 0; return; }
        if (TotalHeight * Scale > Height)
            TranslateY = Math.Clamp(TranslateY, 0, MaxTranslate);
        else
            TranslateY = (TotalPageHeight - Height / Scale) / 2 + SafeZone / Scale;
        var horizontalLimit = Math.Max(0, (MaxPageWidth - (Width - 2 * SafeZone) / Scale) / 2);
        TranslateX = Math.Clamp(TranslateX, -horizontalLimit, horizontalLimit);
    }

    internal Point ToViewport(PagePoint point)
    {
        var index = point.Page - 1;
        return new Point(Width / 2 + Scale * (TranslateX - pages[index].Width / 2 + point.X),
            SafeZone + Scale * (tops[index] - TranslateY + point.Y));
    }

    internal Rect PageBounds(int page)
    {
        var size = pages[page - 1];
        return new Rect(ToViewport(new PagePoint(page, 0, 0)), new Size(size.Width * Scale, size.Height * Scale));
    }

    internal bool TryPick(Point point, out PagePoint result)
    {
        result = default;
        if (pages.Length == 0 || !double.IsFinite(point.X) || !double.IsFinite(point.Y)) return false;
        var documentY = (point.Y - SafeZone) / Scale + TranslateY;
        var index = Array.BinarySearch(tops, documentY);
        if (index < 0) index = ~index - 1;
        if (index < 0 || index >= pages.Length) return false;
        var x = (point.X - Width / 2) / Scale - TranslateX + pages[index].Width / 2;
        var y = documentY - tops[index];
        if (x < 0 || y < 0 || x > pages[index].Width || y > pages[index].Height) return false;
        result = new PagePoint(index + 1, x, y);
        return true;
    }

    internal void Pan(double x, double y)
    { TranslateX += x / Scale; TranslateY -= y / Scale; Clamp(); }

    internal void Zoom(Point anchor, double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0) return;
        var previous = Scale;
        Scale = Math.Clamp(Scale * factor, 0.1, 10);
        TranslateX += (anchor.X - Width / 2) * (1 / Scale - 1 / previous);
        TranslateY += (anchor.Y - SafeZone) * (1 / previous - 1 / Scale);
        Clamp();
    }

    internal void Focus(InspectionOccurrence occurrence)
    {
        if (occurrence.Page < 1 || occurrence.Page > pages.Length) return;
        TranslateX = pages[occurrence.Page - 1].Width / 2 - occurrence.X - occurrence.Width / 2;
        TranslateY = tops[occurrence.Page - 1] + occurrence.Y + occurrence.Height / 2 - (Height / 2 - SafeZone) / Scale;
        Clamp();
    }
}
