using System.Collections.ObjectModel;
using System.Diagnostics;
using ReactiveUI;
using Unit = System.Reactive.Unit;

namespace FossPDF.Previewer;

internal sealed class InspectionTreeItem
{
    public InspectionNode Node { get; }
    public string Title => Node.Label ?? Node.Type;
    public ObservableCollection<InspectionTreeItem> Children { get; } = new();
    public InspectionTreeItem(InspectionNode node) => Node = node;
}

internal sealed class InspectionEntry
{
    public InspectionNode Node { get; }
    public InspectionOccurrence Occurrence { get; }
    public string Title => $"Page {Occurrence.Page}: {Node.Label ?? Occurrence.Text ?? Node.Type}";
    public InspectionEntry(InspectionNode node, InspectionOccurrence occurrence) { Node = node; Occurrence = occurrence; }
}

internal sealed record InspectionMeasurement(int Page, float X1, float Y1, float X2, float Y2);

internal sealed class InspectionViewModel : ReactiveObject, IDisposable
{
    private InspectionSnapshot? snapshot;
    private Dictionary<string, InspectionTreeItem> tree = new(StringComparer.Ordinal);
    private Dictionary<string, List<InspectionOccurrence>> occurrences = new(StringComparer.Ordinal);
    private InspectionTreeItem? selectedTreeItem;
    private InspectionEntry? selectedEntry;
    private string search = string.Empty;
    private string details = string.Empty;
    private string pointer = "Choose Inspect to pick page coordinates.";
    private string status = string.Empty;
    private int interactionMode;
    private double zoomPercent = 100;
    private InspectionMeasurement? measurement;
    private (int Page, float X, float Y)? anchor;
    private int pageCount;

    public ObservableCollection<InspectionTreeItem> Roots { get; } = new();
    public ObservableCollection<InspectionEntry> Matches { get; } = new();
    public IReadOnlyList<string> Modes { get; } = new[] { "Pan", "Inspect", "Measure horizontal", "Measure vertical" };
    public bool Available => snapshot != null;
    public string Diagnostic => snapshot?.Diagnostic ?? string.Empty;
    public bool HasDiagnostic => !string.IsNullOrEmpty(Diagnostic);
    public InspectionOccurrence? Highlight => selectedEntry?.Occurrence;
    public InspectionMeasurement? Measurement { get => measurement; private set => this.RaiseAndSetIfChanged(ref measurement, value); }
    public string Details { get => details; private set => this.RaiseAndSetIfChanged(ref details, value); }
    public string Pointer { get => pointer; private set => this.RaiseAndSetIfChanged(ref pointer, value); }
    public string Status { get => status; private set => this.RaiseAndSetIfChanged(ref status, value); }
    public string Search { get => search; set { this.RaiseAndSetIfChanged(ref search, value ?? string.Empty); RefreshSearch(); } }
    public int InteractionMode { get => interactionMode; set { this.RaiseAndSetIfChanged(ref interactionMode, value); anchor = null; } }
    public string ZoomLabel => $"Zoom: {zoomPercent:F0}%";
    public double ZoomPercent { get => zoomPercent; set { if (double.IsFinite(value)) { this.RaiseAndSetIfChanged(ref zoomPercent, Math.Clamp(value, 10, 1000)); this.RaisePropertyChanged(nameof(ZoomLabel)); } } }
    public ReactiveCommand<Unit, Unit> OpenSourceCommand { get; }

    public InspectionTreeItem? SelectedTreeItem
    {
        get => selectedTreeItem;
        set
        {
            if (ReferenceEquals(selectedTreeItem, value)) return;
            this.RaiseAndSetIfChanged(ref selectedTreeItem, value);
            if (value != null) SelectNode(value);
        }
    }

    public InspectionEntry? SelectedEntry
    {
        get => selectedEntry;
        set
        {
            this.RaiseAndSetIfChanged(ref selectedEntry, value);
            this.RaisePropertyChanged(nameof(Highlight));
            if (value == null) { Details = string.Empty; return; }
            this.RaiseAndSetIfChanged(ref selectedTreeItem, tree[value.Node.Id], nameof(SelectedTreeItem));
            var location = FindSource(value.Node);
            var item = value.Occurrence;
            Details = $"{value.Node.Type} | {value.Node.Id}\nPage {item.Page}: x={item.X:F2}, y={item.Y:F2}\nWidth {item.Width:F2} pt; height {item.Height:F2} pt\n" +
                (value.Node.Configuration ?? location?.Configuration ?? string.Empty) + "\n" + (item.Text ?? value.Node.Label ?? string.Empty) + "\n" +
                (location == null ? "Source not annotated. Add Inspect at the caller." : $"{location.SourceFile}:{location.SourceLine} ({location.SourceMember})");
        }
    }

    public InspectionViewModel() => OpenSourceCommand = ReactiveCommand.Create(OpenSource);

    public void Load(InspectionSnapshot? value, int pages)
    {
        value?.Validate(pages);
        snapshot = value; pageCount = pages; anchor = null; Measurement = null;
        SelectedEntry = null; SelectedTreeItem = null; Roots.Clear(); Matches.Clear();
        tree = new(StringComparer.Ordinal); occurrences = new(StringComparer.Ordinal);
        if (value != null)
        {
            foreach (var node in value.Nodes)
            {
                var item = new InspectionTreeItem(node); tree.Add(node.Id, item);
                if (node.ParentId == null) Roots.Add(item);
                else tree[node.ParentId].Children.Add(item);
            }
            foreach (var occurrence in value.Occurrences)
            {
                if (!occurrences.TryGetValue(occurrence.NodeId, out var items))
                    occurrences.Add(occurrence.NodeId, items = new());
                items.Add(occurrence);
            }
        }
        this.RaisePropertyChanged(nameof(Available)); this.RaisePropertyChanged(nameof(Diagnostic)); this.RaisePropertyChanged(nameof(HasDiagnostic));
        RefreshSearch();
    }

