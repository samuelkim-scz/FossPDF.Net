using FossPDF.Drawing;
using FossPDF.Infrastructure;

namespace FossPDF.UnitTests.TestEngine.Operations
{
    internal class CanvasDrawRectangleStyleOperation : OperationBase
    {
        public Position Position { get; }
        public Size Size { get; }
        public RectangleStyle Style { get; }

        public CanvasDrawRectangleStyleOperation(Position position, Size size, RectangleStyle style)
        {
            Position = position;
            Size = size;
            Style = style;
        }
    }
}
