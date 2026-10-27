using System.Text;
using FossPDF.Fluent;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace FossPDF.NativePrototypes;

internal static class TaggingPrototype
{
    // FP-401 must capture batches during rendering; this proof consumes explicit associations.
    // -1 means a known whole-page batch; other values identify independently captured streams.
    internal sealed record PageBatch(int PageIndex, string Role, string Language, string Text, int ContentStreamIndex = -1);

    internal static readonly PageBatch[] Batches =
    [
        new(0, "H1", "en-US", "Quarterly totals"),
        new(1, "P", "en-US", "Revenue increased by 12 percent.")
    ];

    public static PrototypeResult Run(string outputDirectory)
    {
        var checks = new List<string>();
        var source = Document.Create(container =>
        {
            foreach (var batch in Batches)
                container.Page(page =>
                {
                    page.Size(300, 120);
                    page.Margin(15);
                    page.Content().Text(batch.Text).FontSize(batch.Role == "H1" ? 18 : 12);
                });
        }).GeneratePdf();

        var sourcePath = Path.Combine(outputDirectory, "tagging-source-foss.pdf");
        File.WriteAllBytes(sourcePath, source);
        using var input = new MemoryStream(source);
        using var output = new MemoryStream();
        TagKnownPageBatches(input, output, Batches);
        Require(input.CanRead && output.CanWrite, "caller streams remain open after backend documents are disposed");

        var tagged = output.ToArray();
        var taggedPath = Path.Combine(outputDirectory, "tagged-structure.pdf");
        File.WriteAllBytes(taggedPath, tagged);
        JsonFile.Write(Path.Combine(outputDirectory, "semantic-batches.json"), Batches);

        using var originalStream = new MemoryStream(source);
        using var original = PdfReader.Open(originalStream, PdfDocumentOpenMode.Import);
        using var reopenedStream = new MemoryStream(tagged);
        using var reopened = PdfReader.Open(reopenedStream, PdfDocumentOpenMode.Import);
        var catalog = reopened.Internals.Catalog;
        Require(reopened.PageCount == 2, "tagged output retains both Skia pages");
        Require(catalog.Elements.GetString("/Lang") == "en-US", "document language survives serialization");
        Require(catalog.Elements.GetDictionary("/MarkInfo")!.Elements.GetBoolean("/Marked"), "catalog is marked");
        var root = catalog.Elements.GetDictionary("/StructTreeRoot")!;
        var documentNode = root.Elements.GetArray("/K")!.Elements.GetDictionary(0)!;
        var children = documentNode.Elements.GetArray("/K")!;
        var numbers = root.Elements.GetDictionary("/ParentTree")!.Elements.GetArray("/Nums")!;
        Require(documentNode.Elements.GetName("/S") == "/Document" && children.Elements.Count == 2,
            "structure has a Document with heading and paragraph in declared order");
        for (var index = 0; index < Batches.Length; index++)
        {
            var node = children.Elements.GetDictionary(index)!;
            var page = reopened.Pages[index];
            Require(node.Elements.GetName("/S") == "/" + Batches[index].Role &&
                    node.Elements.GetReference("/Pg")!.ObjectID == page.Reference!.ObjectID &&
                    node.Elements.GetInteger("/K") == 0 && page.Elements.GetInteger("/StructParents") == index &&
                    numbers.Elements.GetInteger(index * 2) == index &&
                    numbers.Elements.GetArray(index * 2 + 1)!.Elements.GetDictionary(0)!.Reference!.ObjectID == node.Reference!.ObjectID,
                $"page {index + 1} has matching role, page reference, MCID and parent-tree entry");
            var before = Contents(original.Pages[index]);
            var after = Contents(page);
            Require(before.All(bytes => after.Any(candidate => candidate.AsSpan().IndexOf(bytes) >= 0)),
                $"page {index + 1} retains the original Skia paint instructions");
            Require(Encoding.ASCII.GetString(after[0]).Contains("/MCID 0 /ActualText <FEFF", StringComparison.Ordinal) &&
                    Encoding.ASCII.GetString(after[^1]).Contains("EMC", StringComparison.Ordinal),
                $"page {index + 1} has a marked-content wrapper with declared Unicode");
        }

        return new PrototypeResult
        {
            Name = "semantic-tagging", Passed = true, Checks = checks,
            Artifacts = [sourcePath, taggedPath, Path.Combine(outputDirectory, "semantic-batches.json")]
        };

        void Require(bool condition, string description)
        {
            if (!condition) throw new InvalidDataException(description);
            checks.Add("OK: " + description + ".");
        }
    }

