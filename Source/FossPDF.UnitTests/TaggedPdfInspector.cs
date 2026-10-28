#if NET9_0
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace FossPDF.UnitTests
{
    /// <summary>
    /// Parsed view of a tagged PDF: structure tree, marked-content references, object references,
    /// parent tree and page forms. Shared by the feature tests and the ACC-401 parity runner.
    /// </summary>
    internal sealed class TaggedPdfInspector : IDisposable
    {
        private TaggedPdfInspector(PdfDocument document)
        {
            Document = document;
            Catalog = document.Internals.Catalog;

            var structTreeRoot = Resolve(Catalog.Elements.GetObject("/StructTreeRoot"))
                ?? throw new InvalidOperationException("The tagged PDF has no structure tree root.");

            StructTreeRoot = structTreeRoot;
            ParentTree = ReadParentTree(structTreeRoot);
            Destinations = ReadDestinations(document);
            Pages = ReadPages(document);
            PageReferences = Pages.Select(x => x.Reference).ToList();

            var rootReference = Resolve(structTreeRoot.Elements.GetObject("/K"))
                ?? throw new InvalidOperationException("The structure tree root has no document element.");

            StructureRoot = Snapshot(rootReference, null);
        }

        public PdfDocument Document { get; }
        public PdfDictionary Catalog { get; }
        public PdfDictionary StructTreeRoot { get; }
        public PdfDocumentInformation Info => Document.Info;
        public StructureNode StructureRoot { get; }
        public IReadOnlyDictionary<int, PdfItem> ParentTree { get; }
        public IReadOnlyDictionary<string, PdfArray> Destinations { get; }
        public List<PageSnapshot> Pages { get; }
        public List<PdfReference> PageReferences { get; }

        public static TaggedPdfInspector Open(byte[] pdf)
        {
            var stream = new MemoryStream(pdf, writable: false);
            return new TaggedPdfInspector(PdfReader.Open(stream, PdfDocumentOpenMode.Import));
        }

        public IEnumerable<string> StructureRoles()
        {
            foreach (var node in Nodes())
                yield return node.Role;
        }

        /// <summary>Every structure element in depth-first reading order, starting at Document.</summary>
        public IEnumerable<StructureNode> Nodes()
        {
            return Flatten(StructureRoot);
        }

        public int ResolvePageIndex(PdfItem item)
        {
            var page = Resolve(item);
            return Pages.FindIndex(x => ReferenceEquals(x.Dictionary, page));
        }

        public void AssertMarkedContentIsMapped(StructureNode node)
        {
            foreach (var child in Flatten(node))
            {
                foreach (var reference in child.MarkedContent)
                {
                    var page = Resolve(reference.Elements.GetObject("/Pg"));
                    var form = Resolve(reference.Elements.GetObject("/Stm"));

                    Assert.That(page, Is.Not.Null, $"{child.Role}: /Pg resolves to a page.");
                    Assert.That(form, Is.Not.Null, $"{child.Role}: /Stm resolves to a form.");

                    var pageSnapshot = Pages.Single(x => ReferenceEquals(x.Dictionary, page));
                    var formSnapshot = pageSnapshot.Forms.Single(x => ReferenceEquals(x.Dictionary, form));

                    Assert.That(formSnapshot.StructParents, Is.Not.Null,
                        $"{child.Role}: the form declares /StructParents.");
                    Assert.That(ParentTree.ContainsKey(formSnapshot.StructParents!.Value), Is.True,
                        $"{child.Role}: the parent tree has an entry for the form.");
                    Assert.That(ResolveArray(ParentTree[formSnapshot.StructParents.Value])
                            .Any(x => ReferenceEquals(Resolve(x), child.Dictionary)), Is.True,
                        $"{child.Role}: the parent tree maps the form back to the element.");
                    Assert.That(formSnapshot.Role, Is.EqualTo(child.Role),
                        "The form's marked-content role matches its structure element.");
                    Assert.That(formSnapshot.Mcid, Is.EqualTo(0));
                }
            }

            foreach (var child in node.Children)
                AssertMarkedContentIsMapped(child);
        }

        public void AssertObjectReferenceIsMapped(StructureNode node)
        {
            var link = node.ObjectReferences.Single();
            var page = Resolve(link.Elements.GetObject("/Pg"));
            var annotation = Resolve(link.Elements.GetObject("/Obj"));

            Assert.That(page, Is.Not.Null);
            Assert.That(annotation, Is.Not.Null);
            Assert.That(annotation!.Elements.ContainsKey("/StructParent"), Is.True,
                "A tagged annotation declares its parent-tree key.");

            var key = annotation.Elements.GetInteger("/StructParent");
            Assert.That(ParentTree.ContainsKey(key), Is.True);
            Assert.That(ReferenceEquals(Resolve(ParentTree[key]), node.Dictionary), Is.True,
                "The annotation's parent-tree entry points back to the link element.");
            Assert.That(ReferenceEquals(Resolve(annotation.Elements.GetObject("/P")), page), Is.True,
                "The annotation is owned by the page it is painted on.");
        }

        public void Dispose()
        {
            Document.Dispose();
        }

        private StructureNode Snapshot(PdfDictionary element, StructureNode? parent)
        {
            var node = new StructureNode(
                element,
                Trim(element.Elements.GetName("/S")),
                ReadText(element, "/Alt"),
                ReadText(element, "/Lang"),
                parent);

            var value = element.Elements.GetObject("/K");

            if (value == null)
                return node;

            foreach (var item in ResolveArray(value))
            {
                var dictionary = Resolve(item);

                if (dictionary == null)
                    continue;

                var type = Trim(dictionary.Elements.GetName("/Type"));

                if (type == "MCR")
                    node.MarkedContent.Add(dictionary);
                else if (type == "OBJR")
                    node.ObjectReferences.Add(dictionary);
                else if (dictionary.Elements.ContainsKey("/S"))
                    node.Children.Add(Snapshot(dictionary, node));
            }

            return node;
        }

        private static IEnumerable<StructureNode> Flatten(StructureNode node)
        {
            yield return node;

            foreach (var child in node.Children)
                foreach (var descendant in Flatten(child))
                    yield return descendant;
        }

        private static IReadOnlyDictionary<int, PdfItem> ReadParentTree(PdfDictionary structTreeRoot)
        {
            var result = new SortedDictionary<int, PdfItem>();
            var tree = Resolve(structTreeRoot.Elements.GetObject("/ParentTree"));
            var numbers = tree?.Elements.GetArray("/Nums");

            if (numbers == null)
                return result;

            for (var index = 0; index + 1 < numbers.Elements.Count; index += 2)
                result[numbers.Elements.GetInteger(index)] = numbers.Elements[index + 1];

            return result;
        }

        private static IReadOnlyDictionary<string, PdfArray> ReadDestinations(PdfDocument document)
        {
            var result = new Dictionary<string, PdfArray>(StringComparer.Ordinal);
            var dests = document.Internals.Catalog.Elements.GetDictionary("/Dests");

            if (dests == null)
                return result;

            foreach (var entry in dests.Elements)
            {
                if (entry.Value is PdfArray array)
                    result[entry.Key] = array;
            }

            return result;
        }

        private static List<PageSnapshot> ReadPages(PdfDocument document)
        {
            var pages = new List<PageSnapshot>();

            for (var index = 0; index < document.PageCount; index++)
            {
                var page = document.Pages[index];
                var reference = PdfInternals.GetReference(page)
                    ?? throw new InvalidOperationException("A page is not an indirect object.");
                var snapshot = new PageSnapshot(page, reference);

                var resources = page.Elements.GetDictionary("/Resources");
                var xobjects = resources?.Elements.GetDictionary("/XObject");

                if (xobjects != null)
                {
                    foreach (var entry in xobjects.Elements)
                    {
                        var form = Resolve(entry.Value);

                        if (form == null || Trim(form.Elements.GetName("/Subtype")) != "Form")
                            continue;

                        snapshot.Forms.Add(FormSnapshot.Create(form, ReadFormContent(form)));
                    }
                }

                var annotations = page.Elements.GetArray("/Annots");

                if (annotations != null)
                {
                    foreach (var item in annotations.Elements)
                    {
                        var annotation = Resolve(item);

                        if (annotation != null)
                            snapshot.Annotations.Add(annotation);
                    }
                }

                pages.Add(snapshot);
            }

            return pages;
        }

        private static string ReadFormContent(PdfDictionary form)
        {
            var stream = form.Stream;

            if (stream == null)
                return string.Empty;

            if (stream.IsFiltered())
                Assert.That(stream.TryUncompress(), Is.True, "A form content stream can be decoded.");

            return Encoding.Latin1.GetString(stream.UnfilteredValue);
        }

        private static string? ReadText(PdfDictionary dictionary, string key)
        {
            var item = dictionary.Elements[key];

            return item switch
            {
                PdfString value => value.Value,
                PdfName name => name.ToString(),
                _ => null
            };
        }

        private static string Trim(string? name)
        {
            return name == null
                ? string.Empty
                : name.StartsWith("/", StringComparison.Ordinal) ? name.Substring(1) : name;
        }

        private static PdfDictionary? Resolve(PdfItem? item)
        {
            return item switch
            {
                PdfReference reference => reference.Value as PdfDictionary,
                PdfDictionary dictionary => dictionary,
                _ => null
            };
        }

        private static PdfArray ResolveArray(PdfItem item)
        {
            if (item is PdfArray array)
                return array;

            if (item is PdfReference reference && reference.Value is PdfArray referenced)
                return referenced;

            return new PdfArray(new PdfDocument(), new PdfItem[] { item });
        }
    }

    internal sealed class PageSnapshot
    {
        public PageSnapshot(PdfPage dictionary, PdfReference reference)
        {
            Dictionary = dictionary;
            Reference = reference;
        }

        public PdfPage Dictionary { get; }
        public PdfReference Reference { get; }
        public List<FormSnapshot> Forms { get; } = new();
        public List<PdfDictionary> Annotations { get; } = new();
    }

    internal sealed class FormSnapshot
    {
        public PdfDictionary Dictionary { get; private set; } = null!;
        public string Content { get; private set; } = string.Empty;
        public string? Role { get; private set; }
        public int? Mcid { get; private set; }
        public int? StructParents { get; private set; }

        public static FormSnapshot Create(PdfDictionary form, string content)
        {
            var snapshot = new FormSnapshot
            {
                Dictionary = form,
                Content = content,
                StructParents = form.Elements.ContainsKey("/StructParents")
                    ? form.Elements.GetInteger("/StructParents")
                    : null
            };

            if (content.StartsWith("/Artifact BMC", StringComparison.Ordinal))
                return snapshot;

            const string marker = " << /MCID ";

            if (!content.StartsWith("/", StringComparison.Ordinal))
                return snapshot;

            var markerIndex = content.IndexOf(marker, StringComparison.Ordinal);

            if (markerIndex < 0)
                return snapshot;

            snapshot.Role = content.Substring(1, markerIndex - 1);

            var valueStart = markerIndex + marker.Length;
            var valueEnd = content.IndexOf(" >>", valueStart, StringComparison.Ordinal);

            snapshot.Mcid = int.Parse(content.Substring(valueStart, valueEnd - valueStart));

            return snapshot;
        }
    }

    internal sealed class StructureNode
    {
        public StructureNode(
            PdfDictionary dictionary,
            string role,
            string? alternativeText,
            string? language,
            StructureNode? parent)
        {
            Dictionary = dictionary;
            Role = role;
            AlternativeText = alternativeText;
            Language = language;
            Parent = parent;
        }

        public PdfDictionary Dictionary { get; }
        public string Role { get; }
        public string? AlternativeText { get; }
        public string? Language { get; }
        public StructureNode? Parent { get; }
        public List<StructureNode> Children { get; } = new();
        public List<PdfDictionary> MarkedContent { get; } = new();
        public List<PdfDictionary> ObjectReferences { get; } = new();
    }
}
#endif
