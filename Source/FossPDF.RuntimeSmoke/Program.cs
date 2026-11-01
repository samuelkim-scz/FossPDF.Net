using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FossPDF.Drawing;
using FossPDF.Drawing.Exceptions;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using SkiaSharp;
#if SMOKE_FONTS
using FossPDF.FontSubset.Glue;
#endif
#if SMOKE_SVG
using FossPDF.Svg;
#endif

namespace FossPDF.RuntimeSmoke;

internal static class Program
{
    internal const string Text = "FossPDF runtime Revenue EUR 12.00";
    private static string output = string.Empty;

    private static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--output")
        {
            Console.Error.WriteLine("Usage: FossPDF.RuntimeSmoke --output <directory>");
            return 2;
        }
        output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        var report = new RuntimeReport();
        Check("TXT-001", TextFixture);
        Check("IMG-001", ImageFixture);
        Check("layout-error", LayoutErrorFixture);
#if SMOKE_FONTS
        report.Feature = "Fonts";
        Check("fonts", FontFixture);
#elif SMOKE_SVG
        report.Feature = "Svg";
        Check("IMG-201", SvgFixture);
#elif SMOKE_PDF
        report.Feature = "Pdf";
        Check("PDF-302", PdfFixture);
#endif
        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories)
                     .Where(path => Path.GetFileName(path).StartsWith("libSkiaSharp", StringComparison.OrdinalIgnoreCase) ||
                                    Path.GetFileName(path).StartsWith("libHarfBuzzSharp", StringComparison.OrdinalIgnoreCase)))
            report.NativeFiles.Add(new NativeFile(Path.GetRelativePath(AppContext.BaseDirectory, path),
                new FileInfo(path).Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))));
        File.WriteAllText(Path.Combine(output, "runtime-report.json"), JsonSerializer.Serialize(report, RuntimeJsonContext.Default.RuntimeReport));
        foreach (var result in report.Results) Console.WriteLine($"{result.Id}: {(result.Passed ? "PASS" : "FAIL")} {result.Error}");
        return report.Results.All(result => result.Passed) ? 0 : 1;

        void Check(string id, Action action)
        {
            try { action(); report.Results.Add(new ScenarioResult(id, true, null)); }
            catch (Exception error) { report.Results.Add(new ScenarioResult(id, false, error.ToString())); }
        }
    }

    private static Document Page(Action<PageDescriptor> compose, float width = 200, float height = 140)
        => Document.Create(container => container.Page(page =>
        {
            page.Size(width, height); page.Margin(10); page.PageColor("#ffffff");
            page.DefaultTextStyle(style => style.FontSize(14));
            compose(page);
        })).WithMetadata(new DocumentMetadata
        {
            Title = "FossPDF runtime qualification", RasterDpi = 72,
            CreationDate = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedDate = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

    private static void Save(Document document, string id)
    {
        var pdf = document.GeneratePdf();
        Require(Encoding.ASCII.GetString(pdf, 0, 5) == "%PDF-", "Missing PDF header.");
        File.WriteAllBytes(Path.Combine(output, id + ".pdf"), pdf);
        var images = document.GenerateImages().ToArray();
        Require(images.Length == 1, "The fixture must produce one page.");
        File.WriteAllBytes(Path.Combine(output, id + ".png"), images[0]);
        using var image = SKBitmap.Decode(images[0]);
        Require(image != null && image.Width > 0 && image.Height > 0, "The raster output must decode.");
    }

    private static void TextFixture() => Save(Page(page => page.Content().Text(text =>
    {
        text.Span("FossPDF runtime ").Bold();
        text.Span("Revenue ").Italic().FontColor("#1976d2");
        text.Span("EUR 12.00");
    })), "TXT-001");

    private static void ImageFixture()
    {
        using var bitmap = new SKBitmap(64, 64);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint())
        {
            var colors = new[] { "#d32f2f", "#1976d2", "#388e3c", "#fbc02d" };
            for (var index = 0; index < colors.Length; index++)
            {
                paint.Color = SKColor.Parse(colors[index]);
                canvas.DrawRect(index % 2 * 32, index / 2 * 32, 32, 32, paint);
            }
        }
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        Save(Page(page => page.Content().Image(encoded.ToArray()), 100, 100), "IMG-001");
        using var rendered = SKBitmap.Decode(Path.Combine(output, "IMG-001.png"));
        var red = rendered.GetPixel(30, 30); var blue = rendered.GetPixel(70, 30);
        var green = rendered.GetPixel(30, 70); var yellow = rendered.GetPixel(70, 70);
        Require(red.Red > 180 && red.Green < 70 && blue.Blue > 180 && blue.Red < 60 &&
                green.Green > 100 && green.Red < 90 && yellow.Red > 220 && yellow.Green > 150,
            "Raster quadrant order changed.");
    }

    private static void LayoutErrorFixture()
    {
        try
        {
            Page(page => page.Content().Width(10000).Height(10000).Text("Impossible"))
                .WithSettings(new DocumentSettings { EnableDebugging = true, DocumentLayoutExceptionThreshold = 3 }).GeneratePdf();
        }
        catch (DocumentLayoutException error)
        {
            var trace = error.ElementTrace ?? string.Empty;
            File.WriteAllText(Path.Combine(output, "layout-error.txt"), error.Message + "\n" + trace);
            Require(trace.Contains("10000", StringComparison.Ordinal) && trace.Contains("Constrained", StringComparison.Ordinal),
                "The diagnostic must retain element configuration or identity.");
            return;
        }
        throw new InvalidOperationException("Impossible content must fail with a layout diagnostic.");
    }

#if SMOKE_FONTS
    private static void FontFixture()
    {
        FontManager.RegisterFontFromEmbeddedResource("FossPDF.RuntimeSmoke.ConsumerFont.ttf");
        var subsetCalls = 0;
        FontManager.RegisterSubsetCallback((manager, fonts) =>
        {
            BuiltInFontSubsetting.Callback(manager, fonts);
            subsetCalls++;
        });
        try
        {
            Save(Page(page => page.Content().Text("Subset Lato Revenue EUR 12.00").FontFamily("Lato")), "fonts");
            Require(subsetCalls > 0, "The built-in font subsetter must execute.");
        }
        finally { FontManager.RegisterSubsetCallback(null); }
    }
#endif

#if SMOKE_SVG
    private static void SvgFixture()
    {
        using var image = SvgImage.FromText("<svg xmlns='http://www.w3.org/2000/svg' width='80' height='80' viewBox='0 0 80 80'><rect width='40' height='80' fill='#d32f2f'/><rect x='40' width='40' height='80' fill='#1976d2'/><text x='5' y='68' font-family='Lato' font-size='8' fill='#ffffff'>SVG Revenue</text></svg>");
        Save(Page(page => page.Content().Svg(image), 100, 100), "IMG-201");
        using var rendered = SKBitmap.Decode(Path.Combine(output, "IMG-201.png"));
        Require(rendered.GetPixel(30, 50).Red > 180 && rendered.GetPixel(70, 50).Blue > 180, "SVG geometry changed.");
    }
#endif

#if SMOKE_PDF
    private static void PdfFixture()
    {
        var primary = Page(page => page.Content().Text("FIRST")).GeneratePdf();
        var append = Page(page => page.Content().Text("SECOND"), 240, 180).GeneratePdf();
        using var input = new MemoryStream(primary); using var outputStream = new MemoryStream();
        DocumentOperation.Load(input).Merge(append).TakePages("1").Save(outputStream);
        Require(input.CanRead && outputStream.CanWrite, "Borrowed streams must remain open.");
        File.WriteAllBytes(Path.Combine(output, "PDF-302.pdf"), outputStream.ToArray());
        var payload = Encoding.UTF8.GetBytes("<runtime>RUN-503</runtime>");
        using var attachment = new MemoryStream(payload);
        var encrypted = DocumentOperation.Load(primary)
            .AddAttachment(attachment, new DocumentOperation.DocumentAttachment
            {
                AttachmentName = "runtime-data.xml", MimeType = "application/xml",
                Relationship = DocumentOperation.DocumentAttachmentRelationship.Data
            })
            .ExtendMetadata("<runtime:Id xmlns:runtime='urn:fosspdf:runtime'>RUN-503</runtime:Id>")
            .Encrypt(new DocumentOperation.Encryption256Bit { UserPassword = "runtime-reader", OwnerPassword = "runtime-owner" })
            .SaveToBytes();
        Require(attachment.CanRead, "The attachment stream must remain open.");
        File.WriteAllBytes(Path.Combine(output, "PDF-305-encrypted.pdf"), encrypted);
        var decrypted = DocumentOperation.Load(encrypted, "runtime-owner").Decrypt().SaveToBytes();
        File.WriteAllBytes(Path.Combine(output, "PDF-305-decrypted.pdf"), decrypted);
    }
#endif

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed class RuntimeReport
{
    public int SchemaVersion { get; set; } = 1;
    public string Feature { get; set; } = "Core";
#if SMOKE_AOT
    public string BuildMode { get; set; } = "aot";
#elif SMOKE_TRIMMED
    public string BuildMode { get; set; } = "trimmed";
#else
    public string BuildMode { get; set; } = "jit";
#endif
    public string Rid { get; set; } = RuntimeInformation.RuntimeIdentifier;
    public string Framework { get; set; } = RuntimeInformation.FrameworkDescription;
    public string OperatingSystem { get; set; } = RuntimeInformation.OSDescription;
    public string Architecture { get; set; } = RuntimeInformation.ProcessArchitecture.ToString();
    public string OsArchitecture { get; set; } = RuntimeInformation.OSArchitecture.ToString();
    public bool DynamicCodeSupported { get; set; } = RuntimeFeature.IsDynamicCodeSupported;
    public List<ScenarioResult> Results { get; set; } = new();
    public List<NativeFile> NativeFiles { get; set; } = new();
}
internal sealed record ScenarioResult(string Id, bool Passed, string? Error);
internal sealed record NativeFile(string Path, long Bytes, string Sha256);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(RuntimeReport))]
internal partial class RuntimeJsonContext : JsonSerializerContext;
