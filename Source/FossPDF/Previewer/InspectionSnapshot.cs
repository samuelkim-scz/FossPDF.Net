using System;
using System.Collections.Generic;

namespace FossPDF.Previewer
{
    /// <summary>Optional local inspection protocol, independent of the existing picture format.</summary>
    public sealed class InspectionSnapshot
    {
        public const int CurrentVersion = 1;
        public int Version { get; set; } = CurrentVersion;
        public List<InspectionNode> Nodes { get; set; } = new();
        public List<InspectionOccurrence> Occurrences { get; set; } = new();
        public string? Diagnostic { get; set; }

        /// <summary>Checks protocol bounds and references before accepting a snapshot.</summary>
        public void Validate(int pageCount)
        {
            if (Version != CurrentVersion)
                throw new ArgumentException("Unsupported inspection protocol version; supported version is 1.");
            if (Nodes == null || Occurrences == null || Nodes.Count > 20000 || Occurrences.Count > 100000 || Diagnostic?.Length > 32768)
                throw new ArgumentException("Inspection payload exceeds its limits or is missing collections.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in Nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.Id) || node.Id.Length > 2048 || ids.Contains(node.Id) ||
                    node.Type == null || node.Type.Length > 256 || node.Label?.Length > 4096 || node.Configuration?.Length > 4096 ||
                    node.SourceFile?.Length > 4096 || node.SourceMember?.Length > 512 || node.SourceLine < 0 ||
                    (node.ParentId != null && !ids.Contains(node.ParentId)))
                    throw new ArgumentException("Inspection nodes must be bounded, unique and ordered with parents before children.");
                ids.Add(node.Id);
            }
            foreach (var occurrence in Occurrences)
            {
                if (occurrence == null || occurrence.NodeId == null || !ids.Contains(occurrence.NodeId) || occurrence.Page < 1 || occurrence.Page > pageCount ||
                    !Finite(occurrence.X) || !Finite(occurrence.Y) || !Finite(occurrence.Width) || !Finite(occurrence.Height) ||
                    occurrence.Width < 0 || occurrence.Height < 0 || occurrence.Text?.Length > 4096)
                    throw new ArgumentException("Inspection occurrences have invalid nodes, pages or geometry.");
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public sealed class InspectionNode
    {
        public string Id { get; set; } = string.Empty;
        public string? ParentId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string? Label { get; set; }
        public string? Configuration { get; set; }
        public string? SourceFile { get; set; }
        public int SourceLine { get; set; }
        public string? SourceMember { get; set; }
        public override string ToString() => Label ?? Type;
    }

    public sealed class InspectionOccurrence
    {
        public string NodeId { get; set; } = string.Empty;
        public int Page { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public string? Text { get; set; }
    }
}
