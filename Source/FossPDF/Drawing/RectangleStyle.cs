using FossPDF.Infrastructure;

namespace FossPDF.Drawing
{
    /// <summary>
    /// Complete description of a decorated rectangle: an optional fill, an optional border and the
    /// corner radii shared by both. It is the single drawing contract for backgrounds, borders and
    /// rounded clipping, so new graphics features extend this type instead of adding canvas methods.
    /// </summary>
    internal struct RectangleStyle
    {
        public string? FillColor { get; set; }
        public LinearGradient? FillGradient { get; set; }

        public string? BorderColor { get; set; }
        public LinearGradient? BorderGradient { get; set; }

        public float Top { get; set; }
        public float Right { get; set; }
        public float Bottom { get; set; }
        public float Left { get; set; }

        public BorderRadius CornerRadius { get; set; }
        public BorderAlignment BorderAlignment { get; set; }

        public bool HasFill => FillGradient != null || FillColor != null;

        public bool HasBorder =>
            Top > 0 || Right > 0 || Bottom > 0 || Left > 0;
    }
}
