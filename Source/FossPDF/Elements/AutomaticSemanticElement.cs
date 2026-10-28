using FossPDF.Drawing;
using FossPDF.Infrastructure;

namespace FossPDF.Elements
{
    internal sealed class AutomaticSemanticElement : ContainerElement
    {
        private readonly string _role;
        private SemanticNode? _node;

        internal AutomaticSemanticElement(string role) => _role = role;

        internal override void Draw(Size availableSpace)
        {
            using var scope = new AutomaticSemanticScope(Canvas, ref _node, _role);
            base.Draw(availableSpace);
        }
    }

    internal sealed class DefaultArtifactElement : ContainerElement
    {
        internal override void Draw(Size availableSpace)
        {
            if (AutomaticSemanticScope.GetCanvas(Canvas) is not { } semantic)
            {
                base.Draw(availableSpace);
                return;
            }
            semantic.BeginDefaultArtifactScope();
            try { base.Draw(availableSpace); }
            finally { semantic.EndArtifactScope(); }
        }
    }
}
