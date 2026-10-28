using System;
using FossPDF.Elements;
using FossPDF.Helpers;
using FossPDF.Infrastructure;

namespace FossPDF.Fluent
{
    public static class BorderExtensions
    {
        private static IContainer Border(this IContainer element, Action<RectangleDecoration> handler)
        {
            // Reuse the existing decoration so that a background, a border and their shared corner
            // radii always describe the same rectangle regardless of the chain order.
            var border = element as RectangleDecoration ?? element.Element(new Border());
            handler(border);

            return border;
        }

        public static IContainer Border(this IContainer element, float value, Unit unit = Unit.Point)
        {
            return element
                .BorderHorizontal(value, unit)
                .BorderVertical(value, unit);
        }

        public static IContainer BorderVertical(this IContainer element, float value, Unit unit = Unit.Point)
        {
            return element
                .BorderLeft(value, unit)
                .BorderRight(value, unit);
        }

        public static IContainer BorderHorizontal(this IContainer element, float value, Unit unit = Unit.Point)
        {
            return element
                .BorderTop(value, unit)
                .BorderBottom(value, unit);
        }

        public static IContainer BorderLeft(this IContainer element, float value, Unit unit = Unit.Point)
        {
            return element.Border(x => x.Left = value.ToPoints(unit));
        }

        public static IContainer BorderRight(this IContainer element, float value, Unit unit = Unit.Point)
        {
            return element.Border(x => x.Right = value.ToPoints(unit));
        }

        public static IContainer BorderTop(this IContainer element, float value, Unit unit = Unit.Point)
        {
            return element.Border(x => x.Top = value.ToPoints(unit));
        }

        public static IContainer BorderBottom(this IContainer element, float value, Unit unit = Unit.Point)
        {
            return element.Border(x => x.Bottom = value.ToPoints(unit));
        }

        public static IContainer BorderColor(this IContainer element, string color)
        {
            ColorValidator.Validate(color);
            return element.Border(x => x.BorderColor = color);
        }
    }
}
