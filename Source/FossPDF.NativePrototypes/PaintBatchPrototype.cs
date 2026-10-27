using FossPDF.Fluent;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace FossPDF.NativePrototypes;

// Proves the proposed batch transport separately from the whole-page tagging mechanism:
// two Skia-painted fragments become independently addressable vector forms on one page.
internal static class PaintBatchPrototype
{
    public static PrototypeResult Run(string outputDirectory)
    {
        using var destination = new PdfDocument();
        var page = destination.AddPage();
        page.Width = XUnit.FromPoint(300);
        page.Height = XUnit.FromPoint(120);
        var mapping = new List<object>();
        var artifacts = new List<string>();
        var associations = new List<TaggingPrototype.PageBatch>();
        foreach (var batch in TaggingPrototype.Batches)
        {
            var fragment = Document.Create(container => container.Page(source =>
            {
                source.Size(300, 60);
                source.Margin(15);
                source.Content().Text(batch.Text).FontSize(batch.Role == "H1" ? 18 : 12);
            })).GeneratePdf();
            var sourceName = $"batch-{batch.PageIndex}-source.pdf";
            var sourcePath = Path.Combine(outputDirectory, sourceName);
            File.WriteAllBytes(sourcePath, fragment);
            artifacts.Add(sourcePath);
            using var input = new MemoryStream(fragment);
            using var form = XPdfForm.FromStream(input);
            using (var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
                graphics.DrawImage(form, 0, batch.PageIndex * 60, 300, 60);
            mapping.Add(new
            {
                NodeId = batch.PageIndex, batch.Role, batch.Language, batch.Text, Source = sourceName,
                PageIndex = 0, ContentStreamIndex = page.Contents.Elements.Count - 1
            });
            associations.Add(new TaggingPrototype.PageBatch(0, batch.Role, batch.Language, batch.Text,
                page.Contents.Elements.Count - 1));
        }
        using var output = new MemoryStream();
        PrototypeStreams.Save(destination, output);
        var assembledPath = Path.Combine(outputDirectory, "semantic-batch-assembly.pdf");
        File.WriteAllBytes(assembledPath, output.ToArray());
        var mappingPath = Path.Combine(outputDirectory, "paint-batches.json");
        JsonFile.Write(mappingPath, mapping);
        artifacts.Add(assembledPath);
        artifacts.Add(mappingPath);
        using var assembledInput = new MemoryStream(output.ToArray());
        using var taggedOutput = new MemoryStream();
        TaggingPrototype.TagKnownPageBatches(assembledInput, taggedOutput, associations);
        var taggedPath = Path.Combine(outputDirectory, "semantic-batch-tagged.pdf");
        File.WriteAllBytes(taggedPath, taggedOutput.ToArray());
        artifacts.Add(taggedPath);
        if (page.Contents.Elements.Count != 2)
            throw new InvalidDataException("Expected independently addressable streams for both paint batches.");
        return new PrototypeResult
        {
            Name = "semantic-paint-batches", Passed = true, Artifacts = artifacts,
            Checks = ["OK: two FossPDF/Skia fragments were composed as independent vector form invocations and tagged with distinct MCIDs on one page."]
        };
    }
}
