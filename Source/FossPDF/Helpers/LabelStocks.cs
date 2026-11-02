using FossPDF.Infrastructure;

namespace FossPDF.Helpers
{
    /// <summary>Common rectangular label formats.</summary>
    public static class LabelStocks
    {
        /// <summary>
        /// Letter sheet with 30 labels, 2-5/8 x 1 inches, in three columns and ten rows.
        /// Nominal side margin 3/16 inch, top margin 1/2 inch, horizontal gap 1/8 inch.
        /// Also matches the Avery 8160 template. Print at Actual Size / 100%.
        /// </summary>
        public static LabelStock Avery5160 => new()
        {
            PageSize = PageSizes.Letter,
            LabelWidth = 2.625f,
            LabelHeight = 1,
            Columns = 3,
            Rows = 10,
            MarginLeft = 0.1875f,
            MarginTop = 0.5f,
            HorizontalGap = 0.125f,
            Unit = Unit.Inch
        };

        /// <summary>A single 4 x 6 inch label per page, suitable for thermal-label workflows.</summary>
        public static LabelStock Thermal4x6 => new()
        {
            PageSize = new PageSize(4, 6, Unit.Inch),
            LabelWidth = 4,
            LabelHeight = 6,
            Unit = Unit.Inch
        };
    }
}
