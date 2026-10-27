using System;
using FossPDF.Fluent;
using SkiaSharp;
using Svg.Skia;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using SharedImage = FossPDF.Infrastructure.Image;

namespace FossPDF.NativePrototypes;

internal static class SvgPrototype
{
    private const string Svg = """
        <svg xmlns="http://www.w3.org/2000/svg" width="240" height="140" viewBox="0 0 240 140">
          <rect width="240" height="140" fill="#ffffff" />
          <rect x="10" y="10" width="100" height="60" fill="#d32f2f" />
          <circle cx="170" cy="40" r="30" fill="#1976d2" />
          <path d="M10 120 L110 90 L230 120 Z" fill="#388e3c" />
        </svg>
        """;

    public static PrototypeResult Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var checks = new List<string>();
        var artifacts = new List<string>();
        var passed = true;

        using var svgInput = new MemoryStream(Encoding.UTF8.GetBytes(Svg));
        using var svg = Parse(svgInput);
        var picture = svg.Picture ?? throw new InvalidOperationException("Svg.Skia did not produce a picture.");

        var width = (int) Math.Ceiling(picture.CullRect.Width);
        var height = (int) Math.Ceiling(picture.CullRect.Height);

        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Svg.Skia returned an empty picture.");

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        canvas.DrawPicture(picture);

        CheckPixel(bitmap, 20, 20, "#d32f2f", "red rectangle");
        CheckPixel(bitmap, 170, 40, "#1976d2", "blue circle");
        CheckPixel(bitmap, 120, 110, "#388e3c", "green triangle");
        CheckPixel(bitmap, 230, 10, "#ffffff", "white outside the shapes");

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var png = data.ToArray();

        var svgPath = Path.Combine(outputDirectory, "prototype.svg");
        var pngPath = Path.Combine(outputDirectory, "svg-render.png");
        File.WriteAllText(svgPath, Svg);
        File.WriteAllBytes(pngPath, png);
        artifacts.Add(svgPath);
        artifacts.Add(pngPath);

        using var sharedImage = SharedImage.FromBinaryData(png);
        var pdf = Document.Create(container => container.Page(page =>
        {
            page.Size(260, 160);
            page.Margin(10);
            page.PageColor("#ffffff");
            page.Content().Image(sharedImage);
        })).GeneratePdf(out var pageCount);

        var pdfPath = Path.Combine(outputDirectory, "svg-in-foss-pdf.pdf");
        File.WriteAllBytes(pdfPath, pdf);
        artifacts.Add(pdfPath);

        // Draw the same preloaded picture directly into the PDF canvas. No bitmap is involved.
        var vectorPdf = Document.Create(container => container.Page(page =>
        {
            page.Size(260, 160);
            page.Margin(10);
            page.Content().Canvas((target, size) =>
            {
                target.Save();
                target.Scale(size.Width / width, size.Height / height);
                target.DrawPicture(picture);
                target.Restore();
            });
        })).GeneratePdf();
        var vectorPath = Path.Combine(outputDirectory, "svg-vector-in-foss-pdf.pdf");
        File.WriteAllBytes(vectorPath, vectorPdf);
        artifacts.Add(vectorPath);
        checks.Add("OK: the reusable SKPicture was also drawn directly through FossPDF.Canvas for vector PDF output.");

        if (pageCount != 1)
        {
            passed = false;
            checks.Add($"FAIL: expected one PDF page, got {pageCount}.");
        }
        else
        {
            checks.Add($"OK: Svg.Skia rendered {width}x{height} px and FossPDF placed it in a one-page PDF.");
        }

        return new PrototypeResult
        {
            Name = "svg-input",
            Passed = passed,
            Checks = checks,
            Artifacts = artifacts
        };

        void CheckPixel(SKBitmap target, int x, int y, string expectedColor, string description)
        {
            var expected = SKColor.Parse(expectedColor);
            var actual = target.GetPixel(x, y);

            if (actual == expected)
            {
                checks.Add($"OK: {description} at ({x},{y}) is {expectedColor}.");
                return;
            }

            passed = false;
            checks.Add($"FAIL: {description} at ({x},{y}) is {actual} instead of {expectedColor}.");
        }
    }

    internal static SKSvg Parse(Stream input)
    {
        using var copy = PrototypeStreams.CopyInput(input);
        using var reader = XmlReader.Create(copy, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null
        });
        var document = XDocument.Load(reader);
        // The proof deliberately accepts a small shape-only subset. Production resource/font
        // resolution, sanitization and SVG feature coverage are contracts owned by FP-201.
        if (document.Root == null || document.Root.Name != XName.Get("svg", "http://www.w3.org/2000/svg") ||
            document.Root.DescendantsAndSelf().Any(element =>
                element.Name.LocalName is not ("svg" or "rect" or "circle" or "path") ||
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName is "href" or "src" or "style" ||
                    attribute.Value.Contains("url(", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("The SVG proof only accepts self-contained shape fixtures.");
        return SKSvg.CreateFromSvg(document.ToString());
    }
}
