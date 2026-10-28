using FossPDF.Elements.Text.Items;

namespace FossPDF.Elements.Text.Calculation
{
    internal class TextLineElement
    {
        // Assigned together when TextBlock creates a measured line element.
        public ITextBlockItem Item { get; set; } = null!;
        public TextMeasurementResult Measurement { get; set; } = null!;

        // Top of an injected element relative to the text baseline; unused for text spans.
        public float InjectedElementTop { get; set; }
    }
}
