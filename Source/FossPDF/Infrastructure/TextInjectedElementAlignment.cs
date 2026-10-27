namespace FossPDF.Infrastructure
{
    /// <summary>
    /// Defines how content injected into a text line is aligned to the surrounding typography.
    /// </summary>
    public enum TextInjectedElementAlignment
    {
        /// <summary>
        /// Aligns the bottom edge of the injected element with the text baseline. The element sits above the baseline.
        /// </summary>
        AboveBaseline,

        /// <summary>
        /// Aligns the top edge of the injected element with the text baseline. The element hangs below the baseline.
        /// </summary>
        BelowBaseline,

        /// <summary>
        /// Aligns the top edge of the injected element with the top of the font. Taller elements extend downward.
        /// </summary>
        Top,

        /// <summary>
        /// Aligns the bottom edge of the injected element with the top of the font. Taller elements extend upward.
        /// </summary>
        Bottom,

        /// <summary>
        /// Aligns the middle of the injected element with the middle of the text.
        /// </summary>
        Middle
    }
}
