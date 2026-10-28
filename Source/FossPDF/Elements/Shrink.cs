using FossPDF.Drawing;
using FossPDF.Infrastructure;

namespace FossPDF.Elements
{
    /// <summary>
    /// Draws its content in the most compact size achievable along the selected axes. This is
    /// useful when the parent provides more space than the content needs, for example when a
    /// height-balanced multi-column layout must not claim the full page height.
    /// </summary>
    internal sealed class Shrink : ContainerElement, IContentDirectionAware
    {
        public bool Vertical { get; set; }
        public bool Horizontal { get; set; }

        public ContentDirection ContentDirection { get; set; }

        internal override void Draw(Size availableSpace)
        {
            var measurement = base.Measure(availableSpace);

            if (measurement.Type == SpacePlanType.Wrap)
                return;

            var targetSize = new Size(
                Horizontal ? measurement.Width : availableSpace.Width,
                Vertical ? measurement.Height : availableSpace.Height);

            var offset = ContentDirection == ContentDirection.RightToLeft
                ? new Position(availableSpace.Width - targetSize.Width, 0)
                : Position.Zero;

            Canvas.Translate(offset);
            base.Draw(targetSize);
            Canvas.Translate(offset.Reverse());
        }
    }
}
