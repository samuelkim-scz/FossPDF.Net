using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FossPDF.Drawing;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace FossPDF.PdfOperations
{
    /// <summary>
    /// Assembles the paint batches captured by the core package into one tagged PDF. Every batch is
    /// imported as a form XObject that is painted exactly once; marked content is written inside the
    /// form stream and the form's <c>/StructParents</c> entry maps its MCID to the logical structure
    /// element. Page annotations are copied to the destination page and, when they belong to a
    /// semantic element, associated with it through an object reference.
    /// </summary>
    internal static class TaggedPdfAssembly
    {
        private const double GeometryTolerance = 0.5;

        public static byte[] Assemble(TaggedDocumentContent content)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            if (content.PageCount <= 0 || content.Pages.Count == 0)
                throw new PdfOperationException("A tagged PDF requires at least one page.");

            var fragments = new List<FragmentLease>();

            try
            {
                using var destination = new PdfDocument();

                ApplyMetadata(destination, content.Metadata);

                var elements = CreateStructureElements(destination, content.Root);
                var pages = CreatePages(destination, content.Pages);

                var parentTree = new SortedDictionary<int, PdfItem>();
                var structureEntries = new Dictionary<string, List<StructureEntry>>(StringComparer.Ordinal);
                var namedDestinations = new Dictionary<string, PdfItem>(StringComparer.Ordinal);
                var nextParentTreeKey = 0;

                for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
                {
                    var page = pages[pageIndex];

                    foreach (var batch in page.Source.Batches.OrderBy(x => x.Order))
                    {
                        var lease = FragmentLease.Open(batch);
                        fragments.Add(lease);

                        var pageReference = PdfInternals.GetReference(page.DocumentPage)
                            ?? throw new PdfOperationException("A destination page could not be registered.");

                        AppendBatch(
                            destination,
                            page,
                            pageReference,
                            lease,
                            batch,
                            elements,
                            structureEntries,
                            parentTree,
                            namedDestinations,
                            ref nextParentTreeKey);
                    }

                    page.Commit();
                }

                ApplyStructure(destination, content.Root, elements, structureEntries);
                ApplyParentTree(
                    destination,
                    content.Root,
                    content.Metadata.Language,
                    elements,
                    parentTree,
                    nextParentTreeKey);
                ApplyNamedDestinations(destination, namedDestinations);

                using var output = new MemoryStream();
                destination.Save(output, false);

                return output.ToArray();
            }
            finally
            {
                foreach (var fragment in fragments)
                    fragment.Dispose();
            }
        }

        private static void ApplyMetadata(PdfDocument document, DocumentMetadata metadata)
        {
            SetIfNotEmpty(metadata.Title, value => document.Info.Title = value);
            SetIfNotEmpty(metadata.Author, value => document.Info.Author = value);
            SetIfNotEmpty(metadata.Subject, value => document.Info.Subject = value);
            SetIfNotEmpty(metadata.Keywords, value => document.Info.Keywords = value);
            SetIfNotEmpty(metadata.Creator, value => document.Info.Creator = value);
            SetIfNotEmpty(metadata.Producer, value => document.Info.Elements.SetString("/Producer", value));

            document.Info.CreationDate = metadata.CreationDate;
            document.Info.ModificationDate = metadata.ModifiedDate;
        }

        private static void SetIfNotEmpty(string? value, Action<string> setter)
        {
            if (!string.IsNullOrWhiteSpace(value))
                setter(value!);
        }

        private static List<PagePlan> CreatePages(PdfDocument document, IReadOnlyList<TaggedDocumentPage> sources)
        {
            var pages = new List<PagePlan>(sources.Count);

            foreach (var source in sources)
            {
                if (source.Width <= 0 || source.Height <= 0)
                    throw new PdfOperationException("Every page of a tagged PDF must have a positive size.");

                var page = document.AddPage();
                page.Width = XUnit.FromPoint(source.Width);
                page.Height = XUnit.FromPoint(source.Height);

                pages.Add(new PagePlan(document, page, source));
            }

            return pages;
        }

        private static void AppendBatch(
            PdfDocument destination,
            PagePlan page,
            PdfReference pageReference,
            FragmentLease lease,
            TaggedPaintBatch batch,
            IReadOnlyDictionary<string, StructureElement> elements,
            Dictionary<string, List<StructureEntry>> structureEntries,
            SortedDictionary<int, PdfItem> parentTree,
            Dictionary<string, PdfItem> namedDestinations,
            ref int nextParentTreeKey)
        {
            var fragment = lease.Document;

            if (fragment.PageCount != 1)
                throw new PdfOperationException($"Paint batch {batch.Order} does not contain exactly one page.");

            // Import the fragment page into the destination, then detach it from the page tree. The
            // imported page object remains a vehicle for the fragment's resources, content stream
            // and annotations; it is replaced by a form XObject.
            var imported = destination.AddPage(fragment.Pages[0]);
            destination.Pages.Remove(imported);

            VerifyPageSize(imported, page.Source, batch.Order);

            CopyNamedDestinations(
                destination,
                fragment,
                fragment.Pages[0],
                pageReference,
                namedDestinations);

            var element = batch.NodeId == null
                ? null
                : GetElement(elements, batch.NodeId);

            var annotations = ReadAnnotations(destination, imported);

            foreach (var annotation in annotations)
                annotation.Elements.SetReference("/P", pageReference);

            var content = ReadContent(imported);

            if (content.Length > 0)
            {
                int? structParents = null;

                if (element != null)
                {
                    structParents = nextParentTreeKey;
                    nextParentTreeKey++;
                }

                var form = CreateForm(
                    destination,
                    imported,
                    page.Source,
                    content,
                    element?.Role,
                    structParents);

                var formReference = PdfInternals.GetReference(form)
                    ?? throw new PdfOperationException("A tagged content form could not be registered.");

                page.AddForm(formReference);

                if (element != null)
                {
                    var array = new PdfArray(destination);
                    array.Elements.Add(element.Reference);
                    parentTree[structParents!.Value] = array;

                    AddStructureEntry(
                        structureEntries,
                        element,
                        new StructureEntry(batch.Order, CreateMarkedContentReference(destination, pageReference, formReference)));
                }
            }

            page.AddAnnotations(annotations);

            if (element == null)
                return;

            foreach (var annotation in annotations)
            {
                var annotationReference = PdfInternals.GetReference(annotation);

                if (annotationReference == null)
                {
                    destination.Internals.AddObject(annotation);
                    annotationReference = PdfInternals.GetReference(annotation);
                }

                if (annotationReference == null)
                    throw new PdfOperationException("An annotation could not be registered in the tagged PDF.");

                annotation.Elements.SetInteger("/StructParent", nextParentTreeKey);
                parentTree[nextParentTreeKey] = element.Reference;
                nextParentTreeKey++;

                AddStructureEntry(
                    structureEntries,
                    element,
                    new StructureEntry(batch.Order, CreateObjectReference(destination, pageReference, annotationReference)));
            }
        }

        private static void VerifyPageSize(PdfPage imported, TaggedDocumentPage source, int order)
        {
            var box = imported.MediaBox;

            if (Math.Abs(box.Width - source.Width) > GeometryTolerance ||
                Math.Abs(box.Height - source.Height) > GeometryTolerance)
            {
                throw new PdfOperationException(
                    $"Paint batch {order} was rendered at {box.Width:0.##}x{box.Height:0.##} pt " +
                    $"but its page is {source.Width:0.##}x{source.Height:0.##} pt.");
            }
        }

        private static StructureElement GetElement(
            IReadOnlyDictionary<string, StructureElement> elements,
            string id)
        {
            if (!elements.TryGetValue(id, out var element))
                throw new PdfOperationException($"Paint batch references unknown structure node '{id}'.");

            return element;
        }

        private static void AddStructureEntry(
            Dictionary<string, List<StructureEntry>> entries,
            StructureElement element,
            StructureEntry entry)
        {
            if (!entries.TryGetValue(element.Node.Id, out var list))
            {
                list = new List<StructureEntry>();
                entries.Add(element.Node.Id, list);
            }

            list.Add(entry);
        }

        private static PdfDictionary CreateForm(
            PdfDocument destination,
            PdfPage imported,
            TaggedDocumentPage page,
            byte[] content,
            string? role,
            int? structParents)
        {
            var form = new PdfDictionary(destination);
            form.Elements.SetName("/Type", "/XObject");
            form.Elements.SetName("/Subtype", "/Form");
            form.Elements.SetRectangle("/BBox", new PdfRectangle(new XRect(0, 0, page.Width, page.Height)));

            var resources = imported.Elements.GetDictionary("/Resources");

            if (resources != null)
                form.Elements.SetObject("/Resources", resources);

            var group = imported.Elements.GetDictionary("/Group");

            if (group != null)
                form.Elements.SetObject("/Group", group);

            if (structParents.HasValue)
                form.Elements.SetInteger("/StructParents", structParents.Value);

            var marked = role == null
                ? MarkArtifact(content)
                : MarkContent(content, role);

            form.CreateStream(marked);

            destination.Internals.AddObject(form);
            return form;
        }

        private static byte[] MarkContent(byte[] content, string role)
        {
            using var output = new MemoryStream();
            var prefix = Encoding.ASCII.GetBytes("/" + role + " << /MCID 0 >> BDC\n");
            var suffix = Encoding.ASCII.GetBytes("\nEMC\n");

            output.Write(prefix, 0, prefix.Length);
            output.Write(content, 0, content.Length);
            output.Write(suffix, 0, suffix.Length);

            return output.ToArray();
        }

        private static byte[] MarkArtifact(byte[] content)
        {
            using var output = new MemoryStream();
            var prefix = Encoding.ASCII.GetBytes("/Artifact BMC\n");
            var suffix = Encoding.ASCII.GetBytes("\nEMC\n");

            output.Write(prefix, 0, prefix.Length);
            output.Write(content, 0, content.Length);
            output.Write(suffix, 0, suffix.Length);

            return output.ToArray();
        }

        private static PdfDictionary CreateMarkedContentReference(
            PdfDocument destination,
            PdfReference pageReference,
            PdfReference formReference)
        {
            var reference = new PdfDictionary(destination);
            reference.Elements.SetName("/Type", "/MCR");
            reference.Elements.SetReference("/Pg", pageReference);
            reference.Elements.SetReference("/Stm", formReference);
            reference.Elements.SetInteger("/MCID", 0);

            return reference;
        }

        private static PdfDictionary CreateObjectReference(
            PdfDocument destination,
            PdfReference pageReference,
            PdfReference objectReference)
        {
            var reference = new PdfDictionary(destination);
            reference.Elements.SetName("/Type", "/OBJR");
            reference.Elements.SetReference("/Pg", pageReference);
            reference.Elements.SetReference("/Obj", objectReference);

            return reference;
        }

        private static List<PdfDictionary> ReadAnnotations(PdfDocument destination, PdfPage imported)
        {
            var annotations = new List<PdfDictionary>();
            var array = imported.Elements.GetArray("/Annots");

            if (array == null)
                return annotations;

            // Detach the annotations from the imported page before it becomes unreachable.
            imported.Elements.Remove("/Annots");

            foreach (var item in array.Elements)
            {
                var annotation = item switch
                {
                    PdfReference reference => reference.Value as PdfDictionary,
                    PdfDictionary dictionary => dictionary,
                    _ => null
                };

                if (annotation == null)
                    throw new PdfOperationException("An annotation in a paint batch is not a dictionary.");

                annotations.Add(annotation);
            }

            return annotations;
        }

        private static byte[] ReadContent(PdfPage page)
        {
            using var output = new MemoryStream();

            foreach (var item in page.Contents.Elements)
            {
                var dictionary = item switch
                {
                    PdfReference reference => reference.Value as PdfDictionary,
                    PdfDictionary value => value,
                    _ => null
                };

                var stream = dictionary?.Stream;

                if (stream == null)
                    continue;

                if (stream.IsFiltered() && !stream.TryUncompress())
                    throw new PdfOperationException("A paint batch uses a content stream filter that is not supported.");

                var bytes = stream.UnfilteredValue;

                if (bytes == null || bytes.Length == 0)
                    continue;

                output.Write(bytes, 0, bytes.Length);
                output.WriteByte((byte)'\n');
            }

            return output.ToArray();
        }

        private static void CopyNamedDestinations(
            PdfDocument destination,
            PdfDocument fragment,
            PdfPage imported,
            PdfReference pageReference,
            Dictionary<string, PdfItem> destinations)
        {
            var catalog = fragment.Internals.Catalog;
            var source = catalog?.Elements.GetDictionary("/Dests")
                         ?? catalog?.Elements.GetDictionary("/Names")?.Elements.GetDictionary("/Dests");

            if (source == null)
                return;

            var names = source.Elements.GetArray("/Names");

            if (names != null)
            {
                for (var index = 0; index + 1 < names.Elements.Count; index += 2)
                {
                    var name = names.Elements[index] as PdfName
                        ?? throw new PdfOperationException("A named destination uses an unsupported name tree key.");

                    var key = NormalizeName(name.ToString());
                    var value = RewriteDestination(destination, imported, pageReference, names.Elements[index + 1]);

                    if (!destinations.ContainsKey(key))
                        destinations.Add(key, value);
                }

                return;
            }

            // Skia writes the legacy flat /Dests dictionary: one entry per name.
            foreach (var entry in source.Elements)
            {
                if (destinations.ContainsKey(entry.Key))
                    continue;

                destinations.Add(entry.Key, RewriteDestination(destination, imported, pageReference, entry.Value!));
            }
        }

        private static string NormalizeName(string value)
        {
            return value.StartsWith("/", StringComparison.Ordinal) ? value : "/" + value;
        }

        private static PdfItem RewriteDestination(
            PdfDocument destination,
            PdfPage imported,
            PdfReference pageReference,
            PdfItem value)
        {
            var source = value as PdfArray
                ?? throw new PdfOperationException("A named destination uses an unsupported value.");

            if (source.Elements.Count == 0)
                throw new PdfOperationException("A named destination has an empty destination array.");

            var array = new PdfArray(destination);

            for (var index = 0; index < source.Elements.Count; index++)
            {
                var item = source.Elements[index];

                if (index == 0)
                {
                    if (!ReferencesPage(item, imported))
                        throw new PdfOperationException("A named destination does not point at its own page.");

                    array.Elements.Add(pageReference);
                    continue;
                }

                switch (item)
                {
                    case PdfName name:
                        array.Elements.Add(new PdfName(name.ToString()));
                        break;

                    case PdfInteger integer:
                        array.Elements.Add(new PdfInteger(integer.Value));
                        break;

                    case PdfReal real:
                        array.Elements.Add(new PdfReal(real.Value));
                        break;

                    default:
                        throw new PdfOperationException(
                            "A named destination uses an unsupported destination type.");
                }
            }

            return array;
        }

        private static bool ReferencesPage(PdfItem item, PdfPage page)
        {
            return item switch
            {
                PdfReference reference => ReferenceEquals(reference.Value, page),
                PdfDictionary dictionary => ReferenceEquals(dictionary, page),
                _ => false
            };
        }

        private static Dictionary<string, StructureElement> CreateStructureElements(
            PdfDocument destination,
            TaggedStructureNode root)
        {
            var elements = new Dictionary<string, StructureElement>(StringComparer.Ordinal);

            Create(root, null);

            return elements;

            void Create(TaggedStructureNode node, StructureElement? parent)
            {
                ValidateRole(node);

                if (elements.ContainsKey(node.Id))
                    throw new PdfOperationException($"The structure tree contains duplicate node id '{node.Id}'.");

                var dictionary = new PdfDictionary(destination);
                dictionary.Elements.SetName("/Type", "/StructElem");
                dictionary.Elements.SetName("/S", "/" + node.Role);

                if (parent != null)
                    dictionary.Elements.SetReference("/P", parent.Reference);

                if (!string.IsNullOrEmpty(node.AlternativeText))
                    dictionary.Elements.SetValue("/Alt", ToText(node.AlternativeText!));

                if (!string.IsNullOrWhiteSpace(node.Language))
                    dictionary.Elements.SetString("/Lang", node.Language!);

                destination.Internals.AddObject(dictionary);

                var reference = PdfInternals.GetReference(dictionary)
                    ?? throw new PdfOperationException($"Structure node '{node.Id}' could not be registered.");

                var element = new StructureElement(node, reference);
                elements.Add(node.Id, element);

                foreach (var child in node.Children)
                    Create(child, element);
            }
        }

        private static void ValidateRole(TaggedStructureNode node)
        {
            if (string.IsNullOrWhiteSpace(node.Role))
                throw new PdfOperationException($"Structure node '{node.Id}' has an empty role.");

            foreach (var character in node.Role)
            {
                if (char.IsWhiteSpace(character) || "()<>[]{}/%#".IndexOf(character) >= 0)
                {
                    throw new PdfOperationException(
                        $"Structure node '{node.Id}' has an invalid role name '{node.Role}'.");
                }
            }
        }

        private static void ApplyStructure(
            PdfDocument destination,
            TaggedStructureNode root,
            IReadOnlyDictionary<string, StructureElement> elements,
            Dictionary<string, List<StructureEntry>> entries)
        {
            Assign(root);
            return;

            void Assign(TaggedStructureNode node)
            {
                var element = elements[node.Id];
                var items = new List<OrderedItem>();

                if (entries.TryGetValue(node.Id, out var own))
                {
                    foreach (var entry in own)
                        items.Add(new OrderedItem(entry.Order, items.Count, entry.Item));
                }

                var sequence = 1_000_000;

                foreach (var child in node.Children)
                {
                    items.Add(new OrderedItem(child.Order, sequence, elements[child.Id].Reference));
                    sequence++;
                }

                if (items.Count == 0)
                {
                    element.Dictionary.Elements.Remove("/K");
                }
                else if (items.Count == 1)
                {
                    element.Dictionary.Elements.SetValue("/K", items[0].Item);
                }
                else
                {
                    var array = new PdfArray(destination);

                    foreach (var item in items.OrderBy(x => x.Order).ThenBy(x => x.Sequence))
                        array.Elements.Add(item.Item);

                    element.Dictionary.Elements.SetValue("/K", array);
                }

                foreach (var child in node.Children)
                    Assign(child);
            }
        }

        private static void ApplyParentTree(
            PdfDocument destination,
            TaggedStructureNode root,
            string? language,
            IReadOnlyDictionary<string, StructureElement> elements,
            SortedDictionary<int, PdfItem> parentTree,
            int nextParentTreeKey)
        {
            var tree = new PdfDictionary(destination);
            tree.Elements.SetName("/Type", "/ParentTree");

            var numbers = new PdfArray(destination);

            foreach (var entry in parentTree)
            {
                numbers.Elements.Add(new PdfInteger(entry.Key));
                numbers.Elements.Add(entry.Value);
            }

            tree.Elements.SetValue("/Nums", numbers);

            destination.Internals.AddObject(tree);

            var treeReference = PdfInternals.GetReference(tree)
                ?? throw new PdfOperationException("The parent tree could not be registered.");

            var rootElement = elements[root.Id];

            var structTreeRoot = new PdfDictionary(destination);
            structTreeRoot.Elements.SetName("/Type", "/StructTreeRoot");
            structTreeRoot.Elements.SetValue("/K", rootElement.Reference);
            structTreeRoot.Elements.SetReference("/ParentTree", treeReference);
            structTreeRoot.Elements.SetInteger("/ParentTreeNextKey", nextParentTreeKey);

            destination.Internals.AddObject(structTreeRoot);

            var catalog = destination.Internals.Catalog
                ?? throw new PdfOperationException("The output PDF does not expose a document catalog.");

            catalog.Elements.SetReference("/StructTreeRoot", structTreeRoot);

            var markInfo = new PdfDictionary(destination);
            markInfo.Elements.SetBoolean("/Marked", true);
            catalog.Elements.SetObject("/MarkInfo", markInfo);

            var preferences = new PdfDictionary(destination);
            preferences.Elements.SetBoolean("/DisplayDocTitle", true);
            catalog.Elements.SetObject("/ViewerPreferences", preferences);

            if (!string.IsNullOrWhiteSpace(language))
                catalog.Elements.SetString("/Lang", language!);
        }

        private static void ApplyNamedDestinations(
            PdfDocument destination,
            Dictionary<string, PdfItem> destinations)
        {
            if (destinations.Count == 0)
                return;

            var catalog = destination.Internals.Catalog
                ?? throw new PdfOperationException("The output PDF does not expose a document catalog.");

            var dictionary = new PdfDictionary(destination);

            foreach (var entry in destinations.OrderBy(x => x.Key, StringComparer.Ordinal))
                dictionary.Elements.SetValue(entry.Key, entry.Value);

            catalog.Elements.SetObject("/Dests", dictionary);
        }

        private static PdfString ToText(string value)
        {
            var isAscii = value.All(character => character <= 0x7F);

            return isAscii
                ? new PdfString(value)
                : new PdfString(value, PdfStringEncoding.Unicode);
        }

        private sealed class FragmentLease : IDisposable
        {
            private readonly MemoryStream _buffer;

            private FragmentLease(MemoryStream buffer, PdfDocument document)
            {
                _buffer = buffer;
                Document = document;
            }

            public PdfDocument Document { get; }

            public static FragmentLease Open(TaggedPaintBatch batch)
            {
                var buffer = new MemoryStream(batch.Fragment, writable: false);
                PdfDocument document;

                try
                {
                    document = PdfReader.Open(buffer, PdfDocumentOpenMode.Import);
                }
                catch (Exception exception) when (!(exception is PdfOperationException))
                {
                    throw new PdfOperationException(
                        $"Paint batch {batch.Order} could not be opened as a PDF fragment. " +
                        "The captured content may be corrupted.", exception);
                }

                return new FragmentLease(buffer, document);
            }

            public void Dispose()
            {
                Document.Dispose();
                _buffer.Dispose();
            }
        }

        private sealed class StructureElement
        {
            public StructureElement(TaggedStructureNode node, PdfReference reference)
            {
                Node = node;
                Reference = reference;
            }

            public TaggedStructureNode Node { get; }
            public PdfReference Reference { get; }
            public PdfDictionary Dictionary => (PdfDictionary)Reference.Value!;
            public string Role => Node.Role;
        }

        private sealed class StructureEntry
        {
            public StructureEntry(int order, PdfItem item)
            {
                Order = order;
                Item = item;
            }

            public int Order { get; }
            public PdfItem Item { get; }
        }

        private readonly struct OrderedItem
        {
            public OrderedItem(int order, int sequence, PdfItem item)
            {
                Order = order;
                Sequence = sequence;
                Item = item;
            }

            public int Order { get; }
            public int Sequence { get; }
            public PdfItem Item { get; }
        }

        private sealed class PagePlan
        {
            private readonly PdfDocument _document;
            private readonly List<KeyValuePair<string, PdfReference>> _forms = new List<KeyValuePair<string, PdfReference>>();
            private readonly List<PdfDictionary> _annotations = new List<PdfDictionary>();
            private readonly StringBuilder _content = new StringBuilder();
            private int _formIndex;

            public PagePlan(PdfDocument document, PdfPage documentPage, TaggedDocumentPage source)
            {
                _document = document;
                DocumentPage = documentPage;
                Source = source;
            }

            public PdfPage DocumentPage { get; }
            public TaggedDocumentPage Source { get; }

            public void AddForm(PdfReference form)
            {
                var name = "/T" + _formIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                _formIndex++;

                _forms.Add(new KeyValuePair<string, PdfReference>(name, form));

                _content.Append("q\n1 0 0 1 0 0 cm\n");
                _content.Append(name);
                _content.Append(" Do\nQ\n");
            }

            public void AddAnnotations(IEnumerable<PdfDictionary> annotations)
            {
                _annotations.AddRange(annotations);
            }

            public void Commit()
            {
                if (_forms.Count > 0)
                {
                    var resources = new PdfDictionary(_document);
                    var xobjects = new PdfDictionary(_document);

                    foreach (var form in _forms)
                        xobjects.Elements.SetReference(form.Key, form.Value);

                    resources.Elements.SetObject("/XObject", xobjects);
                    DocumentPage.Elements.SetObject("/Resources", resources);

                    var stream = new PdfDictionary(_document);
                    stream.CreateStream(Encoding.ASCII.GetBytes(_content.ToString()));
                    _document.Internals.AddObject(stream);

                    var reference = PdfInternals.GetReference(stream)
                        ?? throw new PdfOperationException("A page content stream could not be registered.");

                    DocumentPage.Contents.Elements.Add(reference);
                }

                if (_annotations.Count > 0)
                {
                    var array = new PdfArray(_document);

                    foreach (var annotation in _annotations)
                    {
                        var reference = PdfInternals.GetReference(annotation);

                        if (reference == null)
                            throw new PdfOperationException("An annotation could not be registered.");

                        array.Elements.Add(reference);
                    }

                    DocumentPage.Elements.SetValue("/Annots", array);
                }
            }
        }
    }
}
