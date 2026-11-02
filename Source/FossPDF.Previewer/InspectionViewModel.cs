using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Threading;
using ReactiveUI;

namespace FossPDF.Previewer;

internal sealed class InspectionEntry
{
    internal InspectionNode Node { get; }
    internal List<InspectionEntry> Children { get; } = new();
    internal InspectionEntry? Parent { get; set; }
    internal int Depth { get; set; }
    internal List<InspectionOccurrence> Paints { get; } = new();
    internal bool Expanded { get; set; }
    public string Title => string.IsNullOrWhiteSpace(Node.Label) ? Node.Type : Node.Label;
    public string Description => $"{Node.Type}, {Paints.Count} occurrence(s)";
    public string ToggleGlyph => Children.Count == 0 ? "·" : Expanded ? "−" : "+";
    public string DisplayTitle => $"{ToggleGlyph} {Title}";
    public Thickness Indent { get; internal set; }
    internal InspectionEntry(InspectionNode node) => Node = node;
    public override string ToString() => Title;
}

internal sealed class OccurrenceEntry
{
    internal InspectionOccurrence Value { get; }
    internal OccurrenceEntry(InspectionOccurrence value) => Value = value;
    public override string ToString() => FormattableString.Invariant($"Page {Value.Page} • ({Value.X:0.##}, {Value.Y:0.##}) • {Value.Width:0.##} × {Value.Height:0.##} pt");
}

internal readonly record struct InspectionMeasurement(PagePoint Start, PagePoint End, bool Horizontal)
{
    internal double Points => Math.Abs(Horizontal ? End.X - Start.X : End.Y - Start.Y);
    public override string ToString() => FormattableString.Invariant($"{(Horizontal ? "Horizontal" : "Vertical")}: {Points:0.##} pt ({Points * 25.4 / 72:0.##} mm), page {Start.Page}");
}

internal sealed class InspectionViewModel : ReactiveObject, IDisposable
{
    public static IReadOnlyList<string> Tools { get; } = new[] { "Pan", "Pick element", "Measure horizontal", "Measure vertical" };
    private readonly Action<Action> dispatch;
    private Dictionary<string, InspectionEntry> index = new(StringComparer.Ordinal);
    private InspectionEntry[] ordered = Array.Empty<InspectionEntry>();
    private InspectionOccurrence[] paints = Array.Empty<InspectionOccurrence>();
    private CancellationTokenSource? searchCancellation;
    private long revision;
    private bool disposed;
    private bool suppressFocus;
    private int pageCount;
    internal Task SearchCompletion { get; private set; } = Task.CompletedTask;
    public ObservableCollection<InspectionEntry> Entries { get; } = new();
    public ObservableCollection<OccurrenceEntry> Occurrences { get; } = new();
    internal IReadOnlyList<InspectionOccurrence> Highlights => selected?.Paints ?? (IReadOnlyList<InspectionOccurrence>)Array.Empty<InspectionOccurrence>();
    public event Action<InspectionOccurrence>? FocusRequested;

    private string query = string.Empty;
    public string Query { get => query; set { this.RaiseAndSetIfChanged(ref query, value ?? string.Empty); QueueSearch(); } }
    private string searchStatus = "Enable inspection in the document client (--inspect in the sample) to inspect elements.";
    public string SearchStatus { get => searchStatus; private set => this.RaiseAndSetIfChanged(ref searchStatus, value); }
    private string diagnostic = string.Empty;
    public string Diagnostic { get => diagnostic; private set { this.RaiseAndSetIfChanged(ref diagnostic, value); this.RaisePropertyChanged(nameof(HasDiagnostic)); } }
    public bool HasDiagnostic => Diagnostic.Length > 0;
    private string status = "Pan: drag to move; Ctrl+wheel to zoom. Select a tool to inspect or measure.";
    public string Status { get => status; set => this.RaiseAndSetIfChanged(ref status, value); }
    private string pointer = "Point coordinates appear here.";
    public string Pointer { get => pointer; private set => this.RaiseAndSetIfChanged(ref pointer, value); }
    private string tool = Tools[0];
    public string Tool { get => tool; set { this.RaiseAndSetIfChanged(ref tool, Tools.Contains(value) ? value : Tools[0]); ResetMeasurement(); } }
    private bool magnify;
    public bool Magnify { get => magnify; set => this.RaiseAndSetIfChanged(ref magnify, value); }
    public string CoordinatePage { get; set; } = "1";
    public string CoordinateX { get; set; } = "0";
    public string CoordinateY { get; set; } = "0";
    private PagePoint? hovered;
    internal PagePoint? Hovered => hovered;
    private InspectionMeasurement? measurement;
    internal InspectionMeasurement? Measurement => measurement;
    private bool measuring;
    public string MeasurementText => measurement?.ToString() ?? "Choose a measurement tool, then pick two points on one page.";
    private InspectionEntry? selected;
    public InspectionEntry? Selected
    {
        get => selected;
        set
        {
            this.RaiseAndSetIfChanged(ref selected, value);
            Occurrences.Clear();
            if (value != null) foreach (var paint in value.Paints) Occurrences.Add(new OccurrenceEntry(paint));
            SelectedOccurrence = Occurrences.FirstOrDefault();
            this.RaisePropertyChanged(nameof(Details));
            this.RaisePropertyChanged(nameof(SourceLocation));
            this.RaisePropertyChanged(nameof(HasSource));
            this.RaisePropertyChanged(nameof(Highlights));
        }
    }
    private OccurrenceEntry? selectedOccurrence;
    public OccurrenceEntry? SelectedOccurrence
    {
        get => selectedOccurrence;
        set
        {
            this.RaiseAndSetIfChanged(ref selectedOccurrence, value);
            this.RaisePropertyChanged(nameof(OccurrenceDetails));
            if (!suppressFocus && value != null) FocusRequested?.Invoke(value.Value);
        }
    }
    public string Details => Selected == null ? "Select an element in the hierarchy, search results or page." :
        $"{Selected.Node.Type}\n{Selected.Node.Id}\nConfiguration: {Selected.Node.Configuration ?? "No annotation"}";
    public string OccurrenceDetails => SelectedOccurrence == null ? "This node has no painted occurrence." :
        SelectedOccurrence + (string.IsNullOrWhiteSpace(SelectedOccurrence.Value.Text) ? string.Empty : "\n" + SelectedOccurrence.Value.Text);
    internal InspectionNode? SourceNode
    {
        get
        {
            for (var entry = Selected; entry != null; entry = entry.Parent)
                if (!string.IsNullOrWhiteSpace(entry.Node.SourceFile)) return entry.Node;
            return null;
        }
    }
    public bool HasSource => SourceNode != null;
    public string CopyLocation => SourceNode is { } source ? $"{source.SourceFile}:{source.SourceLine}" : string.Empty;
    public string SourceLocation => SourceNode is { } source ? $"{source.SourceFile}:{source.SourceLine}\n{source.SourceMember}" : "No source annotation. Add Inspect at a component boundary.";

