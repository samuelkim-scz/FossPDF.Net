using FossPDF.Drawing;
using FossPDF.Helpers;
using FossPDF.Infrastructure;

namespace FossPDF.Elements
{
    public interface ILine
    {
        
    }
    
    internal enum LineType
    {
        Vertical,
        Horizontal
    }
    
    internal class Line : Element, ILine, ICacheable
    {
        public LineType Type { get; set; } = LineType.Vertical;
        public string Color { get; set; } = Colors.Black;
        public float Size { get; set; } = 1;

        public string[]? GradientColors { get; set; }
        public float[]? DashPattern { get; set; }
        public float DashPhase { get; set; }

        internal override SpacePlan Measure(Size availableSpace)
        {
            if (availableSpace.IsNegative())
                return SpacePlan.Wrap();
            
            return Type switch
            {
                LineType.Vertical when availableSpace.Width + Infrastructure.Size.Epsilon >= Size => SpacePlan.FullRender(Size, 0),
                LineType.Horizontal when availableSpace.Height + Infrastructure.Size.Epsilon >= Size => SpacePlan.FullRender(0, Size),
                _ => SpacePlan.Wrap()
            };
        }

        internal override void Draw(Size availableSpace)
        {
            if (GradientColors == null && DashPattern == null)
            {
                DrawLegacyLine(availableSpace);
                return;
            }

            var style = new LineStyle
            {
                Width = Size,
                Color = Color,
                GradientColors = GradientColors,
                DashPattern = DashPattern,
                DashPhase = DashPhase
            };

            if (Type == LineType.Vertical)
            {
                Canvas.DrawLine(Position.Zero, new Position(0, availableSpace.Height), style);
            }
            else if (Type == LineType.Horizontal)
            {
                Canvas.DrawLine(Position.Zero, new Position(availableSpace.Width, 0), style);
            }
        }

        private void DrawLegacyLine(Size availableSpace)
        {
            if (Type == LineType.Vertical)
            {
                Canvas.DrawRectangle(new Position(-Size/2, 0), new Size(Size, availableSpace.Height), Color);
            }
            else if (Type == LineType.Horizontal)
            {
                Canvas.DrawRectangle(new Position(0, -Size/2), new Size(availableSpace.Width, Size), Color);
            }
        }
    }
}
