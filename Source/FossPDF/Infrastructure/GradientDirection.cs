namespace FossPDF.Infrastructure
{
    /// <summary>
    /// Direction of a linear gradient inside the bounds of the decorated container.
    /// </summary>
    public enum GradientDirection
    {
        /// <summary>From the left edge to the right edge.</summary>
        Horizontal,

        /// <summary>From the top edge to the bottom edge.</summary>
        Vertical,

        /// <summary>From the top-left corner to the bottom-right corner.</summary>
        DiagonalTopLeftToBottomRight,

        /// <summary>From the top-right corner to the bottom-left corner.</summary>
        DiagonalTopRightToBottomLeft
    }
}