    internal InspectionViewModel(Action<Action>? dispatch = null) => this.dispatch = dispatch ?? (action => Dispatcher.UIThread.Post(action));

    internal void Refresh(InspectionSnapshot? snapshot, int pages)
    {
        if (disposed) return;
        CancelSearch();
        revision++;
        snapshot?.Validate(pages);
        Entries.Clear();
        pageCount = pages;
        index = new Dictionary<string, InspectionEntry>(StringComparer.Ordinal);
        var entries = new List<InspectionEntry>();
        foreach (var node in snapshot?.Nodes ?? Enumerable.Empty<InspectionNode>())
        {
            var entry = new InspectionEntry(node);
            if (node.ParentId != null)
            {
                entry.Parent = index[node.ParentId];
                entry.Parent.Children.Add(entry);
                entry.Depth = entry.Parent.Depth + 1;
                entry.Indent = new Thickness(Math.Min(160, entry.Parent.Indent.Left + 12), 0, 0, 0);
            }
            entry.Expanded = entry.Indent.Left < 24;
            index.Add(node.Id, entry);
            entries.Add(entry);
        }
        ordered = entries.ToArray();
        paints = snapshot?.Occurrences.ToArray() ?? Array.Empty<InspectionOccurrence>();
        foreach (var occurrence in paints) index[occurrence.NodeId].Paints.Add(occurrence);
        Diagnostic = snapshot?.Diagnostic ?? string.Empty;
        Selected = null;
        UpdatePointer(null);
        ResetMeasurement();
        Status = snapshot == null ? "Picture-only preview. Measurement is available; enable inspection for element details." :
            "Select a hierarchy row or choose Pick element. Ctrl+wheel zooms; middle-drag pans in every tool.";
        QueueSearch();
    }

    public void ToggleSelected()
    {
        if (Selected == null || Selected.Children.Count == 0 || Query.Length != 0) return;
        var keep = Selected;
        keep.Expanded = !keep.Expanded;
        ShowHierarchy();
        Selected = keep;
    }

    internal void SelectParent()
    {
        if (Selected?.Parent is { } parent) { Reveal(parent); Selected = parent; }
    }

    private void Reveal(InspectionEntry entry)
    {
        for (var parent = entry.Parent; parent != null; parent = parent.Parent) parent.Expanded = true;
        Query = string.Empty;
        ShowHierarchy();
    }

