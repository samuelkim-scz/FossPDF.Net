using FossPDF.Drawing;
using FossPDF.Infrastructure;

namespace FossPDF.Elements.Table
{
    internal class TableCellRenderingCommand
    {
        public TableCell Cell { get; set; } = null!; // Supplied by the layout planner.
        public SpacePlan Measurement { get; set; }
        public Size Size { get; set; }
        public Position Offset { get; set; }
    }
}
