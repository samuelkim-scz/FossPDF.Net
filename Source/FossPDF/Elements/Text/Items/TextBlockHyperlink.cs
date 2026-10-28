using FossPDF.Elements.Text.Calculation;
using FossPDF.Infrastructure;
using FossPDF.Drawing;

namespace FossPDF.Elements.Text.Items
{
    internal class TextBlockHyperlink : TextBlockSpan
    {
        private SemanticNode? _semanticNode;
        public string Url { get; set; } = string.Empty;
        
        public override TextMeasurementResult? Measure(TextMeasurementRequest request)
        {
            return MeasureWithoutCache(request);
        }

        public override void Draw(TextDrawingRequest request)
        {
            using var semantic = new AutomaticSemanticScope(request.Canvas, ref _semanticNode, "Link");
            request.Canvas.Translate(new Position(0, request.TotalAscent));
            request.Canvas.DrawHyperlink(Url, new Size(request.TextSize.Width, request.TextSize.Height));
            request.Canvas.Translate(new Position(0, -request.TotalAscent));
            
            base.Draw(request);
        }
    }
}
