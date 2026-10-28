namespace FossPDF.Drawing
{
    /// <summary>
    /// Stroke description for a decorative line: width, paint, optional multi-stop gradient along
    /// the line and an optional dash pattern with a phase offset. Solid lines keep the legacy
    /// rectangle drawing operation; this contract is used only when a line is dashed or gradient.
    /// </summary>
    internal struct LineStyle
    {
        public float Width { get; set; }
        public string? Color { get; set; }
        public string[]? GradientColors { get; set; }
        public float[]? DashPattern { get; set; }
        public float DashPhase { get; set; }
    }
}
