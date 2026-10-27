using System;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.UniversalAccessibility;

namespace FossPDF.NativePrototypes;

internal static class TaggingPrototype
{
    public static PrototypeResult Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var checks = new List<string>();
        var artifacts = new List<string>();
        var passed = true;

        using var document = new PdfDocument();
        document.Options.CompressContentStreams = false;
        document.Options.NoCompression = true;

        // UAManager must be created before pages or other document operations.
        var uaManager = UAManager.ForDocument(document);
        uaManager.SetDocumentLanguage("en-US");
        var builder = uaManager.StructureBuilder;

        var headingFont = new XFont("Lato", 18, XFontStyleEx.Bold);
        var bodyFont = new XFont("Lato", 12, XFontStyleEx.Regular);

        builder.BeginElement(PdfGroupingElementTag.Article);

        var page = document.AddPage();
        page.Width = XUnit.FromPoint(300);
        page.Height = XUnit.FromPoint(200);

        {
            using var graphics = XGraphics.FromPdfPage(page);

            builder.BeginElement(PdfBlockLevelElementTag.Heading1);
            graphics.DrawString("Accessibility prototype", headingFont, XBrushes.Black, 30, 50);
            builder.End();

            builder.BeginElement(PdfBlockLevelElementTag.Paragraph);
            // Trailing space is required for screen-reader word separation.
            graphics.DrawString("Tagged content for structure verification. ", bodyFont, XBrushes.Black, 30, 90);
            builder.End();
        }

        builder.End();

        using var stream = new MemoryStream();
        document.Save(stream);
        var bytes = stream.ToArray();

        var path = Path.Combine(outputDirectory, "tagged-structure.pdf");
        File.WriteAllBytes(path, bytes);
        artifacts.Add(path);

        var ascii = Encoding.ASCII.GetString(bytes);

        Check("structure tree root", uaManager.StructureTreeRoot != null);
        Check("document structure element", uaManager.StructureTreeElementDocument != null);
        Check("/StructTreeRoot marker", ascii.Contains("/StructTreeRoot", StringComparison.Ordinal));
        Check("/StructElem marker", ascii.Contains("/StructElem", StringComparison.Ordinal));
        Check("/MarkInfo marker", ascii.Contains("/MarkInfo", StringComparison.Ordinal));
        Check("/MCID marker", ascii.Contains("/MCID", StringComparison.Ordinal));
        Check("/ParentTree marker", ascii.Contains("/ParentTree", StringComparison.Ordinal));
        Check("marked-content BDC operator", ascii.Contains("BDC", StringComparison.Ordinal));
        Check("marked-content EMC operator", ascii.Contains("EMC", StringComparison.Ordinal));

        using var reopenedStream = new MemoryStream(bytes);
        using var reopened = PdfReader.Open(reopenedStream, PdfDocumentOpenMode.Import, new PdfReaderOptions());
        Check("reopened page count", reopened.PageCount == 1);

        return new PrototypeResult
        {
            Name = "semantic-tagging",
            Passed = passed,
            Checks = checks,
            Artifacts = artifacts
        };

        void Check(string description, bool condition)
        {
            if (condition)
            {
                checks.Add($"OK: {description}.");
                return;
            }

            passed = false;
            checks.Add($"FAIL: {description}.");
        }
    }
}
