using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using FossPDF.Drawing;
using FossPDF.Drawing.Proxy;
using FossPDF.Elements;
using FossPDF.Elements.Text;
using FossPDF.Infrastructure;

namespace FossPDF.Previewer
{
    internal sealed class InspectionMarker : ContainerElement
    {
        internal string Id { get; init; } = string.Empty;
        internal string? Label { get; init; }
        internal string? Configuration { get; init; }
        internal string SourceFile { get; init; } = string.Empty;
        internal int SourceLine { get; init; }
        internal string SourceMember { get; init; } = string.Empty;
    }

    internal sealed class InspectionCollector
    {
        internal InspectionSnapshot Snapshot { get; } = new();
        private readonly ConditionalWeakTable<Element, object> wrapped = new();
        private readonly Dictionary<string, InspectionNode> nodes = new(StringComparer.Ordinal);
        private readonly ConditionalWeakTable<Element, InspectionNode> elementIds = new();
        private readonly Stack<string> parents = new();
        private int roots;
        private int runtimeIndex;
        internal bool IsActive => parents.Count > 0;

        internal static InspectionCollector? ForCanvas(ICanvas? canvas) => canvas switch
        {
            SkiaPictureCanvas picture => picture.Inspection,
            ProxyCanvas proxy => proxy.Inspection ?? ForCanvas(proxy.Target),
            _ => null
        };

        private static SkiaPictureCanvas? PictureCanvas(ICanvas? canvas) => canvas switch
        {
            SkiaPictureCanvas picture => picture,
            ProxyCanvas proxy => PictureCanvas(proxy.Target),
            _ => null
        };

        internal void Apply(Element content)
        {
            if (content is Empty or InspectionProxy || wrapped.TryGetValue(content, out _))
                return;
            wrapped.Add(content, new object());
            var parent = parents.Count > 0 ? parents.Peek() : null;
            var id = parent == null ? "document-" + roots++ : parent + "/runtime-" + runtimeIndex++;
            var node = AddNode(content, id, parent);
            Wrap(content, node.Id, 0);
        }

        private void Wrap(Element owner, string parent, int depth)
        {
            if (depth > 256)
                throw new InvalidOperationException("Inspection hierarchy exceeds 256 levels.");
            var index = 0;
            foreach (var child in owner.GetChildren().ToArray())
            {
                var path = parent + "/" + index++;
                if (child == null || child is Empty || child is InspectionProxy)
                    continue;
                var id = child is InspectionMarker marker ? parent.Split('/')[0] + "/id:" + Uri.EscapeDataString(marker.Id) : path;
                var node = AddNode(child, id, parent);
                Wrap(child, node.Id, depth + 1);
            }
            owner.CreateProxy(child =>
            {
                if (child == null || child is Empty || child is InspectionProxy)
                    return child;
                return new InspectionProxy(this, elementIds.GetValue(child, _ =>
                    throw new InvalidOperationException("Inspection child was not registered.")).Id, child)
                    { Canvas = child.Canvas, PageContext = child.PageContext };
            });
        }

        private InspectionNode AddNode(Element element, string id, string? parent)
        {
            if (elementIds.TryGetValue(element, out var known))
                return known;
            if (nodes.TryGetValue(id, out var existing))
            {
                if (existing.ParentId != parent || existing.Type != element.GetType().Name)
                    throw new ArgumentException("Inspection IDs must be unique within the logical document: " + id);
                elementIds.Add(element, existing);
                return existing;
            }
            if (nodes.Count >= 20000 || id.Length > 2048)
                throw new InvalidOperationException("Inspection exceeds its 20,000 node limit.");
            var marker = element as InspectionMarker;
            var node = new InspectionNode
            {
                Id = id, ParentId = parent, Type = element.GetType().Name,
                Label = marker?.Label ?? (element is TextBlock text ? Limit(text.Text) : null),
                Configuration = marker?.Configuration,
                SourceFile = marker?.SourceFile, SourceLine = marker?.SourceLine ?? 0, SourceMember = marker?.SourceMember
            };
            nodes.Add(id, node);
            elementIds.Add(element, node);
            Snapshot.Nodes.Add(node);
            return node;
        }

        private static string Limit(string text) => text.Length <= 4096 ? text : text.Substring(0, 4096);

        private sealed class InspectionProxy : ElementProxy
        {
            private readonly InspectionCollector collector;
            private readonly string id;
            internal InspectionProxy(InspectionCollector collector, string id, Element child)
            {
                this.collector = collector; this.id = id; Child = child;
            }

            internal override SpacePlan Measure(Size availableSpace)
            {
                // Deferred and dynamic elements can compose children while measuring. Give those
                // children the same structural parent and runtime slots as the final draw.
                var previousIndex = collector.runtimeIndex;
                collector.runtimeIndex = 0;
                collector.parents.Push(id);
                try { return base.Measure(availableSpace); }
                finally { collector.parents.Pop(); collector.runtimeIndex = previousIndex; }
            }

            internal override void Draw(Size availableSpace)
            {
                var previousIndex = collector.runtimeIndex;
                collector.runtimeIndex = 0;
                collector.parents.Push(id);
                try
                {
                    if (Child is FossPDF.Elements.Lazy)
                        collector.Wrap(Child, id, 0);
                    // ProxyCanvas can target the real page or a speculative FreeCanvas. Only the
                    // real page emits geometry; the association still instruments deferred trees.
                    if (PictureCanvas(Canvas) is { } canvas && ReferenceEquals(canvas.Inspection, collector))
                    {
                        if (collector.Snapshot.Occurrences.Count >= 100000)
                            throw new InvalidOperationException("Inspection exceeds its 100,000 occurrence limit.");
                        var transform = Canvas.Transform;
                        var points = new[] { Vector2.Zero, new Vector2(availableSpace.Width, 0),
                            new Vector2(0, availableSpace.Height), new Vector2(availableSpace.Width, availableSpace.Height) }
                            .Select(point => Vector2.Transform(point, transform)).ToArray();
                        var x = points.Min(point => point.X); var y = points.Min(point => point.Y);
                        collector.Snapshot.Occurrences.Add(new InspectionOccurrence
                        {
                            NodeId = id, Page = canvas.Pictures.Count + 1, X = x, Y = y,
                            Width = points.Max(point => point.X) - x, Height = points.Max(point => point.Y) - y,
                            Text = Child is TextBlock text ? Limit(text.Text) : null
                        });
                    }
                    base.Draw(availableSpace);
                }
                finally { collector.parents.Pop(); collector.runtimeIndex = previousIndex; }
            }
        }
    }
}
