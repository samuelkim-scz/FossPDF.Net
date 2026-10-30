using System;
using System.Collections.Generic;
using System.Linq;

namespace FossPDF.Previewer
{
    internal static class PreviewerProtocol
    {
        internal const int Version = 1;
        internal const int MaxCommandBytes = 16 * 1024 * 1024;
        internal const long MaxRequestBytes = 256L * 1024 * 1024;
        internal const int MaxPages = 10000;

        internal static void Validate(InspectionSnapshot inspection, int pageCount)
        {
            if (inspection.Version != Version)
                throw new ArgumentException("Unsupported inspection version; this previewer supports version 1.");
            if (inspection.Elements == null || inspection.Occurrences == null ||
                inspection.Elements.Count > 20000 || inspection.Occurrences.Count > 100000)
                throw new ArgumentException("Invalid inspection collections or limits.");
            var nodes = new Dictionary<string, InspectionElement>(StringComparer.Ordinal);
            var depths = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var node in inspection.Elements)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.Id) || node.Id.Length > 4096 ||
                    string.IsNullOrWhiteSpace(node.Type) || node.Type.Length > 128 ||
                    node.Label?.Length > 512 || node.Configuration == null || node.Configuration.Count > 32 ||
                    node.Configuration.Any(x => x.Key.Length > 128 || x.Value == null || x.Value.Length > 512) ||
                    (node.Source != null && (node.Source.File == null || node.Source.File.Length > 4096 ||
                        node.Source.Member == null || node.Source.Member.Length > 512 || node.Source.Line < 0)) ||
                    nodes.ContainsKey(node.Id))
                    throw new ArgumentException("Invalid or duplicate inspection element.");
                // Parent-before-child order makes cycles impossible and keeps validation linear.
                if (node.ParentId != null && !nodes.ContainsKey(node.ParentId))
                    throw new ArgumentException("Inspection parents must precede their children.");
                var depth = node.ParentId == null ? 0 : depths[node.ParentId] + 1;
                if (depth > 128) throw new ArgumentException("Inspection hierarchy exceeds 128 levels.");
                depths.Add(node.Id, depth);
                nodes.Add(node.Id, node);
            }
            foreach (var occurrence in inspection.Occurrences)
            {
                if (occurrence == null || occurrence.ElementId == null || !nodes.ContainsKey(occurrence.ElementId) ||
                    occurrence.PageNumber < 1 || occurrence.PageNumber > pageCount ||
                    occurrence.Transform == null || occurrence.Transform.Length != 6 ||
                    occurrence.Transform.Any(x => !Finite(x)) ||
                    !Finite(occurrence.X) || !Finite(occurrence.Y) || !Finite(occurrence.Width) ||
                    !Finite(occurrence.Height) || !Finite(occurrence.LocalWidth) || !Finite(occurrence.LocalHeight) ||
                    occurrence.Width < 0 || occurrence.Height < 0 || occurrence.LocalWidth < 0 || occurrence.LocalHeight < 0)
                    throw new ArgumentException("Invalid inspection occurrence geometry or reference.");
            }
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
