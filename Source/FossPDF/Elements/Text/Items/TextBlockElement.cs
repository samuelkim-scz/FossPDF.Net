using FossPDF.Drawing;
using FossPDF.Elements.Text.Calculation;
using FossPDF.Helpers;
using FossPDF.Infrastructure;

namespace FossPDF.Elements.Text.Items
{
    internal class TextBlockElement : ITextBlockItem
    {
        public Element Element { get; set; } = Empty.Instance;
        public TextInjectedElementAlignment Alignment { get; set; } = TextInjectedElementAlignment.AboveBaseline;

        public TextMeasurementResult? Measure(TextMeasurementRequest request)
        {
            Element.VisitChildren(x => (x as IStateResettable)?.ResetState());
            Element.InjectDependencies(request.PageContext, request.Canvas);

            var measurement = Element.Measure(new Size(request.AvailableWidth, Size.Max.Height));

            if (measurement.Type != SpacePlanType.FullRender)
                return null;
            
            return new TextMeasurementResult
            {
                Width = measurement.Width,
                
                Ascent = -measurement.Height,
                Descent = 0,
                
                LineHeight = 1,
                
                StartIndex = 0,
                EndIndex = 0,
                TotalIndex = 0,

                InjectedElementAlignment = Alignment
            };
        }

        public void Draw(TextDrawingRequest request)
        {
            Element.VisitChildren(x => (x as IStateResettable)?.ResetState());
            Element.InjectDependencies(request.PageContext, request.Canvas);

            var offset = request.InjectedElementTop;
            request.Canvas.Translate(new Position(0, offset));
            Element.Draw(request.TextSize);
            request.Canvas.Translate(new Position(0, -offset));
        }
    }
}
