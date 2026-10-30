using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using FossPDF.Drawing;
using FossPDF.Drawing.Proxy;
using FossPDF.Elements;
using FossPDF.Infrastructure;

namespace FossPDF.Previewer
{
    internal sealed class InspectionCapture
    {
        internal InspectionSnapshot Snapshot { get; } = new();
        private readonly int MaxElements;
        private readonly int MaxOccurrences;
        private readonly ConditionalWeakTable<Element, InspectionElement> Installed = new();
        private readonly Dictionary<string, InspectionElement> Nodes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> ExplicitPaths = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> Depths = new(StringComparer.Ordinal);
        private InspectionElement? Current;
        private int GeneratedIndex;
        private int Roots;
        private int PayloadBudget;

        private void Reserve(int bytes)
        {
            PayloadBudget = checked(PayloadBudget + bytes);
            if (PayloadBudget > PreviewerProtocol.MaxCommandBytes)
                throw new InvalidOperationException("Preview inspection exceeds the 16 MiB payload budget.");
        }

        internal InspectionCapture(PreviewerOptions options)
        {
            MaxElements = options.MaxElements;
            MaxOccurrences = options.MaxOccurrences;
        }

        internal static InspectionCapture? ForCanvas(ICanvas? canvas) => canvas switch
        {
            SkiaPictureCanvas picture => picture.Inspection,
            ProxyCanvas proxy => proxy.Inspection ?? ForCanvas(proxy.Target),
            _ => null
        };

        private static SkiaPictureCanvas? ActualCanvas(ICanvas? canvas) => canvas switch
        {
            SkiaPictureCanvas picture => picture,
            ProxyCanvas proxy => ActualCanvas(proxy.Target),
            _ => null
        };

        internal void Prepare(Element root)
        {
            if (Installed.TryGetValue(root, out _)) return;
            var path = Current == null ? "root" + Roots++ : Current.Id + "/g" + GeneratedIndex++;
            Install(root, Current, path, 0, wrap: false);
        }

        private Element Install(Element element, InspectionElement? parent, string path, int depth, bool wrap)
        {
            if (element is Empty || element is InspectionProxy) return element;
            // Cache/debug/layout proxies are implementation details, not hierarchy nodes.
            if (element is ElementProxy)
            {
                element.CreateProxy(child => child == null ? null : Install(child, parent, path, depth, true));
                return element;
            }
            var hierarchyDepth = parent == null ? 0 : Depths[parent.Id] + 1;
            if (hierarchyDepth > 128 || path.Length > 4096)
                throw new InvalidOperationException("Preview inspection exceeds the hierarchy depth limit (128).");
            if (Installed.TryGetValue(element, out var existing))
                return wrap ? new InspectionProxy(this, existing, element) : element;

            var boundary = element as InspectionBoundary;
            var id = boundary == null ? path : "id:" + Uri.EscapeDataString(boundary.Id);
            if (boundary != null)
            {
                if (ExplicitPaths.TryGetValue(id, out var previous) && previous != path)
                    throw new InvalidOperationException("Duplicate preview inspection ID: " + boundary.Id);
                ExplicitPaths[id] = path;
            }
            if (!Nodes.TryGetValue(id, out var node))
            {
                if (Nodes.Count >= MaxElements)
                    throw new InvalidOperationException("Preview inspection exceeds the element limit.");
                node = new InspectionElement
                {
                    Id = id, ParentId = parent?.Id, Type = element.GetType().Name,
                    Label = boundary?.Label, Source = boundary?.Source ?? parent?.Source,
                    Configuration = ReadConfiguration(element)
                };
                Nodes.Add(id, node);
                Depths.Add(id, hierarchyDepth);
                Reserve(512 + 6 * (id.Length + (node.ParentId?.Length ?? 0) +
                    (node.Source?.File.Length ?? 0) + (node.Source?.Member.Length ?? 0) +
                    (node.Label?.Length ?? 0) + node.Configuration.Sum(x => x.Key.Length + x.Value.Length)));
                Snapshot.Elements.Add(node);
            }
            Installed.Add(element, node);
            var index = 0;
            // Some typed containers expose items through GetChildren but proxy the item's child.
            // Register the actual hierarchy first, then wrap the replaceable drawing boundaries.
            foreach (var child in element.GetChildren().ToArray())
                if (child != null) Install(child, node, id + "/" + index++, depth + 1, false);
            element.CreateProxy(child =>
            {
                if (child == null || child is InspectionProxy) return child;
                if (Installed.TryGetValue(child, out var registered))
                    return new InspectionProxy(this, registered, child);
                return Install(child, node, id + "/" + index++, depth + 1, true);
            });
            return wrap ? new InspectionProxy(this, node, element) : element;
        }

