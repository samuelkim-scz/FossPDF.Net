using FossPDF.Helpers;

namespace FossPDF.Elements
{
    internal class Border : RectangleDecoration
    {
        public string Color
        {
            get => BorderColor ?? Colors.Black;
            set => BorderColor = value;
        }

        public override string ToString()
        {
            return $"Border: Top({Top}) Right({Right}) Bottom({Bottom}) Left({Left}) Color({Color})";
        }
    }
}
