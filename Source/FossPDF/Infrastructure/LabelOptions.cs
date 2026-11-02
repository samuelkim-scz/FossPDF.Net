using System;

namespace FossPDF.Infrastructure
{
    /// <summary>Settings for one label job. Starting positions affect only its first sheet.</summary>
    public sealed record LabelOptions
    {
        /// <summary>Number of leading slots to leave empty. Null defaults to zero.</summary>
        public int? SkipLabels { get; init; }
        /// <summary>One-based starting row. Specify together with StartColumn.</summary>
        public int? StartRow { get; init; }
        /// <summary>One-based starting column. Specify together with StartRow.</summary>
        public int? StartColumn { get; init; }
        /// <summary>Consecutive copies to print for each input item.</summary>
        public int CopiesPerItem { get; init; } = 1;
        /// <summary>Physical shift to the right (negative shifts left) on every sheet.</summary>
        public float OffsetX { get; init; }
        /// <summary>Physical shift down (negative shifts up) on every sheet.</summary>
        public float OffsetY { get; init; }
        /// <summary>Unit for OffsetX and OffsetY.</summary>
        public Unit Unit { get; init; } = Unit.Point;

        internal int GetSkipCount(LabelStock stock)
        {
            var capacity = stock.Rows * stock.Columns;
            if (SkipLabels.HasValue && (SkipLabels.Value < 0 || SkipLabels.Value >= capacity))
                throw new ArgumentOutOfRangeException(nameof(SkipLabels), "SkipLabels must identify an available slot on the first sheet.");
            if (StartRow.HasValue != StartColumn.HasValue)
                throw new ArgumentException("Specify both StartRow and StartColumn.");
            var skip = SkipLabels ?? 0;
            if (StartRow.HasValue)
            {
                if (StartRow.Value < 1 || StartRow.Value > stock.Rows)
                    throw new ArgumentOutOfRangeException(nameof(StartRow));
                if (StartColumn!.Value < 1 || StartColumn.Value > stock.Columns)
                    throw new ArgumentOutOfRangeException(nameof(StartColumn));
                var position = (StartRow.Value - 1) * stock.Columns + StartColumn.Value - 1;
                if (SkipLabels.HasValue && skip != position)
                    throw new ArgumentException("SkipLabels and the starting row/column describe different slots.");
                skip = position;
            }
            if (CopiesPerItem <= 0)
                throw new ArgumentOutOfRangeException(nameof(CopiesPerItem));
            return skip;
        }

        internal (float X, float Y) GetOffsets(LabelStock stock)
        {
            var x = OffsetX.ToPoints(Unit);
            var y = OffsetY.ToPoints(Unit);
            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y))
                throw new ArgumentException("Label alignment offsets must be finite.");
            stock.ValidateFit(x, y);
            return (x, y);
        }
    }
}
