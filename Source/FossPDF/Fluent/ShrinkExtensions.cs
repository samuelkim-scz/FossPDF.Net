using FossPDF.Elements;
using FossPDF.Infrastructure;

namespace FossPDF.Fluent
{
    public static class ShrinkExtensions
    {
        private static IContainer Shrink(this IContainer element, bool? vertical = null, bool? horizontal = null)
        {
            var shrink = element as Shrink ?? new Shrink();

            if (vertical.HasValue)
                shrink.Vertical = vertical.Value;

            if (horizontal.HasValue)
                shrink.Horizontal = horizontal.Value;

            return element.Element(shrink);
        }

        /// <summary>
        /// Renders its content in the most compact size achievable along both axes.
        /// </summary>
        public static IContainer Shrink(this IContainer element)
        {
            return element.Shrink(true, true);
        }

        /// <summary>
        /// Renders its content using only the height it needs instead of the full available height.
        /// </summary>
        public static IContainer ShrinkVertical(this IContainer element)
        {
            return element.Shrink(true);
        }

        /// <summary>
        /// Renders its content using only the width it needs instead of the full available width.
        /// </summary>
        public static IContainer ShrinkHorizontal(this IContainer element)
        {
            return element.Shrink(null, true);
        }
    }
}
