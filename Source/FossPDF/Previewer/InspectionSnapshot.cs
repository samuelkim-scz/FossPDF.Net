using System;
using System.Collections.Generic;

namespace FossPDF.Previewer
{
    /// <summary>Opt-in local inspection. Limits fail generation rather than silently dropping data.</summary>
    public sealed class PreviewerOptions
    {
        public bool EnableInspection { get; set; }
        public int MaxElements { get; set; } = 20000;
        public int MaxOccurrences { get; set; } = 100000;
        internal void Validate()
        {
            if (MaxElements < 1 || MaxElements > 20000 || MaxOccurrences < 1 || MaxOccurrences > 100000)
                throw new ArgumentOutOfRangeException(nameof(PreviewerOptions), "Inspection limits must be within 1..20000 elements and 1..100000 occurrences.");
        }
    }

    /// <summary>Version 1 inspection wire contract. Coordinates use points from the page's top left.</summary>
    public sealed class InspectionSnapshot
    {
        public int Version { get; set; } = 1;
        public List<InspectionElement> Elements { get; set; } = new();
        public List<InspectionOccurrence> Occurrences { get; set; } = new();
    }

    public sealed class InspectionElement
    {
        public string Id { get; set; } = string.Empty;
        public string? ParentId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string? Label { get; set; }
        public InspectionSource? Source { get; set; }
        public Dictionary<string, string> Configuration { get; set; } = new();
    }

    public sealed class InspectionSource
    {
        public string File { get; set; } = string.Empty;
        public int Line { get; set; }
        public string Member { get; set; } = string.Empty;
    }

    public sealed class InspectionOccurrence
    {
        public string ElementId { get; set; } = string.Empty;
        public int PageNumber { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public float LocalWidth { get; set; }
        public float LocalHeight { get; set; }
        /// <summary>Affine transform: M11, M12, M21, M22, M41, M42.</summary>
        public float[] Transform { get; set; } = Array.Empty<float>();
    }
}
