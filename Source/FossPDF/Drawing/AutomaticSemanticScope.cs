using System;
using FossPDF.Elements;
using FossPDF.Infrastructure;

namespace FossPDF.Drawing
{
    internal readonly struct AutomaticSemanticScope : IDisposable
    {
        private readonly ISemanticCanvas? _canvas;

        internal AutomaticSemanticScope(ICanvas canvas, ref SemanticNode? node, string role, string? alternativeText = null)
        {
            _canvas = null;
            if (GetCanvas(canvas) is not { } semantic)
                return;
            node ??= new SemanticNode(role, alternativeText, null);
            if (semantic.BeginAutomaticScope(node))
                _canvas = semantic;
        }

        public void Dispose() => _canvas?.EndSemanticScope();

        internal static ICanvas Unwrap(ICanvas canvas)
        {
            while (canvas is ProxyCanvas proxy && proxy.Target != null)
                canvas = proxy.Target;
            return canvas;
        }

        internal static ISemanticCanvas? GetCanvas(ICanvas canvas) => Unwrap(canvas) as ISemanticCanvas;

        internal static SemanticNode? GetExplicitCellNode(Element? element)
        {
            while (element is ContainerElement container)
            {
                if (element is SemanticElement semantic && semantic.Node.Role is "TH" or "TD")
                    return semantic.Node;
                element = container.Child;
            }
            return null;
        }
    }
}
