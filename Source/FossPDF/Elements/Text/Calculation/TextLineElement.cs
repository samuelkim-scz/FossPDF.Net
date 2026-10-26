using FossPDF.Elements.Text.Items;

namespace FossPDF.Elements.Text.Calculation
{
    internal class TextLineElement
    {
        // Assigned together when TextBlock creates a measured line element.
        public ITextBlockItem Item { get; set; } = null!;
        public TextMeasurementResult Measurement { get; set; } = null!;
    }
}
