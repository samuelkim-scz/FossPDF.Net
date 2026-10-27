namespace FossPDF.Infrastructure
{
    /// <summary>
    /// Determines where a border is painted relative to the layout bounds of its container.
    /// </summary>
    public enum BorderAlignment
    {
        /// <summary>The border is painted inside the layout bounds and never overlaps neighbouring content.</summary>
        Inside,

        /// <summary>The border is centered on the layout bounds. This is the default and the legacy behavior.</summary>
        Center,

        /// <summary>The border is painted outside the layout bounds.</summary>
        Outside
    }
}
