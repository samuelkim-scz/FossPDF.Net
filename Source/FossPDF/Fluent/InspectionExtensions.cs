using System;
using System.Runtime.CompilerServices;
using FossPDF.Elements;
using FossPDF.Infrastructure;
using FossPDF.Previewer;

namespace FossPDF.Fluent
{
    public static class InspectionExtensions
    {
        /// <summary>
        /// Names an inspection boundary and captures its call site, including in Release builds.
        /// IDs must be unique within the composed document. Use a distinct ID for each component instance.
        /// Source information is sent only when preview inspection is explicitly enabled.
        /// </summary>
        public static IContainer Inspect(this IContainer container, string id, string? label = null,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0, [CallerMemberName] string member = "")
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128)
                throw new ArgumentException("Inspection IDs require 1..128 non-whitespace characters.", nameof(id));
            if (label?.Length > 512 || file.Length > 4096 || member.Length > 512 || line < 0)
                throw new ArgumentException("Inspection source or label exceeds the protocol limits.");
            return container.Element(new InspectionBoundary
            {
                Id = id, Label = label,
                Source = new InspectionSource { File = file, Line = line, Member = member }
            });
        }
    }
}

namespace FossPDF.Elements
{
    internal sealed class InspectionBoundary : ContainerElement
    {
        public string Id { get; set; } = string.Empty;
        public string? Label { get; set; }
        public InspectionSource Source { get; set; } = new();
    }
}
