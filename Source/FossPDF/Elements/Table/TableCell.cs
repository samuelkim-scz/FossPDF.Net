namespace FossPDF.Elements.Table
{
    using FossPDF.Drawing;
    using FossPDF.Infrastructure;

    internal class TableCell : Container, ITableCellContainer
    {
        private SemanticNode? _semanticNode;
        internal string SemanticRole { get; set; } = "TD";

        internal override void Draw(Size availableSpace)
        {
            var explicitNode = AutomaticSemanticScope.GetExplicitCellNode(Child);
            using var semantic = explicitNode != null
                ? default : new AutomaticSemanticScope(Canvas, ref _semanticNode, SemanticRole);
            var node = explicitNode ?? _semanticNode;
            if (node != null && AutomaticSemanticScope.GetCanvas(Canvas) != null)
            {
                node.RowSpan = RowSpan;
                node.ColumnSpan = ColumnSpan;
                if (node.Role == "TH")
                    node.TableHeaderScope ??= "Column";
            }
            base.Draw(availableSpace);
        }

        public int Row { get; set; } = 0;
        public int RowSpan { get; set; } = 1;

        public int Column { get; set; } = 0;
        public int ColumnSpan { get; set; } = 1;
        
        public int ZIndex { get; set; }
        
        public bool IsRendered { get; set; }
    }
}
