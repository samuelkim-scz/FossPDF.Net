using System;
using FossPDF.Helpers;

namespace FossPDF.Infrastructure
{
    /// <summary>Physical geometry of a rectangular label sheet or a single-label page.</summary>
    public sealed record LabelStock
    {
        /// <summary>PDF page dimensions; PageSize always stores points.</summary>
        public PageSize PageSize { get; init; } = PageSizes.Letter;
        public float LabelWidth { get; init; }
        public float LabelHeight { get; init; }
        public int Columns { get; init; } = 1;
        public int Rows { get; init; } = 1;
        public float MarginLeft { get; init; }
        public float MarginTop { get; init; }
        public float HorizontalGap { get; init; }
        public float VerticalGap { get; init; }
        /// <summary>Unit for label dimensions, margins and gaps.</summary>
        public Unit Unit { get; init; } = Unit.Point;

        internal LabelStock InPoints()
        {
            if (PageSize == null)
                throw new ArgumentException("Label stock requires a page size.");
            Positive(PageSize.Width, nameof(PageSize));
            Positive(PageSize.Height, nameof(PageSize));
            if (PageSize.Width > Size.Infinity || PageSize.Height > Size.Infinity)
                throw new ArgumentException("Label page dimensions exceed the supported page size.");
            if (Rows <= 0 || Columns <= 0 || (long)Rows * Columns > int.MaxValue)
                throw new ArgumentException("Label rows and columns must be positive and their product must fit an Int32.");

            var result = this with
            {
                LabelWidth = LabelWidth.ToPoints(Unit),
                LabelHeight = LabelHeight.ToPoints(Unit),
                MarginLeft = MarginLeft.ToPoints(Unit),
                MarginTop = MarginTop.ToPoints(Unit),
                HorizontalGap = HorizontalGap.ToPoints(Unit),
                VerticalGap = VerticalGap.ToPoints(Unit),
                Unit = Unit.Point
            };
            Positive(result.LabelWidth, nameof(LabelWidth));
            Positive(result.LabelHeight, nameof(LabelHeight));
            NonNegative(result.MarginLeft, nameof(MarginLeft));
            NonNegative(result.MarginTop, nameof(MarginTop));
            NonNegative(result.HorizontalGap, nameof(HorizontalGap));
            NonNegative(result.VerticalGap, nameof(VerticalGap));
            result.ValidateFit(0, 0);
            return result;
        }

        internal void ValidateFit(float offsetX, float offsetY)
        {
            var right = MarginLeft + (double)Columns * LabelWidth + (Columns - 1d) * HorizontalGap + offsetX;
            var bottom = MarginTop + (double)Rows * LabelHeight + (Rows - 1d) * VerticalGap + offsetY;
            if (MarginLeft + offsetX < -Size.Epsilon || MarginTop + offsetY < -Size.Epsilon ||
                right > PageSize.Width + Size.Epsilon || bottom > PageSize.Height + Size.Epsilon)
                throw new ArgumentException("The label grid, including alignment offsets, must fit within the page.");
        }

        private static void Positive(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0)
                throw new ArgumentOutOfRangeException(name, "The dimension must be finite and positive.");
        }

        private static void NonNegative(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(name, "The dimension must be finite and non-negative.");
        }
    }
}
