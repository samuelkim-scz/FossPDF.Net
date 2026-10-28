namespace FossPDF.Drawing
{
    /// <summary>
    /// Optional capability of a rendering canvas. Canvases that do not implement it simply draw the
    /// semantic wrapper's child, so the fluent API is transparent for images, SVG, XPS and previews.
    /// </summary>
    internal interface ISemanticCanvas
    {
        void BeginSemanticScope(SemanticNode node);
        void EndSemanticScope();
        bool BeginAutomaticScope(SemanticNode node);
        void BeginDefaultArtifactScope();

        /// <summary>Starts an artifact scope that also suppresses nested semantic wrappers.</summary>
        void BeginArtifactScope();
        void EndArtifactScope();
    }
}
