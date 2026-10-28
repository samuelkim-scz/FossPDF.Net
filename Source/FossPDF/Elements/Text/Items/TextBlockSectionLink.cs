using FossPDF.Elements.Text.Calculation;
using FossPDF.Infrastructure;
using FossPDF.Drawing;

namespace FossPDF.Elements.Text.Items
{
    internal class TextBlockSectionLink : TextBlockSpan
    {
        private SemanticNode? _semanticNode;
        public string SectionName { get; set; } = string.Empty;
        
        public override TextMeasurementResult? Measure(TextMeasurementRequest request)
        {
            return MeasureWithoutCache(request);
        }

        public override void Draw(TextDrawingRequest request)
        {
            using var semantic = new AutomaticSemanticScope(request.Canvas, ref _semanticNode, "Link");
            request.Canvas.Translate(new Position(0, request.TotalAscent));
            request.Canvas.DrawSectionLink(
                request.PageContext.GetDocumentLocationName(SectionName),
                new Size(request.TextSize.Width, request.TextSize.Height));
            request.Canvas.Translate(new Position(0, -request.TotalAscent));
            
            base.Draw(request);
        }
    }
}
