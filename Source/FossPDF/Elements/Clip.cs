namespace FossPDF.Elements
{
    /// <summary>
    /// Clips the child content to the layout bounds, rounded by the decoration's corner radii.
    /// Backgrounds and borders are drawn outside the clip so they are never cut off.
    /// </summary>
    internal class Clip : RectangleDecoration
    {
        public Clip()
        {
            ClipContent = true;
        }
    }
}
