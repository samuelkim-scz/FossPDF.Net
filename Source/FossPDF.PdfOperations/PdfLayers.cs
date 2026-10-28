using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FossPDF.Fluent;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace FossPDF.PdfOperations
{
    /// <summary>
    /// Applies overlay and underlay files to the pages of an existing-PDF operation. A layer page is
    /// imported as a form XObject, so the original content streams, resources, fonts and
    /// transparency groups stay vector data instead of being rasterized.
    /// </summary>
    internal static class PdfLayers
    {
        private const string AllPages = "1-z";

        public static void Apply(
            PdfDocument destination,
            IReadOnlyList<DocumentOperation.LayerOperation> underlays,
            IReadOnlyList<DocumentOperation.LayerOperation> overlays)
        {
            if (underlays.Count == 0 && overlays.Count == 0)
                return;

            var opened = new List<PdfDocument>(underlays.Count + overlays.Count);
            // PDFsharp reads from the stream while the layer pages are imported, so the streams must
            // stay alive for as long as the layer documents are open.
            var buffers = new List<MemoryStream>(underlays.Count + overlays.Count);

            try
            {
                var underlayPlans = new List<LayerPlan>(underlays.Count);
                foreach (var layer in underlays)
                    underlayPlans.Add(LayerPlan.Create(destination, layer, opened, buffers));

                var overlayPlans = new List<LayerPlan>(overlays.Count);
                foreach (var layer in overlays)
                    overlayPlans.Add(LayerPlan.Create(destination, layer, opened, buffers));

                // Importing a layer page temporarily appends it to the page collection and removes
                // it again, so the output page count stays fixed during the whole pass.
                var pageCount = destination.PageCount;

                for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    var writer = new PageLayerWriter(destination, destination.Pages[pageIndex]);

                    foreach (var plan in underlayPlans)
                        plan.AppendContent(destination, pageIndex, writer, isUnderlay: true);

                    foreach (var plan in overlayPlans)
                        plan.AppendContent(destination, pageIndex, writer, isUnderlay: false);

                    writer.Commit();
                }
            }
            finally
            {
                foreach (var document in opened)
                    document.Dispose();

                foreach (var buffer in buffers)
                    buffer.Dispose();
            }
        }

        /// <summary>
        /// Computes the content-stream matrix that places a layer page inside a destination page.
        /// The source page is shown as its own rotation makes it appear; the destination rotation is
        /// inverted so the layer stays upright for the reader. The layer is scaled uniformly to fit
        /// and is centered inside the destination trim box.
        /// </summary>
        /// <returns>The six matrix values in PDF <c>a b c d e f</c> order.</returns>
        internal static double[] ComputePlacementMatrix(
            PdfRectangle destinationBox,
            int destinationRotation,
            PdfRectangle sourceBox,
            int sourceRotation)
        {
            var sourceWidth = IsQuarterTurn(sourceRotation) ? sourceBox.Height : sourceBox.Width;
            var sourceHeight = IsQuarterTurn(sourceRotation) ? sourceBox.Width : sourceBox.Height;
            var destinationWidth = IsQuarterTurn(destinationRotation) ? destinationBox.Height : destinationBox.Width;
            var destinationHeight = IsQuarterTurn(destinationRotation) ? destinationBox.Width : destinationBox.Height;

            if (sourceWidth <= 0 || sourceHeight <= 0)
                throw new PdfOperationException("An overlay or underlay page has an empty page box.");

            var scale = Math.Min(destinationWidth / sourceWidth, destinationHeight / sourceHeight);
            var offsetX = (destinationWidth - sourceWidth * scale) / 2.0;
            var offsetY = (destinationHeight - sourceHeight * scale) / 2.0;

            var left = destinationBox.X1;
            var bottom = destinationBox.Y1;
            var right = destinationBox.X2;
            var top = destinationBox.Y2;

            switch (destinationRotation)
            {
                case 90:
                    // Display (x, y) maps back to content (right - y, x + bottom).
                    return new[]
                    {
                        0, scale, -scale, 0,
                        right - offsetY,
                        offsetX + bottom
                    };

                case 180:
                    // Display (x, y) maps back to content (right - x, top - y).
                    return new[]
                    {
                        -scale, 0, 0, -scale,
                        right - offsetX,
                        top - offsetY
                    };

                case 270:
                    // Display (x, y) maps back to content (y + left, top - x).
                    return new[]
                    {
                        0, -scale, scale, 0,
                        offsetY + left,
                        top - offsetX
                    };

                default:
                    return new[]
                    {
                        scale, 0, 0, scale,
                        offsetX + left,
                        offsetY + bottom
                    };
            }
        }

        private static bool IsQuarterTurn(int rotation)
        {
            return rotation == 90 || rotation == 270;
        }

        private static int NormalizeRotation(int rotation)
        {
            var normalized = rotation % 360;

            if (normalized < 0)
                normalized += 360;

            return normalized == 90 || normalized == 180 || normalized == 270 ? normalized : 0;
        }

        private static double[]? GetFormMatrix(PdfRectangle box, int rotation)
        {
            if (rotation == 0 && box.X1 == 0 && box.Y1 == 0)
                return null;

            var left = box.X1;
            var bottom = box.Y1;
            var right = box.X2;
            var top = box.Y2;

            switch (rotation)
            {
                case 90:
                    // (x, y) -> (y - bottom, right - x)
                    return new[] { 0, -1, 1, 0, -bottom, right };

                case 180:
                    // (x, y) -> (right - x, top - y)
                    return new[] { -1, 0, 0, -1, right, top };

                case 270:
                    // (x, y) -> (top - y, x - left)
                    return new[] { 0, 1, -1, 0, top, -left };

                default:
                    return new[] { 1, 0, 0, 1, -left, -bottom };
            }
        }

        private static PdfRectangle GetVisibleBox(PdfPage page, string name)
        {
            // Trim box, then crop box, then media box, as defined by the PDF specification.
            var box = page.EffectiveTrimBoxReadOnly;

            if (box.IsZero || box.Width <= 0 || box.Height <= 0)
                throw new PdfOperationException($"'{name}' contains a page without a usable page box.");

            return box;
        }

        private static PdfDictionary? ResolveDictionary(PdfItem item)
        {
            return item switch
            {
                PdfReference reference => reference.Value as PdfDictionary,
                PdfDictionary dictionary => dictionary,
                _ => null
            };
        }

        private static byte[] ReadContent(PdfPage page)
        {
            using var output = new MemoryStream();

            foreach (var item in page.Contents.Elements)
            {
                var dictionary = ResolveDictionary(item);
                var stream = dictionary?.Stream;

                if (stream == null)
                    continue;

                if (stream.IsFiltered() && !stream.TryUncompress())
                    throw new PdfOperationException("A layer page uses a content stream filter that is not supported.");

                var bytes = stream.UnfilteredValue;

                if (bytes == null || bytes.Length == 0)
                    continue;

                output.Write(bytes, 0, bytes.Length);
                output.WriteByte((byte)'\n');
            }

            return output.ToArray();
        }

        private static PdfArray CreateArray(PdfDocument document, double[] values)
        {
            var items = new PdfItem[values.Length];

            for (var index = 0; index < values.Length; index++)
                items[index] = new PdfReal(values[index]);

            return new PdfArray(document, items);
        }

        private static string Format(double value)
        {
            if (Math.Abs(value) < 0.0000000005)
                value = 0;

            return value.ToString("0.#########", CultureInfo.InvariantCulture);
        }

        private sealed class LayerPlan
        {
            private readonly DocumentOperation.LayerOperation _layer;
            private readonly PdfDocument _document;
            private readonly Dictionary<int, List<int>> _targets = new Dictionary<int, List<int>>();
            private readonly Dictionary<int, PdfDictionary> _forms = new Dictionary<int, PdfDictionary>();

            private LayerPlan(DocumentOperation.LayerOperation layer, PdfDocument document)
            {
                _layer = layer;
                _document = document;
            }

            public static LayerPlan Create(
                PdfDocument destination,
                DocumentOperation.LayerOperation layer,
                List<PdfDocument> opened,
                List<MemoryStream> buffers)
            {
                var buffer = new MemoryStream(layer.Data, writable: false);
                buffers.Add(buffer);

                PdfDocument document;

                try
                {
                    document = PdfReader.Open(buffer, PdfDocumentOpenMode.Import);
                }
                catch (Exception exception) when (!(exception is PdfOperationException))
                {
                    throw new PdfOperationException(
                        $"The overlay or underlay file '{layer.Name}' could not be opened. It may be malformed, truncated or protected.", exception);
                }

                opened.Add(document);

                var plan = new LayerPlan(layer, document);
                plan.Resolve(destination.PageCount);
                return plan;
            }

            public void AppendContent(
                PdfDocument destination,
                int destinationPageIndex,
                PageLayerWriter writer,
                bool isUnderlay)
            {
                if (!_targets.TryGetValue(destinationPageIndex, out var sourcePages))
                    return;

                var destinationPage = destination.Pages[destinationPageIndex];
                var destinationBox = GetVisibleBox(destinationPage, _layer.Name);
                var destinationRotation = NormalizeRotation(destinationPage.Rotate);

                foreach (var sourcePageIndex in sourcePages)
                {
                    var sourcePage = _document.Pages[sourcePageIndex];
                    var sourceBox = GetVisibleBox(sourcePage, _layer.Name);
                    var sourceRotation = NormalizeRotation(sourcePage.Rotate);
                    var matrix = ComputePlacementMatrix(
                        destinationBox,
                        destinationRotation,
                        sourceBox,
                        sourceRotation);

                    var form = GetForm(destination, sourcePageIndex, sourcePage, sourceBox, sourceRotation);
                    writer.Add(form, matrix, isUnderlay);
                }
            }

            private void Resolve(int destinationPageCount)
            {
                var targetPages = ResolveSelector(
                    _layer.TargetPages ?? AllPages,
                    destinationPageCount,
                    $"{_layer.Name} target pages");

                var repeatPages = _layer.RepeatSourcePages == null
                    ? (IReadOnlyList<int>)Array.Empty<int>()
                    : ResolveSelector(
                        _layer.RepeatSourcePages,
                        _document.PageCount,
                        $"{_layer.Name} repeat pages");

                IReadOnlyList<int> sourcePages;

                if (_layer.SourcePages != null)
                {
                    sourcePages = ResolveSelector(
                        _layer.SourcePages,
                        _document.PageCount,
                        $"{_layer.Name} source pages");
                }
                else if (repeatPages.Count > 0)
                {
                    // Matching the pinned behavior: without an initial range, repetition starts
                    // with the first target page.
                    sourcePages = repeatPages;
                }
                else
                {
                    sourcePages = ResolveSelector(
                        AllPages,
                        _document.PageCount,
                        $"{_layer.Name} source pages");
                }

                for (var index = 0; index < targetPages.Count; index++)
                {
                    int sourcePage;

                    if (index < sourcePages.Count)
                    {
                        sourcePage = sourcePages[index];
                    }
                    else if (repeatPages.Count > 0)
                    {
                        sourcePage = repeatPages[(index - sourcePages.Count) % repeatPages.Count];
                    }
                    else
                    {
                        continue;
                    }

                    if (!_targets.TryGetValue(targetPages[index], out var list))
                    {
                        list = new List<int>();
                        _targets.Add(targetPages[index], list);
                    }

                    list.Add(sourcePage);
                }
            }

            private static IReadOnlyList<int> ResolveSelector(string selector, int pageCount, string name)
            {
                return PageRangeSelector.Resolve(selector, pageCount, name);
            }

            private PdfDictionary GetForm(
                PdfDocument destination,
                int sourcePageIndex,
                PdfPage sourcePage,
                PdfRectangle sourceBox,
                int sourceRotation)
            {
                if (_forms.TryGetValue(sourcePageIndex, out var form))
                    return form;

                form = CreateForm(destination, sourcePage, sourceBox, sourceRotation);
                _forms.Add(sourcePageIndex, form);
                return form;
            }

            private PdfDictionary CreateForm(
                PdfDocument destination,
                PdfPage sourcePage,
                PdfRectangle sourceBox,
                int sourceRotation)
            {
                // Import the page into the destination document, then detach it from the page tree.
                // The imported page object itself becomes unreachable; its resources and the copied
                // content remain reachable through the form XObject.
                var imported = destination.AddPage(sourcePage);
                destination.Pages.Remove(imported);

                var form = new PdfDictionary(destination);
                form.Elements.SetName("/Type", "/XObject");
                form.Elements.SetName("/Subtype", "/Form");
                form.Elements.SetRectangle("/BBox", sourceBox);

                if (imported.Elements.ContainsKey("/Resources"))
                {
                    var resources = imported.Elements.GetDictionary("/Resources");

                    if (resources != null)
                        form.Elements.SetObject("/Resources", resources);
                }

                if (imported.Elements.ContainsKey("/Group"))
                {
                    var group = imported.Elements.GetDictionary("/Group");

                    if (group != null)
                        form.Elements.SetObject("/Group", group);
                }

                var matrix = GetFormMatrix(sourceBox, sourceRotation);

                if (matrix != null)
                    form.Elements.SetValue("/Matrix", CreateArray(destination, matrix));

                var content = ReadContent(imported);
                form.CreateStream(content);
                form.Stream.Zip();

                destination.Internals.AddObject(form);
                return form;
            }
        }

        private sealed class PageLayerWriter
        {
            private readonly PdfDocument _document;
            private readonly PdfPage _page;
            private readonly List<KeyValuePair<string, PdfReference>> _forms = new List<KeyValuePair<string, PdfReference>>();
            private readonly List<string> _underlays = new List<string>();
            private readonly List<string> _overlays = new List<string>();
            private readonly HashSet<string> _names = new HashSet<string>();
            private int _nameIndex;

            public PageLayerWriter(PdfDocument document, PdfPage page)
            {
                _document = document;
                _page = page;
            }

            public void Add(PdfDictionary form, double[] matrix, bool isUnderlay)
            {
                var name = CreateName();

                var reference = PdfInternals.GetReference(form);

                if (reference == null)
                    throw new PdfOperationException("An overlay or underlay page could not be registered.");

                _forms.Add(new KeyValuePair<string, PdfReference>(name, reference));

                var content = BuildContent(name, matrix);

                if (isUnderlay)
                    _underlays.Add(content);
                else
                    _overlays.Add(content);
            }

            private string CreateName()
            {
                var resources = _page.Elements.ContainsKey("/Resources")
                    ? _page.Elements.GetDictionary("/Resources")
                    : null;
                var xobjects = resources != null && resources.Elements.ContainsKey("/XObject")
                    ? resources.Elements.GetDictionary("/XObject")
                    : null;

                // Never reuse a resource name that the page already defines; the page's own
                // content may reference it.
                while (true)
                {
                    var name = "/L" + _nameIndex.ToString(CultureInfo.InvariantCulture);
                    _nameIndex++;

                    if (_names.Contains(name))
                        continue;

                    if (xobjects != null && xobjects.Elements.ContainsKey(name))
                        continue;

                    _names.Add(name);
                    return name;
                }
            }

            public void Commit()
            {
                if (_forms.Count == 0)
                    return;

                var contents = _page.Contents.Elements;
                var index = 0;

                // Underlays are inserted in registration order, so the first underlay is the
                // bottom-most layer. Overlays are appended in registration order.
                foreach (var content in _underlays)
                    contents.Insert(index++, CreateContentStream(content));

                foreach (var content in _overlays)
                    contents.Add(CreateContentStream(content));

                AddResources();
            }

            private PdfReference CreateContentStream(string content)
            {
                var dictionary = new PdfDictionary(_document);
                dictionary.CreateStream(Encoding.ASCII.GetBytes(content));
                _document.Internals.AddObject(dictionary);

                var reference = PdfInternals.GetReference(dictionary);

                if (reference == null)
                    throw new PdfOperationException("An overlay or underlay content stream could not be registered.");

                return reference;
            }

            private void AddResources()
            {
                var resources = _page.Elements.ContainsKey("/Resources")
                    ? _page.Elements.GetDictionary("/Resources")
                    : null;

                // Clone the dictionaries so a resource dictionary shared by several imported pages
                // does not leak the layer XObjects onto pages the layer does not target.
                var resourcesCopy = resources?.Clone() ?? new PdfDictionary(_document);
                var xobjects = resourcesCopy.Elements.ContainsKey("/XObject")
                    ? resourcesCopy.Elements.GetDictionary("/XObject")
                    : null;
                var xobjectsCopy = xobjects?.Clone() ?? new PdfDictionary(_document);

                foreach (var form in _forms)
                    xobjectsCopy.Elements.SetReference(form.Key, form.Value);

                resourcesCopy.Elements.SetObject("/XObject", xobjectsCopy);
                _page.Elements.SetObject("/Resources", resourcesCopy);
            }

            private static string BuildContent(string name, double[] matrix)
            {
                var builder = new StringBuilder();
                builder.Append("q\n");

                for (var index = 0; index < matrix.Length; index++)
                {
                    if (index > 0)
                        builder.Append(' ');

                    builder.Append(Format(matrix[index]));
                }

                builder.Append(" cm\n");
                builder.Append(name);
                builder.Append(" Do\nQ\n");
                return builder.ToString();
            }
        }
    }
}
