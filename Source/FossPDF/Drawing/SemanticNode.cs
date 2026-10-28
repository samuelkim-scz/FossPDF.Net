using System.Collections.Generic;

namespace FossPDF.Drawing
{
    /// <summary>
    /// One logical node of the accessibility tree. The instance is owned by a semantic element and
    /// is reused when that element is drawn again (page breaks, repeated components), so content on
    /// several pages stays attached to one structure element.
    /// </summary>
    internal sealed class SemanticNode
    {
        public SemanticNode(string role, string? alternativeText, string? language)
        {
            Role = role;
            AlternativeText = alternativeText;
            Language = language;
        }

        public string Role { get; }
        public string? AlternativeText { get; }
        public string? Language { get; }

        public string? Id { get; set; }
        public int CreationOrder { get; set; }
        public int ContentOrder { get; set; } = int.MaxValue;

        public SemanticNode? Parent { get; set; }
        public List<SemanticNode> Children { get; } = new List<SemanticNode>();
    }
}
