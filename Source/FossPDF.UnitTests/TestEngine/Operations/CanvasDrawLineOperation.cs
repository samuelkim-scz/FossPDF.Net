using FossPDF.Drawing;
using FossPDF.Infrastructure;

namespace FossPDF.UnitTests.TestEngine.Operations
{
    internal class CanvasDrawLineOperation : OperationBase
    {
        public Position Start { get; }
        public Position End { get; }
        public LineStyle Style { get; }

        public CanvasDrawLineOperation(Position start, Position end, LineStyle style)
        {
            Start = start;
            End = end;
            Style = style;
        }
    }
}
