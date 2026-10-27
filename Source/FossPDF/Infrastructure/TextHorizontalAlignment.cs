namespace FossPDF.Infrastructure
{
    /// <summary>
    /// Defines how text is aligned horizontally within the available width of a paragraph.
    /// Unlike <see cref="HorizontalAlignment"/>, which positions an element inside its parent,
    /// this enum also supports justification and direction-relative alignment.
    /// </summary>
    public enum TextHorizontalAlignment
    {
        /// <summary>
        /// Aligns text to the left edge of the paragraph. This is a physical alignment and
        /// does not depend on the text direction.
        /// </summary>
        Left,

        /// <summary>
        /// Centers text within the paragraph width.
        /// </summary>
        Center,

        /// <summary>
        /// Aligns text to the right edge of the paragraph. This is a physical alignment and
        /// does not depend on the text direction.
        /// </summary>
        Right,

        /// <summary>
        /// Stretches lines to the full paragraph width by expanding spaces between words.
        /// The last line of every paragraph is not justified and is aligned to the logical
        /// start edge instead.
        /// </summary>
        Justify,

        /// <summary>
        /// Aligns text to the logical start edge of the paragraph: the left edge for
        /// left-to-right content and the right edge for right-to-left content.
        /// </summary>
        Start,

        /// <summary>
        /// Aligns text to the logical end edge of the paragraph: the right edge for
        /// left-to-right content and the left edge for right-to-left content.
        /// </summary>
        End
    }
}
