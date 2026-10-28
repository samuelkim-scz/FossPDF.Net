namespace FossPDF.Infrastructure
{
    /// <summary>
    /// Captures and restores the layout progress of an element. Container elements such as
    /// <see cref="FossPDF.Elements.MultiColumn"/> need speculative layout: they draw content to
    /// discover how it splits across columns, then rewind before the real drawing pass. Without a
    /// snapshot, speculative drawing would consume dynamic content or duplicate output.
    /// <para>
    /// The snapshot describes only rendering progress (queues, indexes and rendered flags), never
    /// cached glyph or shape data, which is a pure function of the request and may be reused.
    /// </para>
    /// </summary>
    internal interface IStateSnapshot
    {
        object? CaptureState();
        void RestoreState(object? state);
    }
}
