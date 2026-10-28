using FossPDF.Infrastructure;

namespace FossPDF.UnitTests.TestEngine.Operations
{
    internal class CanvasDrawShadowOperation : OperationBase
    {
        public Position Position { get; }
        public Size Size { get; }
        public BorderRadius CornerRadius { get; }
        public BoxShadowStyle Shadow { get; }

        public CanvasDrawShadowOperation(
            Position position, Size size, BorderRadius cornerRadius, BoxShadowStyle shadow)
        {
            Position = position;
            Size = size;
            CornerRadius = cornerRadius;
            Shadow = shadow;
        }
    }
}