        private static Dictionary<string, string> ReadConfiguration(Element element)
        {
            var result = new Dictionary<string, string>();
            // Only scalar public properties on library-owned elements; never serialize delegates,
            // native handles, arbitrary component objects, or the layout tree.
            foreach (var property in element.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         .OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (property.GetIndexParameters().Length != 0 || !property.CanRead ||
                    !(property.GetMethod?.IsPublic == true || property.GetMethod?.IsAssembly == true) ||
                    !(type.IsEnum || type == typeof(string) || type == typeof(bool) ||
                      type == typeof(int) || type == typeof(float) || type == typeof(double))) continue;
                var value = Convert.ToString(property.GetValue(element), CultureInfo.InvariantCulture);
                if (value != null) result[property.Name] = value.Substring(0, Math.Min(value.Length, 512));
                if (result.Count == 32) break;
            }
            return result;
        }

        private sealed class InspectionProxy : ElementProxy
        {
            private readonly InspectionCapture Capture;
            private readonly InspectionElement Node;
            internal InspectionProxy(InspectionCapture capture, InspectionElement node, Element child)
            { Capture = capture; Node = node; Child = child; }

            internal override SpacePlan Measure(Size availableSpace)
            {
                var previous = Capture.Current;
                var previousIndex = Capture.GeneratedIndex;
                Capture.Current = Node;
                Capture.GeneratedIndex = 0;
                try { return base.Measure(availableSpace); }
                finally { Capture.Current = previous; Capture.GeneratedIndex = previousIndex; }
            }

            internal override void Draw(Size availableSpace)
            {
                var previous = Capture.Current;
                var previousIndex = Capture.GeneratedIndex;
                Capture.Current = Node;
                Capture.GeneratedIndex = 0;
                try
                {
                    // FreeCanvas and speculative multi-column canvases must never emit occurrences.
                    if (ActualCanvas(Canvas) is { } canvas && ReferenceEquals(canvas.Inspection, Capture))
                        Record(canvas, availableSpace);
                    base.Draw(availableSpace);
                }
                finally { Capture.Current = previous; Capture.GeneratedIndex = previousIndex; }
            }

            private void Record(SkiaPictureCanvas canvas, Size size)
            {
                if (Capture.Snapshot.Occurrences.Count >= Capture.MaxOccurrences)
                    throw new InvalidOperationException("Preview inspection exceeds the occurrence limit.");
                var matrix = Canvas.Transform;
                Capture.Reserve(1024 + Node.Id.Length * 6);
                var corners = new[] { Vector2.Zero, new Vector2(size.Width, 0),
                    new Vector2(0, size.Height), new Vector2(size.Width, size.Height) }
                    .Select(point => Vector2.Transform(point, matrix)).ToArray();
                Capture.Snapshot.Occurrences.Add(new InspectionOccurrence
                {
                    ElementId = Node.Id, PageNumber = canvas.Pictures.Count + 1,
                    X = corners.Min(x => x.X), Y = corners.Min(x => x.Y),
                    Width = corners.Max(x => x.X) - corners.Min(x => x.X),
                    Height = corners.Max(x => x.Y) - corners.Min(x => x.Y),
                    LocalWidth = size.Width, LocalHeight = size.Height,
                    Transform = new[] { matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.M41, matrix.M42 }
                });
            }
        }
    }
}
