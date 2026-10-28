using System;

namespace FossPDF.Infrastructure
{
    /// <summary>
    /// Describes a box shadow: an offset, an optional spread, an optional blur and a color. Values
    /// are expressed in points. The shadow follows the container's corner radii and is painted
    /// behind its background, border and content.
    /// </summary>
    public class BoxShadowStyle
    {
        /// <summary>Horizontal offset in points; positive values move the shadow to the right.</summary>
        public float OffsetX { get; set; }

        /// <summary>Vertical offset in points; positive values move the shadow downward.</summary>
        public float OffsetY { get; set; }

        /// <summary>
        /// Spread in points. Positive values expand the shadow shape, negative values contract it.
        /// </summary>
        public float Spread { get; set; }

        /// <summary>
        /// Blur radius in points. Zero produces a sharp shadow; higher values diffuse the edge.
        /// </summary>
        public float Blur { get; set; }

        /// <summary>Shadow color, in any validated color format.</summary>
        public string Color { get; set; } = Helpers.Colors.Black;

        internal void Validate()
        {
            if (!float.IsFinite(OffsetX))
                throw new ArgumentOutOfRangeException(nameof(OffsetX), OffsetX, "The shadow offset must be finite.");

            if (!float.IsFinite(OffsetY))
                throw new ArgumentOutOfRangeException(nameof(OffsetY), OffsetY, "The shadow offset must be finite.");

            if (!float.IsFinite(Spread))
                throw new ArgumentOutOfRangeException(nameof(Spread), Spread, "The shadow spread must be finite.");

            if (!float.IsFinite(Blur) || Blur < 0)
                throw new ArgumentOutOfRangeException(nameof(Blur), Blur, "The shadow blur must be finite and non-negative.");

            Helpers.ColorValidator.Validate(Color);
        }
    }
}
