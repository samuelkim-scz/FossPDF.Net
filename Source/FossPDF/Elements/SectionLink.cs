using FossPDF.Drawing;
using FossPDF.Infrastructure;

namespace FossPDF.Elements
{
    internal class SectionLink : ContainerElement
    {
        private SemanticNode? _semanticNode;
        public string SectionName { get; set; } = string.Empty;
        
        internal override void Draw(Size availableSpace)
        {
            var targetSize = base.Measure(availableSpace);

            if (targetSize.Type == SpacePlanType.Wrap)
                return;

            using var semantic = new AutomaticSemanticScope(Canvas, ref _semanticNode, "Link");
            Canvas.DrawSectionLink(SectionName, targetSize);
            base.Draw(availableSpace);
        }
    }
}