    private void ShowHierarchy()
    {
        Entries.Clear();
        var visible = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in ordered)
        {
            if (entry.Parent != null && (!visible.Contains(entry.Parent.Node.Id) || !entry.Parent.Expanded)) continue;
            visible.Add(entry.Node.Id);
            Entries.Add(entry);
        }
        SearchStatus = ordered.Length == 0 ? "Enable inspection in the document client (--inspect in the sample) to inspect elements." :
            $"{ordered.Length:N0} elements • {paints.Length:N0} occurrences • {pageCount:N0} pages";
    }

    private void QueueSearch()
    {
        if (disposed) return;
        CancelSearch();
        if (ordered.Length == 0 || string.IsNullOrWhiteSpace(Query)) { ShowHierarchy(); SearchCompletion = Task.CompletedTask; return; }
        var cancellation = searchCancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var currentRevision = revision;
        var term = Query.Trim();
        var source = ordered;
        SearchStatus = "Searching…";
        SearchCompletion = SearchAsync();
        async Task SearchAsync()
        {
            try
            {
                await Task.Delay(150, token).ConfigureAwait(false);
                var result = await Task.Run(() =>
                {
                    var matches = new List<InspectionEntry>();
                    foreach (var entry in source)
                    {
                        token.ThrowIfCancellationRequested();
                        if (Contains(entry.Node.Id) || Contains(entry.Node.Label) || Contains(entry.Node.Type) || Contains(entry.Node.Configuration) ||
                            Contains(entry.Node.SourceMember) || entry.Paints.Any(paint => Contains(paint.Text)))
                        {
                            matches.Add(entry);
                            if (matches.Count == 500) break;
                        }
                    }
                    return matches;
                    bool Contains(string? text) => text?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
                }, token).ConfigureAwait(false);
                dispatch(() =>
                {
                    if (disposed || token.IsCancellationRequested || revision != currentRevision) return;
                    Entries.Clear();
                    foreach (var entry in result) Entries.Add(entry);
                    SearchStatus = result.Count == 500 ? "Showing the first 500 matches. Refine the search for more." : $"{result.Count} matches";
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
    }

    internal void UpdatePointer(PagePoint? point)
    {
        hovered = point;
        Pointer = point is { } p ? FormattableString.Invariant($"Page {p.Page} • X {p.X:0.##}, Y {p.Y:0.##} pt") : "Pointer is outside a page.";
        if (measuring && measurement is { } current && point is { } next && next.Page == current.Start.Page)
        { measurement = current with { End = next }; this.RaisePropertyChanged(nameof(MeasurementText)); }
        this.RaisePropertyChanged(nameof(Hovered));
    }

    internal void Pick(PagePoint point)
    {
        if (point.Page < 1 || point.Page > pageCount || !double.IsFinite(point.X) || !double.IsFinite(point.Y) || point.X < 0 || point.Y < 0)
        { Status = "Enter a valid page and non-negative finite point coordinates."; return; }
        if (Tool.StartsWith("Measure", StringComparison.Ordinal))
        {
            var horizontal = Tool == Tools[2];
            if (!measuring || measurement?.Start.Page != point.Page)
            { measurement = new InspectionMeasurement(point, point, horizontal); measuring = true; Status = "Pick the second point on this page."; }
            else
            { measurement = measurement.Value with { End = point }; measuring = false; Status = measurement.Value.ToString(); }
            this.RaisePropertyChanged(nameof(MeasurementText));
            return;
        }
        InspectionOccurrence? occurrence = null;
        var depth = -1;
        foreach (var paint in paints)
        {
            if (paint.Page != point.Page || paint.Width <= 0 || paint.Height <= 0 ||
                point.X < paint.X || point.X > paint.X + paint.Width ||
                point.Y < paint.Y || point.Y > paint.Y + paint.Height)
                continue;
            var candidateDepth = index[paint.NodeId].Depth;
            // Page-wide wrappers are captured after content, even if their child is empty.
            // Prefer the deepest containing element, then later occurrences at equal depth.
            if (candidateDepth < depth) continue;
            occurrence = paint;
            depth = candidateDepth;
        }
        if (occurrence == null) { Status = "No inspected element at that point."; return; }
        suppressFocus = true;
        try
        {
            var entry = index[occurrence.NodeId];
            Reveal(entry);
            Selected = entry;
            SelectedOccurrence = Occurrences.First(entry => ReferenceEquals(entry.Value, occurrence));
        }
        finally { suppressFocus = false; }
        Status = $"Selected {Selected.Title}, page {point.Page}.";
    }

    public void UseCoordinates()
    {
        if (!int.TryParse(CoordinatePage, NumberStyles.Integer, CultureInfo.InvariantCulture, out var page) ||
            !double.TryParse(CoordinateX, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !double.TryParse(CoordinateY, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        { Status = "Enter page, X and Y as numbers (points, decimal separator '.')."; return; }
        var point = new PagePoint(page, x, y);
        Pick(point);
        if (page >= 1 && page <= pageCount && double.IsFinite(x) && double.IsFinite(y) && x >= 0 && y >= 0)
        {
            UpdatePointer(point);
            FocusRequested?.Invoke(new InspectionOccurrence { Page = page, X = (float)x, Y = (float)y });
        }
    }

    public void ResetMeasurement()
    {
        var hadMeasurement = measurement.HasValue;
        measurement = null;
        measuring = false;
        this.RaisePropertyChanged(nameof(MeasurementText));
        if (hadMeasurement) Status = "Measurement cleared.";
    }

    private void CancelSearch()
    { searchCancellation?.Cancel(); searchCancellation?.Dispose(); searchCancellation = null; }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        CancelSearch(); revision++;
        index.Clear(); ordered = Array.Empty<InspectionEntry>(); paints = Array.Empty<InspectionOccurrence>();
        Entries.Clear(); Occurrences.Clear(); selected = null; selectedOccurrence = null; hovered = null; measurement = null;
        FocusRequested = null;
    }
}