    internal static void TagKnownPageBatches(Stream input, Stream output, IReadOnlyList<PageBatch> batches)
    {
        using var copy = PrototypeStreams.CopyInput(input);
        using var document = PdfReader.Open(copy, PdfDocumentOpenMode.Modify);
        // Reject ambiguous/missing associations before changing a PDF or writing any output.
        var wholePages = batches.All(batch => batch.ContentStreamIndex == -1);
        if (batches.Count == 0 || batches.Any(batch => batch.PageIndex < 0 || batch.PageIndex >= document.PageCount ||
                batch.Role is not ("H1" or "P") || string.IsNullOrWhiteSpace(batch.Language) || string.IsNullOrWhiteSpace(batch.Text)) ||
            Enumerable.Range(0, document.PageCount).Any(index =>
            {
                var pageBatches = batches.Where(batch => batch.PageIndex == index).ToArray();
                return wholePages ? pageBatches.Length != 1 :
                    !pageBatches.Select(batch => batch.ContentStreamIndex).Order().SequenceEqual(
                        Enumerable.Range(0, document.Pages[index].Contents.Elements.Count));
            }))
            throw new ArgumentException("Each known page/stream must have exactly one explicit H1/P association.", nameof(batches));

        var root = Indirect("/StructTreeRoot");
        var documentNode = Indirect("/StructElem");
        var parentTree = Indirect(null);
        var children = new PdfArray(document);
        var numbers = new PdfArray(document);
        documentNode.Elements.SetName("/S", "/Document");
        documentNode.Elements["/P"] = root.Reference!;
        documentNode.Elements["/K"] = children;
        root.Elements["/K"] = new PdfArray(document, documentNode.Reference!);
        root.Elements["/ParentTree"] = parentTree.Reference!;
        root.Elements.SetInteger("/ParentTreeNextKey", document.PageCount);
        parentTree.Elements["/Nums"] = numbers;
        var catalog = document.Internals.Catalog;
        catalog.Elements["/StructTreeRoot"] = root.Reference!;
        var marked = new PdfDictionary(document);
        marked.Elements.SetBoolean("/Marked", true);
        catalog.Elements["/MarkInfo"] = marked;
        catalog.Elements.SetString("/Lang", "en-US");

        var parents = new PdfArray[document.PageCount];
        var wrappers = new Dictionary<(int Page, int Stream), (byte[] Prefix, byte[] Suffix)>();
        for (var index = 0; index < document.PageCount; index++)
        {
            parents[index] = new PdfArray(document);
            numbers.Elements.Add(new PdfInteger(index));
            numbers.Elements.Add(parents[index]);
            document.Pages[index].Elements.SetInteger("/StructParents", index);
        }

        foreach (var batch in batches)
        {
            var page = document.Pages[batch.PageIndex];
            var node = Indirect("/StructElem");
            node.Elements.SetName("/S", "/" + batch.Role);
            node.Elements.SetString("/Lang", batch.Language);
            node.Elements["/P"] = documentNode.Reference!;
            node.Elements["/Pg"] = page.Reference!;
            var mcid = parents[batch.PageIndex].Elements.Count;
            node.Elements.SetInteger("/K", mcid);
            children.Elements.Add(node.Reference!);
            parents[batch.PageIndex].Elements.Add(node.Reference!);

            var text = Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(batch.Text));
            var prefix = Encoding.ASCII.GetBytes($"/{batch.Role} << /MCID {mcid} /ActualText <FEFF{text}> >> BDC\n");
            var suffix = Encoding.ASCII.GetBytes("\nEMC\n");
            if (wholePages)
            {
                page.Contents.PrependContent().CreateStream(prefix);
                page.Contents.AppendContent().CreateStream(suffix);
            }
            else wrappers.Add((batch.PageIndex, batch.ContentStreamIndex), (prefix, suffix));
        }
        if (!wholePages)
        {
            for (var index = 0; index < document.PageCount; index++)
            {
                var contents = document.Pages[index].Contents;
                var original = contents.Cast<PdfItem>().ToArray();
                contents.Elements.Clear();
                for (var stream = 0; stream < original.Length; stream++)
                {
                    var wrapper = wrappers[(index, stream)];
                    contents.Elements.Add(StreamObject(wrapper.Prefix).Reference!);
                    contents.Elements.Add(original[stream]);
                    contents.Elements.Add(StreamObject(wrapper.Suffix).Reference!);
                }
            }
        }
        PrototypeStreams.Save(document, output);

        PdfDictionary StreamObject(byte[] bytes)
        {
            var dictionary = Indirect(null);
            dictionary.CreateStream(bytes);
            return dictionary;
        }

        PdfDictionary Indirect(string? type)
        {
            var dictionary = new PdfDictionary(document);
            if (type != null) dictionary.Elements.SetName("/Type", type);
            document.Internals.AddObject(dictionary);
            return dictionary;
        }
    }

    private static byte[][] Contents(PdfPage page) => page.Contents.Cast<PdfItem>()
        .Select(item => ((PdfDictionary)((PdfReference)item).Value).Stream.UnfilteredValue).ToArray();
}
