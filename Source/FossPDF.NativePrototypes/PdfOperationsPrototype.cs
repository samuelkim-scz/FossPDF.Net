using System;
using FossPDF.Fluent;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FossPDF.NativePrototypes;

internal static class PdfOperationsPrototype
{
    public static PrototypeResult Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var checks = new List<string>();
        var artifacts = new List<string>();
        var passed = true;

        var sourceA = CreateSourcePdf(200, 100, "#d32f2f");
        var sourceB = CreateSourcePdf(300, 100, "#1976d2");

        var sourceAPath = Path.Combine(outputDirectory, "pdf-ops-source-a.pdf");
        var sourceBPath = Path.Combine(outputDirectory, "pdf-ops-source-b.pdf");
        File.WriteAllBytes(sourceAPath, sourceA);
        File.WriteAllBytes(sourceBPath, sourceB);
        artifacts.Add(sourceAPath);
        artifacts.Add(sourceBPath);

        using var sourceAStream = new MemoryStream(sourceA);
        using var sourceBStream = new MemoryStream(sourceB);
        var readerOptions = new PdfReaderOptions();

        using var documentA = PdfReader.Open(sourceAStream, PdfDocumentOpenMode.Import, readerOptions);
        using var documentB = PdfReader.Open(sourceBStream, PdfDocumentOpenMode.Import, readerOptions);

        using var output = new PdfDocument();
        output.AddPage(documentB.Pages[0]);
        output.AddPage(documentA.Pages[0]);

        using var outputStream = new MemoryStream();
        output.Save(outputStream);

        if (!outputStream.CanRead || !outputStream.CanWrite)
        {
            passed = false;
            checks.Add("FAIL: PDFsharp closed the caller-owned output stream.");
        }
        else
        {
            checks.Add("OK: PDFsharp left the caller-owned output stream open.");
        }

        var reordered = outputStream.ToArray();

        if (reordered.Length < 5 || reordered[0] != (byte) '%' || reordered[1] != (byte) 'P' ||
            reordered[2] != (byte) 'D' || reordered[3] != (byte) 'F' || reordered[4] != (byte) '-')
        {
            passed = false;
            checks.Add("FAIL: reordered output does not start with %PDF-.");
        }

        var reorderedPath = Path.Combine(outputDirectory, "pdf-ops-reordered.pdf");
        File.WriteAllBytes(reorderedPath, reordered);
        artifacts.Add(reorderedPath);

        using var reopenedStream = new MemoryStream(reordered);
        using var reopened = PdfReader.Open(reopenedStream, PdfDocumentOpenMode.Import, readerOptions);

        var widths = new List<double>();

        for (var index = 0; index < reopened.PageCount; index++)
            widths.Add(reopened.Pages[index].Width.Point);

        if (reopened.PageCount != 2)
        {
            passed = false;
            checks.Add($"FAIL: expected two pages after reordering, got {reopened.PageCount}.");
        }
        else if (widths.Count != 2 || Math.Abs(widths[0] - 300) > 0.01 || Math.Abs(widths[1] - 200) > 0.01)
        {
            passed = false;
            checks.Add($"FAIL: page order/sizes are [{string.Join(", ", widths)}], expected [300, 200].");
        }
        else
        {
            checks.Add("OK: imported Skia-generated pages were reordered to [300, 200].");
        }

        return new PrototypeResult
        {
            Name = "existing-pdf-operations",
            Passed = passed,
            Checks = checks,
            Artifacts = artifacts
        };
    }

    private static byte[] CreateSourcePdf(int width, int height, string color)
    {
        return Document.Create(container => container.Page(page =>
        {
            page.Size(width, height);
            page.PageColor(color);
        })).GeneratePdf();
    }
}
