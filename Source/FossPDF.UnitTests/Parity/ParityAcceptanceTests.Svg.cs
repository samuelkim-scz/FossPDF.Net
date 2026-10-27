#if NET9_0
using System;
using System.IO;
using System.Linq;
using System.Text;
using FossPDF.Drawing;
using FossPDF.Elements;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.Svg;
using FossPDF.UnitTests.TestEngine;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.UnitTests.Parity
{
    /// <summary>
    /// FP-201 acceptance: SVG input, preloading, vector output and the explicit raster fallback.
    /// The SVG companion package targets net9.0 only, so this runner is compiled for that
    /// framework; the fixture declares frameworks: ["net9.0"].
    /// </summary>
    public partial class ParityAcceptanceTests
    {
        [ParityFixtureRunner("IMG-201")]

        private static void RunSvgInput(ParityFixture fixture)
        {
            var input = fixture.Input;
            var source = input.GetProperty("svg").GetString();
            var size = input.GetProperty("size");
            var expectedSize = new Size(size[0].GetInt32(), size[1].GetInt32());

            using var image = SvgImage.FromText(source);

            Assert.That(image.Size, Is.EqualTo(expectedSize),
                "IMG-201: the declared intrinsic size is reported.");

            AssertSvgSourcesAreEquivalent(fixture, source, image);
            AssertSvgFitModes(fixture, image);
            AssertSvgPreloadedReuse(fixture, image);
            AssertSvgVectorOutput(fixture, image);
            AssertSvgRasterFallback(fixture, image, expectedSize);
            AssertSvgRejectedInputs(fixture);
            AssertSvgStreamOwnership(fixture, source, expectedSize);
            AssertSvgDisposedImageCannotDraw(fixture, source, expectedSize);
        }

        private static void AssertSvgSourcesAreEquivalent(
            ParityFixture fixture, string source, SvgImage fromText)
        {
            var textPages = RenderSvgScenario(fixture, fromText, "from-text");

            using (var fromBytes = SvgImage.FromBytes(Encoding.UTF8.GetBytes(source)))
            {
                Assert.That(RenderSvgScenario(fixture, fromBytes, "from-bytes"), Is.EqualTo(textPages),
                    "IMG-201: bytes and text produce the same drawing.");
            }

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(source)))
            using (var fromStream = SvgImage.FromStream(stream))
            {
                Assert.That(RenderSvgScenario(fixture, fromStream, "from-stream"), Is.EqualTo(textPages),
                    "IMG-201: a stream produces the same drawing.");
                Assert.That(stream.CanRead, Is.True, "IMG-201: the caller-owned stream stays open.");
                Assert.That(stream.Position, Is.EqualTo(stream.Length),
                    "IMG-201: the stream is consumed from its current position.");
            }
        }

        private static void AssertSvgFitModes(ParityFixture fixture, SvgImage image)
        {
            var modes = FixtureCatalog.ReadArray(fixture.Input, "fit_modes");

            Assert.That(modes, Is.Not.Empty, "IMG-201: fit modes must be declared.");

            foreach (var mode in modes)
            {
                var scaling = Enum.Parse<ImageScaling>(
                    mode.GetProperty("mode").GetString(), ignoreCase: false);
                var available = mode.GetProperty("available");
                var expected = mode.GetProperty("expected");

                TestPlan
                    .For(_ =>
                    {
                        var container = new Container();
                        container.Svg(image, scaling);
                        return container;
                    })
                    .MeasureElement(new Size(available[0].GetInt32(), available[1].GetInt32()))
                    .CheckMeasureResult(SpacePlan.FullRender(
                        expected[0].GetInt32(), expected[1].GetInt32()));
            }
        }

        private static void AssertSvgPreloadedReuse(ParityFixture fixture, SvgImage image)
        {
            var reuseCount = fixture.Input.GetProperty("reuse_count").GetInt32();
            var document = CreateReuseDocument(fixture, image, reuseCount);

            var pdf = document.GeneratePdf(out var count);
            var pages = document.GenerateImages().ToArray();

            Assert.That(Encoding.ASCII.GetString(pdf, 0, 5), Is.EqualTo("%PDF-"));
            Assert.That(count, Is.EqualTo(reuseCount), "IMG-201: every page is emitted.");
            Assert.That(pages.Length, Is.EqualTo(reuseCount));

            SaveArtifact(fixture.Id, "scenario-reuse/document.pdf", pdf);

            for (var index = 0; index < pages.Length; index++)
            {
                SaveArtifact(fixture.Id, $"scenario-reuse/page-{index + 1}.png", pages[index]);
                Assert.That(pages[index], Is.EqualTo(pages[0]),
                    $"IMG-201: reuse on page {index + 1} must not consume or alter the parsed image.");
            }

            using var bitmap = SKBitmap.Decode(pages[0]);
            Assert.That(bitmap, Is.Not.Null);

            var width = bitmap.Width;
            var height = bitmap.Height;

            AssertPixelColor(bitmap, width / 4, height / 2, "#ff0000", "reuse left half");
            AssertPixelColor(bitmap, width * 3 / 4, height / 2, "#0000ff", "reuse right half");
            Assert.That(CountPixels(bitmap, color => color.Red > 200 && color.Blue < 60),
                Is.GreaterThan(width * height / 4),
                "IMG-201: the replayed geometry keeps the red half.");
        }

        private static void AssertSvgVectorOutput(ParityFixture fixture, SvgImage image)
        {
            var document = CreatePage(fixture, page => page.Content().Svg(image));
            var pdf = document.GeneratePdf();

            SaveArtifact(fixture.Id, "scenario-vector/document.pdf", pdf);

            var pdfText = Encoding.Latin1.GetString(pdf);
            Assert.That(pdfText, Does.Not.Contain("/Subtype /Image"),
                "IMG-201: vector replay must not embed an image XObject.");

            var content = DecodePdfStreams(pdf);
            Assert.That(content, Does.Contain(" re"), "IMG-201: the SVG rectangles are replayed as paths.");
            Assert.That(content, Does.Contain(" rg"), "IMG-201: SVG fills are replayed as vector paint.");
        }

        private static void AssertSvgRasterFallback(
            ParityFixture fixture, SvgImage image, Size expectedSize)
        {
            var dpi = fixture.Input.GetProperty("raster_dpi").GetInt32();
            var document = CreatePage(fixture, page =>
                page.Content().SvgRasterized(image, new SvgRasterizationOptions { Dpi = dpi }));

            var pdf = document.GeneratePdf();
            var pages = document.GenerateImages().ToArray();

            SaveArtifact(fixture.Id, "scenario-raster/document.pdf", pdf);
            SaveArtifact(fixture.Id, "scenario-raster/page-1.png", pages.Single());

            var pdfText = Encoding.Latin1.GetString(pdf);
            Assert.That(pdfText, Does.Contain("/Subtype /Image"),
                "IMG-201: the raster fallback embeds an image XObject.");

            // The image is rendered at the requested DPI before it is placed on the page.
            var page = fixture.Input.GetProperty("page");
            var margin = fixture.Input.GetProperty("margin").GetInt32();
            var availableWidth = page[0].GetInt32() - 2 * margin;
            var availableHeight = page[1].GetInt32() - 2 * margin;
            var scale = dpi / 72f;
            var imageWidth = (int)Math.Ceiling(availableWidth * scale);
            var imageHeight = (int)Math.Ceiling(availableHeight * scale);

            Assert.That(pdfText, Does.Contain($"/Width {imageWidth}"));
            Assert.That(pdfText, Does.Contain($"/Height {imageHeight}"));

            using var bitmap = SKBitmap.Decode(pages.Single());
            Assert.That(bitmap, Is.Not.Null);
            Assert.That(bitmap.Width, Is.EqualTo(availableWidth));
            Assert.That(bitmap.Height, Is.EqualTo(availableHeight));
            AssertPixelColor(bitmap, bitmap.Width / 4, bitmap.Height / 2, "#ff0000", "raster left half");
            AssertPixelColor(bitmap, bitmap.Width * 3 / 4, bitmap.Height / 2, "#0000ff", "raster right half");

            Assert.That(expectedSize.Width, Is.GreaterThan(0));
        }

        private static void AssertSvgRejectedInputs(ParityFixture fixture)
        {
            var rejected = FixtureCatalog.ReadArray(fixture.Input, "rejected_inputs");

            Assert.That(rejected, Is.Not.Empty, "IMG-201: rejection cases must be declared.");

            foreach (var item in rejected)
            {
                var source = item.GetString();

                Assert.That(() => SvgImage.FromText(source), Throws.TypeOf<SvgParseException>(),
                    "IMG-201: unsupported input must fail predictably.");
            }
        }

        private static void AssertSvgStreamOwnership(
            ParityFixture fixture, string source, Size expectedSize)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(source)))
            {
                using (var image = SvgImage.FromStream(stream))
                {
                    Assert.That(image.Size, Is.EqualTo(expectedSize));
                }

                Assert.That(stream.CanRead, Is.True,
                    "IMG-201: a successfully parsed stream is not disposed.");
            }

            using (var invalid = new MemoryStream(Encoding.UTF8.GetBytes("<svg><broken></svg>")))
            {
                Assert.Throws<SvgParseException>(() => SvgImage.FromStream(invalid),
                    "IMG-201: malformed stream input is rejected.");
                Assert.That(invalid.CanRead, Is.True,
                    "IMG-201: a rejected stream is left open for the caller.");
            }
        }

        private static void AssertSvgDisposedImageCannotDraw(
            ParityFixture fixture, string source, Size expectedSize)
        {
            var disposable = SvgImage.FromText(source);
            disposable.Dispose();
            disposable.Dispose();

            using var bitmap = new SKBitmap(
                (int)expectedSize.Width, (int)expectedSize.Height);
            using var canvas = new SKCanvas(bitmap);

            Assert.That(() => disposable.Draw(canvas, expectedSize),
                Throws.TypeOf<ObjectDisposedException>(),
                "IMG-201: disposing the preloaded image invalidates later drawing.");

            Assert.That(fixture.Input.GetProperty("reuse_count").GetInt32(), Is.GreaterThan(0));
        }

        private static byte[] RenderSvgScenario(ParityFixture fixture, SvgImage image, string scenario)
        {
            var document = CreatePage(fixture, page => page.Content().Svg(image));
            var pdf = document.GeneratePdf(out var count);
            var pages = document.GenerateImages().ToArray();

            Assert.That(count, Is.EqualTo(1));
            Assert.That(pages.Length, Is.EqualTo(1));

            SaveArtifact(fixture.Id, $"scenario-{scenario}/document.pdf", pdf);
            SaveArtifact(fixture.Id, $"scenario-{scenario}/page-1.png", pages[0]);

            return pages[0];
        }

        private static Document CreateReuseDocument(ParityFixture fixture, SvgImage image, int pageCount)
        {
            var pageSize = fixture.Input.GetProperty("page");
            var margin = fixture.Input.GetProperty("margin").GetInt32();

            return Document.Create(container =>
            {
                for (var index = 0; index < pageCount; index++)
                {
                    container.Page(page =>
                    {
                        page.Size(pageSize[0].GetInt32(), pageSize[1].GetInt32());
                        page.Margin(margin);
                        page.PageColor("#ffffff");
                        page.Content().Svg(image);
                    });
                }
            }).WithMetadata(new DocumentMetadata
            {
                Title = "FossPDF independent parity fixture IMG-201",
                RasterDpi = 72,
                CreationDate = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ModifiedDate = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        }
    }
}
#endif
