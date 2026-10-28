using FossPDF.Helpers;

namespace FossPDF.Elements
{
    internal class Background : RectangleDecoration
    {
        public string Color
        {
            get => FillColor ?? Colors.Black;
            set => FillColor = value;
        }
    }
}