    private void SelectNode(InspectionTreeItem item)
    {
        var pending = new Stack<InspectionTreeItem>(); pending.Push(item);
        while (pending.Count > 0)
        {
            var candidate = pending.Pop();
            if (occurrences.TryGetValue(candidate.Node.Id, out var values) && values.Count > 0)
            {
                Choose(candidate.Node, values[0]);
                return;
            }
            foreach (var child in candidate.Children.Reverse()) pending.Push(child);
        }
        SelectedEntry = null;
        Details = $"{item.Node.Type} | {item.Node.Id}\nThis element has no rendered occurrence in the current snapshot.";
    }

    private void RefreshSearch()
    {
        Matches.Clear();
        if (snapshot == null) { Status = "Inspection is disabled for this preview."; return; }
        var term = search.Trim();
        foreach (var occurrence in snapshot.Occurrences)
        {
            var node = tree[occurrence.NodeId].Node;
            if (term.Length == 0 ? !string.IsNullOrEmpty(occurrence.Text) :
                (occurrence.Text?.Contains(term, StringComparison.OrdinalIgnoreCase) == true || node.Label?.Contains(term, StringComparison.OrdinalIgnoreCase) == true || node.Id.Contains(term, StringComparison.OrdinalIgnoreCase)))
                Matches.Add(new InspectionEntry(node, occurrence));
            if (Matches.Count >= 500) break;
        }
        Status = $"{snapshot.Nodes.Count} nodes; {snapshot.Occurrences.Count} occurrences; {Matches.Count} results (maximum 500).";
    }

    public void SetPointer(int page, float x, float y) => Pointer = $"Page {page}: x {x:F2} pt; y {y:F2} pt";

    public void Pick(int page, float x, float y)
    {
        if (snapshot == null || page < 1 || page > pageCount || !float.IsFinite(x) || !float.IsFinite(y)) return;
        SetPointer(page, x, y);
        if (InteractionMode is 2 or 3)
        {
            if (anchor == null || anchor.Value.Page != page)
            {
                anchor = (page, x, y); Status = "Choose the second point on this page."; return;
            }
            var start = anchor.Value;
            Measurement = InteractionMode == 2 ? new(page, start.X, start.Y, x, start.Y) : new(page, start.X, start.Y, start.X, y);
            var length = InteractionMode == 2 ? Math.Abs(x - start.X) : Math.Abs(y - start.Y);
            Status = $"{(InteractionMode == 2 ? "Width" : "Height")}: {length:F2} pt ({length * 25.4 / 72:F2} mm).";
            anchor = null; return;
        }
        var hit = snapshot.Occurrences.Where(item => item.Page == page && item.Width > 0 && item.Height > 0 && x >= item.X && y >= item.Y && x <= item.X + item.Width && y <= item.Y + item.Height)
            .OrderBy(item => item.Width * item.Height).FirstOrDefault();
        if (hit != null) Choose(tree[hit.NodeId].Node, hit);
        else SelectedEntry = null;
    }

    private void Choose(InspectionNode node, InspectionOccurrence occurrence)
    {
        var entry = Matches.FirstOrDefault(item => ReferenceEquals(item.Occurrence, occurrence));
        if (entry == null)
        {
            entry = new InspectionEntry(node, occurrence); Matches.Insert(0, entry);
            if (Matches.Count > 500) Matches.RemoveAt(Matches.Count - 1);
        }
        SelectedEntry = entry;
    }

    private InspectionNode? FindSource(InspectionNode node)
    {
        while (true)
        {
            if (!string.IsNullOrEmpty(node.SourceFile)) return node;
            if (node.ParentId == null || !tree.TryGetValue(node.ParentId, out var parent)) return null;
            node = parent.Node;
        }
    }

    internal static string SourceUri(InspectionNode node)
    {
        var path = node.SourceFile!.Replace('\\', '/');
        return "vscode://file" + (path.StartsWith("/", StringComparison.Ordinal) ? string.Empty : "/") +
            Uri.EscapeDataString(path).Replace("%2F", "/") + ":" + Math.Max(1, node.SourceLine);
    }

    private void OpenSource()
    {
        var node = selectedEntry != null ? FindSource(selectedEntry.Node) : selectedTreeItem == null ? null : FindSource(selectedTreeItem.Node);
        if (node == null) { Status = "Source is not annotated. Add Inspect at the caller."; return; }
        if (node.SourceFile!.StartsWith("\\\\", StringComparison.Ordinal) || node.SourceFile.StartsWith("//", StringComparison.Ordinal) ||
            !Path.IsPathFullyQualified(node.SourceFile) || !File.Exists(node.SourceFile))
        { Status = "Source is not present locally. Copy its location from the details panel."; return; }
        try { using var process = Process.Start(new ProcessStartInfo(SourceUri(node)) { UseShellExecute = true }); }
        catch (Exception) { Status = "VS Code could not be opened. Copy the source location from the details panel."; }
    }

    public void Dispose() { OpenSourceCommand.Dispose(); Load(null, 0); }
}
