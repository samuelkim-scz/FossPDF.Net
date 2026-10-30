namespace FossPDF.Previewer;

/// <summary>A refresh atomically replaces both pictures and inspection data.</summary>
internal sealed class PreviewUpdate : IDisposable
{
    public IReadOnlyList<PreviewPage> Pages { get; }
    public InspectionSnapshot? Inspection { get; }
    private bool Disposed;
    public PreviewUpdate(IReadOnlyList<PreviewPage> pages, InspectionSnapshot? inspection)
    { Pages = pages; Inspection = inspection; }
    public void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        foreach (var page in Pages) page.Picture.Dispose();
    }
}
