using FossPDF.Drawing;
using FossPDF.Infrastructure;

namespace FossPDF.Elements.Text.Calculation
{
    internal class TextDrawingRequest
    {
        // TextBlock supplies these dependencies when creating a drawing request.
        public ICanvas Canvas { get; set; } = null!;
        public IPageContext PageContext { get; set; } = null!;
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public float TotalAscent { get; set; }
        public Size TextSize { get; set; }
    }
}
