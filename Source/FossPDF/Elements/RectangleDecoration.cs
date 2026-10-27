using FossPDF.Drawing;
using FossPDF.Helpers;
using FossPDF.Infrastructure;

namespace FossPDF.Elements
{
    /// <summary>
    /// Shared implementation for background, border and clip containers. All three fluent APIs
    /// mutate one decoration so its fill, border and corner radii always agree, independent of the
    /// order in which they are chained.
    /// </summary>
    internal abstract class RectangleDecoration : ContainerElement
    {
        public string? FillColor { get; set; }
        public LinearGradient? FillGradient { get; set; }

        public string? BorderColor { get; set; } = Colors.Black;
        public LinearGradient? BorderGradient { get; set; }

        public float Top { get; set; }
        public float Right { get; set; }
        public float Bottom { get; set; }
        public float Left { get; set; }

        public BorderRadius CornerRadius { get; set; }
        public BorderAlignment BorderAlignment { get; set; } = BorderAlignment.Center;
        public BoxShadowStyle? Shadow { get; set; }

        public bool ClipContent { get; set; }

        internal override void Draw(Size availableSpace)
        {
            if (Shadow != null)
                Canvas.DrawShadow(Position.Zero, availableSpace, CornerRadius, Shadow);

            DrawFill(availableSpace);

            if (ClipContent)
            {
                Canvas.SaveState();
                Canvas.ClipRectangle(Position.Zero, availableSpace, CornerRadius);
                base.Draw(availableSpace);
                Canvas.RestoreState();
            }
            else
            {
                base.Draw(availableSpace);
            }

            DrawBorder(availableSpace);
        }

        private void DrawFill(Size availableSpace)
        {
            if (FillGradient == null && FillColor == null)
                return;

            // Keep the legacy drawing operation for solid, square fills: it preserves the exact
            // output of documents that never opt into the new styling APIs.
            if (FillGradient == null && CornerRadius.IsZero)
            {
                Canvas.DrawRectangle(Position.Zero, availableSpace, FillColor!);
                return;
            }

            Canvas.DrawRectangle(Position.Zero, availableSpace, new RectangleStyle
            {
                FillColor = FillGradient == null ? FillColor : null,
                FillGradient = FillGradient,
                CornerRadius = CornerRadius
            });
        }

        private void DrawBorder(Size availableSpace)
        {
            var hasBorder = Top > 0 || Right > 0 || Bottom > 0 || Left > 0;

            if (hasBorder == false)
                return;

            // Legacy solid per-side borders are painted as four rectangles centered on the layout
            // edges. Keep that exact sequence unless rounded corners, gradients or an explicit
            // alignment require the generalized path.
            if (CornerRadius.IsZero && BorderGradient == null && BorderAlignment == BorderAlignment.Center)
            {
                DrawLegacyBorder(availableSpace);
                return;
            }

            Canvas.DrawRectangle(Position.Zero, availableSpace, new RectangleStyle
            {
                BorderColor = BorderGradient == null ? BorderColor : null,
                BorderGradient = BorderGradient,
                Top = Top,
                Right = Right,
                Bottom = Bottom,
                Left = Left,
                CornerRadius = CornerRadius,
                BorderAlignment = BorderAlignment
            });
        }

        private void DrawLegacyBorder(Size availableSpace)
        {
            var color = BorderColor ?? Colors.Black;

            Canvas.DrawRectangle(
                new Position(-Left / 2, -Top / 2),
                new Size(availableSpace.Width + Left / 2 + Right / 2, Top),
                color);

            Canvas.DrawRectangle(
                new Position(-Left / 2, -Top / 2),
                new Size(Left, availableSpace.Height + Top / 2 + Bottom / 2),
                color);

            Canvas.DrawRectangle(
                new Position(-Left / 2, availableSpace.Height - Bottom / 2),
                new Size(availableSpace.Width + Left / 2 + Right / 2, Bottom),
                color);

            Canvas.DrawRectangle(
                new Position(availableSpace.Width - Right / 2, -Top / 2),
                new Size(Right, availableSpace.Height + Top / 2 + Bottom / 2),
                color);
        }
    }
}
