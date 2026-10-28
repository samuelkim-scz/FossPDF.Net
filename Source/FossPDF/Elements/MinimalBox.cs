namespace FossPDF.Elements
{
    /// <summary>
    /// Legacy name for shrinking both axes. Kept as a supported equivalent of Shrink().
    /// </summary>
    internal class MinimalBox : Shrink
    {
        public MinimalBox()
        {
            Vertical = true;
            Horizontal = true;
        }
    }
}
