using System;
using System.Linq;
using FossPDF.Elements;
using FossPDF.Helpers;
using FossPDF.Infrastructure;

namespace FossPDF.Fluent
{
    public static class LineExtensions
    {
        private static ILine Line(this IContainer element, LineType type, float size)
        {
            var line = new Line
            {
                Size = size,
                Type = type
            };

            element.Element(line);
            return line;
        }

        public static ILine LineVertical(this IContainer element, float size, Unit unit = Unit.Point)
        {
            return element.Line(LineType.Vertical, size.ToPoints(unit));
        }

        public static ILine LineHorizontal(this IContainer element, float size, Unit unit = Unit.Point)
        {
            return element.Line(LineType.Horizontal, size.ToPoints(unit));
        }

        public static void LineColor(this ILine descriptor, string value)
        {
            ColorValidator.Validate(value);
            ((Line)descriptor).Color = value;
        }

        /// <summary>
        /// Applies a linear gradient along the line. The colors are evenly distributed from the
        /// start of the line to its end; at least two colors are required.
        /// </summary>
        public static void LineGradient(this ILine descriptor, params string[] colors)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));

            if (colors == null || colors.Length < 2)
                throw new ArgumentException("A line gradient requires at least two colors.", nameof(colors));

            foreach (var color in colors)
                ColorValidator.Validate(color);

            ((Line)descriptor).GradientColors = colors.ToArray();
        }

        /// <summary>
        /// Configures a dash pattern for the line. The array must contain pairs of dash and gap
        /// lengths, so its length must be even. Values are converted from <paramref name="unit"/>.
        /// </summary>
        public static void LineDashPattern(this ILine descriptor, float[] pattern, Unit unit = Unit.Point)
        {
            LineDashPattern(descriptor, pattern, 0, unit);
        }

        /// <summary>
        /// Configures a dash pattern with a phase offset. A positive phase starts the line inside
        /// the pattern, which is useful for aligning dashes across neighboring elements.
        /// </summary>
        public static void LineDashPattern(this ILine descriptor, float[] pattern, float phase, Unit unit = Unit.Point)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));

            if (pattern == null)
                throw new ArgumentNullException(nameof(pattern));

            if (pattern.Length == 0 || pattern.Length % 2 != 0)
                throw new ArgumentException(
                    "The dash pattern must contain an even number of values (dash and gap pairs).",
                    nameof(pattern));

            if (!float.IsFinite(phase))
                throw new ArgumentOutOfRangeException(nameof(phase), phase, "The dash phase must be finite.");

            var converted = new float[pattern.Length];

            for (var index = 0; index < pattern.Length; index++)
            {
                var value = pattern[index];

                if (!float.IsFinite(value) || value < 0)
                    throw new ArgumentException(
                        "Dash and gap lengths must be finite and non-negative.",
                        nameof(pattern));

                converted[index] = value.ToPoints(unit);
            }

            if (converted.All(x => x <= 0))
                throw new ArgumentException("At least one dash length must be greater than zero.", nameof(pattern));

            var line = (Line) descriptor;
            line.DashPattern = converted;
            line.DashPhase = phase.ToPoints(unit);
        }
    }
}
