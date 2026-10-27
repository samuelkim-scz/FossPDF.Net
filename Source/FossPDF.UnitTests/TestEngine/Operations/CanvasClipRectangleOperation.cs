using FossPDF.Infrastructure;

namespace FossPDF.UnitTests.TestEngine.Operations
{
    internal class CanvasClipRectangleOperation : OperationBase
    {
        public Position Position { get; }
        public Size Size { get; }
        public BorderRadius CornerRadius { get; }

        public CanvasClipRectangleOperation(Position position, Size size, BorderRadius cornerRadius)
        {
            Position = position;
            Size = size;
            CornerRadius = cornerRadius;
        }
    }
}
