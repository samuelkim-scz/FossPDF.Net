using FossPDF.Drawing;
using FossPDF.Infrastructure;

namespace FossPDF.Elements.Text.Calculation
{
    internal class TextMeasurementRequest
    {
        // TextBlock supplies these dependencies when creating a measurement request.
        public ICanvas Canvas { get; set; } = null!;
        public IPageContext PageContext { get; set; } = null!;

        public DocumentSpecificFontManager? FontManager { get; set; }

        public int StartIndex { get; set; }
        public float AvailableWidth { get; set; }

        public bool IsFirstElementInBlock { get; set; }
        public bool IsFirstElementInLine { get; set; }
    }
}
