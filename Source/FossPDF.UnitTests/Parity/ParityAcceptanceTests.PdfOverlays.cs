#if NET9_0
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using FossPDF.Fluent;
using NUnit.Framework;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace FossPDF.UnitTests.Parity
{
    /// <summary>
    /// FP-303 acceptance: overlay and underlay files are placed around the original page content of
    /// selected pages. The operations companion targets net9.0 only, so this runner is compiled for
    /// that framework; the fixture declares frameworks: ["net9.0"].
    /// </summary>
    public partial class ParityAcceptanceTests
    {
        [ParityFixtureRunner("PDF-303")]
        private static void RunPdfLayers(ParityFixture fixture)
        {
            var catalog = FixtureCatalog.Load();
            var sourceId = fixture.Input.GetProperty("source").GetString();
            var sourceFixture = catalog.Fixtures.Single(x => x.Id == sourceId);

            // The source is a generated contract, not a pre-baked artifact: the operation must
            // import its pages rather than composing the document again.
            var source = ExplicitPagination(sourceFixture).GeneratePdf();

            var overlay = fixture.Input.GetProperty("overlay");
            var underlay = fixture.Input.GetProperty("underlay");
            var pages = FixtureCatalog.ReadArray(fixture.Input, "pages")
                .Select(x => x.GetInt32())
                .ToArray();

            Assert.That(pages, Is.Not.Empty, "PDF-303: the fixture declares target pages.");

            var overlayPdf = CreateLayerPdf(ReadLayerRectangle(overlay), overlay.GetProperty("color").GetString());
            var underlayPdf = CreateLayerPdf(ReadLayerRectangle(underlay), underlay.GetProperty("color").GetString());

            var targetSelector = string.Join(",", pages);

            SaveArtifact(fixture.Id, "source.pdf", source);
            var overlayPath = SaveLayerSource(fixture.Id, "overlay.pdf", overlayPdf);
            var underlayPath = SaveLayerSource(fixture.Id, "underlay.pdf", underlayPdf);

            var sourceSnapshot = source.ToArray();
            var overlaySnapshot = overlayPdf.ToArray();
            var underlaySnapshot = underlayPdf.ToArray();

            var output = DocumentOperation
                .Load(source)
                .UnderlayFile(new DocumentOperation.LayerConfiguration
                {
                    FilePath = underlayPath,
                    TargetPages = targetSelector
                })
                .OverlayFile(new DocumentOperation.LayerConfiguration
                {
                    FilePath = overlayPath,
                    TargetPages = targetSelector
                })
                .SaveToBytes();

            Assert.That(Encoding.ASCII.GetString(output, 0, 5), Is.EqualTo("%PDF-"),
                "PDF-303: the result is a PDF document.");
            SaveArtifact(fixture.Id, "layered.pdf", output);

            var sourcePages = ReadPageFacts(source);
            var outputPages = ReadPageFacts(output);

            Assert.That(outputPages, Has.Length.EqualTo(sourcePages.Length),
                "PDF-303: applying layers does not add or remove pages.");
            Assert.That(source, Is.EqualTo(sourceSnapshot),
                "PDF-303: the primary input byte array is not modified.");
            Assert.That(overlayPdf, Is.EqualTo(overlaySnapshot),
                "PDF-303: the overlay byte array is not modified.");
            Assert.That(underlayPdf, Is.EqualTo(underlaySnapshot),
                "PDF-303: the underlay byte array is not modified.");

            for (var pageIndex = 0; pageIndex < sourcePages.Length; pageIndex++)
            {
                Assert.That(outputPages[pageIndex].Width, Is.EqualTo(sourcePages[pageIndex].Width).Within(0.01),
                    $"PDF-303: page {pageIndex + 1} keeps its width.");
                Assert.That(outputPages[pageIndex].Height, Is.EqualTo(sourcePages[pageIndex].Height).Within(0.01),
                    $"PDF-303: page {pageIndex + 1} keeps its height.");
            }

            var targetPage = pages[0] - 1;
            Assert.That(targetPage, Is.InRange(0, outputPages.Length - 1),
                "PDF-303: the target page is inside the source document.");

            var layers = ReadLayerStreams(output, targetPage);

            Assert.That(layers, Has.Length.EqualTo(ReadLayerStreams(source, targetPage).Length + 2),
                "PDF-303: the target page gains one underlay and one overlay content stream.");
            Assert.That(layers[0], Does.Contain("/L0 Do"),
                "PDF-303: the underlay stream is placed before the original content.");
            Assert.That(layers[1], Is.EqualTo(ReadLayerStreams(source, targetPage)[0]),
                "PDF-303: the original content stream is unchanged and stays between the layers.");
            Assert.That(layers[^1], Does.Contain("/L1 Do"),
                "PDF-303: the overlay stream is placed after the original content.");

            var underlayForm = ReadLayerForm(output, targetPage, "/L0");
            Assert.That(underlayForm, Does.Contain(ColorOperator(underlay)),
                "PDF-303: the underlay form carries the declared color.");
            Assert.That(underlayForm, Does.Contain("0 0 120 100 re"),
                "PDF-303: the underlay covers the full page box, so it is visible through areas the original content leaves transparent.");

            var overlayForm = ReadLayerForm(output, targetPage, "/L1");
            Assert.That(overlayForm, Does.Contain(ColorOperator(overlay)),
                "PDF-303: the overlay form carries the declared color.");
            Assert.That(overlayForm, Does.Contain("15 15 20 20 re"),
                "PDF-303: the overlay keeps its declared rectangle, which covers the underlying red block.");

            Assert.That(layers[0], Does.Contain("1 0 0 1 0 0 cm"),
                "PDF-303: a same-size layer is placed without scaling or offset.");
            Assert.That(layers[^1], Does.Contain("1 0 0 1 0 0 cm"),
                "PDF-303: a same-size layer is placed without scaling or offset.");

            for (var pageIndex = 0; pageIndex < outputPages.Length; pageIndex++)
            {
                if (pageIndex == targetPage)
                    continue;

                Assert.That(outputPages[pageIndex].Signature, Is.EqualTo(sourcePages[pageIndex].Signature),
                    $"PDF-303: page {pageIndex + 1} is not a target and stays unchanged.");
            }

            Assert.That(ReadRotation(output, targetPage), Is.EqualTo(ReadRotation(source, targetPage)),
                "PDF-303: the target page keeps its rotation.");

            Assert.That(File.ReadAllBytes(overlayPath), Is.EqualTo(overlaySnapshot),
                "PDF-303: the overlay file is read, not rewritten.");
            Assert.That(File.ReadAllBytes(underlayPath), Is.EqualTo(underlaySnapshot),
                "PDF-303: the underlay file is read, not rewritten.");
        }

        private static (double X, double Y, double Width, double Height) ReadLayerRectangle(JsonElement layer)
        {
            var values = FixtureCatalog.ReadArray(layer, "rect")
                .Select(x => x.GetDouble())
                .ToArray();

            Assert.That(values, Has.Length.EqualTo(4),
                "PDF-303: a layer rectangle declares x, y, width and height.");

            return (values[0], values[1], values[2], values[3]);
        }

        private static byte[] CreateLayerPdf(
            (double X, double Y, double Width, double Height) rectangle,
            string color)
        {
            using var document = new PdfDocument();
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(120);
            page.Height = XUnit.FromPoint(100);

            using var graphics = XGraphics.FromPdfPage(page);
            var value = color.TrimStart('#');
            var brush = new XSolidBrush(XColor.FromArgb(
                Convert.ToInt32(value.Substring(0, 2), 16),
                Convert.ToInt32(value.Substring(2, 2), 16),
                Convert.ToInt32(value.Substring(4, 2), 16)));

            // XGraphics uses a top-left origin; the fixture rectangles use PDF page coordinates.
            graphics.DrawRectangle(
                brush,
                rectangle.X,
                page.Height.Point - rectangle.Y - rectangle.Height,
                rectangle.Width,
                rectangle.Height);

            using var buffer = new MemoryStream();
            document.Save(buffer, false);
            return buffer.ToArray();
        }

        private static string ColorOperator(JsonElement layer)
        {
            var value = layer.GetProperty("color").GetString()!.TrimStart('#');
            static string Channel(string hex) =>
                (Convert.ToInt32(hex, 16) / 255.0).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

            return $"{Channel(value.Substring(0, 2))} {Channel(value.Substring(2, 2))} {Channel(value.Substring(4, 2))} rg";
        }

        private static string SaveLayerSource(string fixtureId, string name, byte[] bytes)
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "parity-artifacts", fixtureId, "sources");
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, name);
            File.WriteAllBytes(path, bytes);
            TestContext.AddTestAttachment(path);
            return path;
        }

        private static string[] ReadLayerStreams(byte[] pdf, int pageIndex)
        {
            using var stream = new MemoryStream(pdf, writable: false);
            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

            return document.Pages[pageIndex].Contents.Elements
                .Select(ResolveLayerItem)
                .Where(x => x?.Stream?.Value != null)
                .Select(x => DecodeLayerStream(x!.Stream!.Value))
                .ToArray();
        }

        private static string ReadLayerForm(byte[] pdf, int pageIndex, string name)
        {
            using var stream = new MemoryStream(pdf, writable: false);
            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

            var resources = document.Pages[pageIndex].Elements.GetDictionary("/Resources");
            var xobjects = resources?.Elements.GetDictionary("/XObject");
            var form = ResolveLayerItem(xobjects?.Elements.GetValue(name));

            Assert.That(form, Is.Not.Null,
                $"PDF-303: page {pageIndex + 1} references the layer form {name}.");

            return DecodeLayerStream(form!.Stream!.Value);
        }

        private static int ReadRotation(byte[] pdf, int pageIndex)
        {
            using var stream = new MemoryStream(pdf, writable: false);
            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            return document.Pages[pageIndex].Rotate;
        }

        private static PdfDictionary ResolveLayerItem(PdfItem item)
        {
            return item switch
            {
                PdfReference reference => reference.Value as PdfDictionary,
                PdfDictionary dictionary => dictionary,
                _ => null
            };
        }

        private static string DecodeLayerStream(byte[] bytes)
        {
            try
            {
                using var compressed = new MemoryStream(bytes, writable: false);
                using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
                using var output = new MemoryStream();
                zlib.CopyTo(output);
                return Encoding.Latin1.GetString(output.ToArray());
            }
            catch (InvalidDataException)
            {
                return Encoding.Latin1.GetString(bytes);
            }
        }
    }
}
#endif
