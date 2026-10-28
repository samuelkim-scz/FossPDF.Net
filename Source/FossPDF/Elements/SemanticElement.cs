using FossPDF.Infrastructure;

namespace FossPDF.Elements
{
    /// <summary>
    /// Wraps the next element in a logical structure node. The wrapper is layout-transparent; it only
    /// opens a semantic scope on canvases that support tagging. Other outputs draw the child directly.
    /// </summary>
    internal sealed class SemanticElement : ContainerElement
    {
        internal SemanticElement(string role, string? alternativeText = null, string? language = null, string? tableHeaderScope = null)
        {
            Node = new Drawing.SemanticNode(role, alternativeText, language) { TableHeaderScope = tableHeaderScope };
        }

        internal Drawing.SemanticNode Node { get; }

        internal override void Draw(Size availableSpace)
        {
            if (Child == null)
                return;

            if (Drawing.AutomaticSemanticScope.GetCanvas(Canvas) is { } semanticCanvas)
            {
                semanticCanvas.BeginSemanticScope(Node);

                try
                {
                    Child.Draw(availableSpace);
                }
                finally
                {
                    semanticCanvas.EndSemanticScope();
                }

                return;
            }

            Child.Draw(availableSpace);
        }
    }

    /// <summary>
    /// Marks the next element and all of its descendants as decorative. Nested semantic wrappers are
    /// ignored, so ignored content never enters the structure tree.
    /// </summary>
    internal sealed class SemanticIgnoreElement : ContainerElement
    {
        internal override void Draw(Size availableSpace)
        {
            if (Child == null)
                return;

            if (Drawing.AutomaticSemanticScope.GetCanvas(Canvas) is { } semanticCanvas)
            {
                semanticCanvas.BeginArtifactScope();

                try
                {
                    Child.Draw(availableSpace);
                }
                finally
                {
                    semanticCanvas.EndArtifactScope();
                }

                return;
            }

            Child.Draw(availableSpace);
        }
    }
}
