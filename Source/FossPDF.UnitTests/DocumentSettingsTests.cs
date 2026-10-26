using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FossPDF.Drawing;
using FossPDF.Drawing.Exceptions;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.UnitTests
{
    [NonParallelizable]
    [Category("DocumentSettings")]
    public class DocumentSettingsTests
    {
        [Test]
        public void LegacyMetadataAndDefaultsRemainAvailable()
        {
            var metadata = new DocumentMetadata { RasterDpi = 144, ImageQuality = 85, PdfA = true };
            IDocument document = new LegacyDocument(metadata);
            var configured = document.WithSettings(DocumentSettings.Default);
            var configuration = GenerationConfiguration.Resolve(configured);

            Assert.That(configuration.Metadata.RasterDpi, Is.EqualTo(144));
            Assert.That(configuration.Metadata.ImageQuality, Is.EqualTo(85));
            Assert.That(configuration.Metadata.PdfA, Is.True);
            Assert.That(configuration.Metadata, Is.Not.SameAs(metadata));
            Assert.That(configured.GetMetadata(), Is.SameAs(metadata));
            using var bitmap = SKBitmap.Decode(configured.GenerateImages().Single());
            Assert.That(bitmap.Width, Is.EqualTo(240));
        }

        [Test]
        public void ExplicitOverridesWinWithoutMutatingSharedMetadata()
        {
            var metadata = new DocumentMetadata { RasterDpi = 36, ImageQuality = 70, PdfA = true };
            var configured = new LegacyDocument(metadata).WithSettings(new DocumentSettings
            {
                ImageRasterDpi = 144,
                ImageCompressionQuality = 95,
                PdfAConformance = PdfAConformance.None
            });
            var configuration = GenerationConfiguration.Resolve(configured);
            Assert.That(configuration.Metadata.RasterDpi, Is.EqualTo(144));
            Assert.That(configuration.Metadata.ImageQuality, Is.EqualTo(95));
            Assert.That(configuration.Metadata.PdfA, Is.False);
            configured.GeneratePdf();
            Assert.That(metadata.RasterDpi, Is.EqualTo(36));
            Assert.That(metadata.ImageQuality, Is.EqualTo(70));
            Assert.That(metadata.PdfA, Is.True);
        }

        [Test]
        public void ImageExportOverrideDoesNotChangeSubsequentDocumentExports()
        {
            var document = MarkerDocument().WithSettings(new DocumentSettings { ImageRasterDpi = 144 });
            using var thumbnail = SKBitmap.Decode(document.GenerateImages(
                new ImageGenerationSettings { RasterDpi = 72 }).Single());
            using var fullSize = SKBitmap.Decode(document.GenerateImages().Single());
            Assert.That(thumbnail.Width, Is.EqualTo(120));
            Assert.That(fullSize.Width, Is.EqualTo(240));
            Assert.That(document.GetMetadata().RasterDpi, Is.EqualTo(72));
        }

        [Test]
        public void MetadataIsReadOnceAndSnapshottedBeforeComposition()
        {
            var metadata = new DocumentMetadata { RasterDpi = 72 };
            var source = new LegacyDocument(metadata, () => metadata.RasterDpi = 144);
            using var bitmap = SKBitmap.Decode(source.GenerateImages().Single());
            Assert.That(source.MetadataReads, Is.EqualTo(1));
            Assert.That(bitmap.Width, Is.EqualTo(120));
            Assert.That(metadata.RasterDpi, Is.EqualTo(144));
        }

        [Test]
        public void ReplacingSettingsOnLegacyDocumentsUsesOnlyTheLatestOverride()
        {
            IDocument source = new LegacyDocument(new DocumentMetadata { RasterDpi = 72 });
            var first = source.WithSettings(new DocumentSettings { ImageRasterDpi = 144 });
            var second = first.WithSettings(new DocumentSettings { ImageRasterDpi = 216 });
            using var sourceImage = SKBitmap.Decode(source.GenerateImages().Single());
            using var firstImage = SKBitmap.Decode(first.GenerateImages().Single());
            using var secondImage = SKBitmap.Decode(second.GenerateImages().Single());
            Assert.That(sourceImage.Width, Is.EqualTo(120));
            Assert.That(firstImage.Width, Is.EqualTo(240));
            Assert.That(secondImage.Width, Is.EqualTo(360));
        }

        [Test]
        public void ProcessDefaultsAreCapturedBeforeComposition()
        {
            var previousLimit = Settings.DocumentLayoutExceptionThreshold;
            var previousGlyphs = Settings.CheckIfAllTextGlyphsAreAvailable;
            try
            {
                Settings.DocumentLayoutExceptionThreshold = 2;
                Settings.CheckIfAllTextGlyphsAreAvailable = false;
                var document = Document.Create(container =>
                {
                    Settings.DocumentLayoutExceptionThreshold = 1;
                    Settings.CheckIfAllTextGlyphsAreAvailable = true;
                    container.Page(page => page.Content().Text(char.ConvertFromUtf32(0x10ffff)));
                    container.Page(page => page.Content().Text("Second page"));
                });
                document.GeneratePdf(out var count);
                Assert.That(count, Is.EqualTo(2));
            }
            finally
            {
                Settings.DocumentLayoutExceptionThreshold = previousLimit;
                Settings.CheckIfAllTextGlyphsAreAvailable = previousGlyphs;
            }
        }

        [Test]
        public void NullOptionsAreRejected()
        {
            var document = MarkerDocument();
            IDocument legacy = new LegacyDocument(DocumentMetadata.Default);
            Assert.Throws<ArgumentNullException>(() => document.WithSettings(null));
            Assert.Throws<ArgumentNullException>(() => legacy.WithSettings(null));
            Assert.Throws<ArgumentNullException>(() => document.GenerateImages((ImageGenerationSettings)null));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ConcurrentSettingsKeepDpiDirectionAndRuntimeOptionsIsolated(bool reverse)
        {
            var globalCaching = Settings.EnableCaching;
            var globalDebugging = Settings.EnableDebugging;
            var globalGlyphs = Settings.CheckIfAllTextGlyphsAreAvailable;
            var globalLimit = Settings.DocumentLayoutExceptionThreshold;
            var metadata = new DocumentMetadata { RasterDpi = 72, ImageQuality = 101 };
            using var barrier = new Barrier(2);

            Document Make(int dpi, ContentDirection direction, bool caching, bool debugging) =>
                MarkerDocument(beforeCompose: () =>
                {
                    if (!barrier.SignalAndWait(TimeSpan.FromSeconds(20)))
                        throw new TimeoutException("Concurrent fixture did not reach composition.");
                }).WithMetadata(metadata).WithSettings(new DocumentSettings
                {
                    ImageRasterDpi = dpi,
                    ContentDirection = direction,
                    EnableCaching = caching,
                    EnableDebugging = debugging,
                    CheckIfAllTextGlyphsAreAvailable = !caching,
                    DocumentLayoutExceptionThreshold = 1
                });

            var left = Make(72, ContentDirection.LeftToRight, reverse, !reverse);
            var right = Make(144, ContentDirection.RightToLeft, !reverse, reverse);
            var images = await Task.WhenAll(
                Task.Run(() => left.GenerateImages().Single()),
                Task.Run(() => right.GenerateImages().Single()));

            using var leftBitmap = SKBitmap.Decode(images[0]);
            using var rightBitmap = SKBitmap.Decode(images[1]);
            Assert.That(leftBitmap.Width, Is.EqualTo(120));
            Assert.That(rightBitmap.Width, Is.EqualTo(240));
            Assert.That(leftBitmap.GetPixel(15, 15), Is.EqualTo(SKColors.Red));
            Assert.That(leftBitmap.GetPixel(35, 15), Is.EqualTo(SKColors.Blue));
            Assert.That(rightBitmap.GetPixel(155, 30), Is.EqualTo(SKColors.Blue));
            Assert.That(rightBitmap.GetPixel(195, 30), Is.EqualTo(SKColors.Red));
            Assert.That(rightBitmap.GetPixel(30, 30), Is.EqualTo(SKColors.White));
            Assert.That(metadata.RasterDpi, Is.EqualTo(72));
            Assert.That(metadata.ImageQuality, Is.EqualTo(101));
            Assert.That(Settings.EnableCaching, Is.EqualTo(globalCaching));
            Assert.That(Settings.EnableDebugging, Is.EqualTo(globalDebugging));
            Assert.That(Settings.CheckIfAllTextGlyphsAreAvailable, Is.EqualTo(globalGlyphs));
            Assert.That(Settings.DocumentLayoutExceptionThreshold, Is.EqualTo(globalLimit));
        }

        [Test]
        public void ExplicitPageDirectionOverridesDocumentDirection()
        {
            var document = MarkerDocument(explicitPageDirection: true).WithSettings(new DocumentSettings
            {
                ContentDirection = ContentDirection.RightToLeft
            });
            using var bitmap = SKBitmap.Decode(document.GenerateImages().Single());
            Assert.That(bitmap.GetPixel(15, 15), Is.EqualTo(SKColors.Red));
            Assert.That(bitmap.GetPixel(35, 15), Is.EqualTo(SKColors.Blue));
        }

        [Test]
        public void ExplicitElementDirectionOverridesDocumentDirection()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(120, 80);
                page.Margin(10);
                page.Content().ContentFromLeftToRight().Row(row =>
                {
                    row.ConstantItem(20).Height(20).Background("#ff0000");
                    row.ConstantItem(20).Height(20).Background("#0000ff");
                });
            })).WithSettings(new DocumentSettings { ContentDirection = ContentDirection.RightToLeft });
            using var bitmap = SKBitmap.Decode(document.GenerateImages().Single());
            Assert.That(bitmap.GetPixel(15, 15), Is.EqualTo(SKColors.Red));
            Assert.That(bitmap.GetPixel(35, 15), Is.EqualTo(SKColors.Blue));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void PageLimitAllowsExactlyTheDeclaredMaximum(int pageCount)
        {
            var document = Pages(pageCount).WithSettings(new DocumentSettings
            {
                DocumentLayoutExceptionThreshold = pageCount
            });
            document.GeneratePdf(out var actual);
            Assert.That(actual, Is.EqualTo(pageCount));
        }

        [Test]
        public async Task ConcurrentPageLimitsDoNotAffectOtherDocuments()
        {
            var allowed = Pages(2).WithSettings(new DocumentSettings { DocumentLayoutExceptionThreshold = 2 });
            var limited = Pages(2).WithSettings(new DocumentSettings { DocumentLayoutExceptionThreshold = 1 });
            await Task.WhenAll(
                Task.Run(() =>
                {
                    allowed.GeneratePdf(out var count);
                    Assert.That(count, Is.EqualTo(2));
                }),
                Task.Run(() => Assert.Throws<DocumentLayoutException>(() => limited.GeneratePdf())));
        }

        [Test]
        public async Task ConcurrentGlyphChecksDoNotUseEachOthersPolicy()
        {
            var text = char.ConvertFromUtf32(0x10ffff);
            Document MissingGlyph(bool check) => Document.Create(container =>
                container.Page(page => page.Content().Text(text))).WithSettings(new DocumentSettings
                {
                    CheckIfAllTextGlyphsAreAvailable = check,
                    EnableDebugging = false
                });
            await Task.WhenAll(
                Task.Run(() => Assert.Throws<DocumentDrawingException>(() => MissingGlyph(true).GeneratePdf())),
                Task.Run(() => Assert.DoesNotThrow(() => MissingGlyph(false).GeneratePdf())));
        }

        public static IEnumerable<TestCaseData> InvalidSettings()
        {
            yield return Invalid("ZeroDpi", new DocumentSettings { ImageRasterDpi = 0 });
            yield return Invalid("NegativeDpi", new DocumentSettings { ImageRasterDpi = -1 });
            yield return Invalid("NegativeQuality", new DocumentSettings { ImageCompressionQuality = -1 });
            yield return Invalid("ExcessQuality", new DocumentSettings { ImageCompressionQuality = 102 });
            yield return Invalid("ZeroPageLimit", new DocumentSettings { DocumentLayoutExceptionThreshold = 0 });
            yield return Invalid("UnknownDirection", new DocumentSettings { ContentDirection = (ContentDirection)99 });
            yield return Invalid("UnknownPdfA", new DocumentSettings { PdfAConformance = (PdfAConformance)99 });
            yield return Invalid("UnknownPdfUa", new DocumentSettings { PdfUaConformance = (PdfUaConformance)99 });

            static TestCaseData Invalid(string name, DocumentSettings settings) =>
                new TestCaseData(settings).SetName("InvalidSettings_" + name);
        }

        [TestCaseSource(nameof(InvalidSettings))]
        public void InvalidOptionsFailBeforeCompositionOrWriting(DocumentSettings settings)
        {
            var composed = false;
            var document = MarkerDocument(() => composed = true).WithSettings(settings);
            using var stream = new MemoryStream();
            stream.WriteByte(42);
            Assert.Throws<ArgumentOutOfRangeException>(() => document.GeneratePdf(stream));
            Assert.That(composed, Is.False);
            Assert.That(stream.ToArray(), Is.EqualTo(new byte[] { 42 }));
        }

        [TestCase(PdfAConformance.PdfA2A)]
        [TestCase(PdfAConformance.PdfA2B)]
        [TestCase(PdfAConformance.PdfA2U)]
        [TestCase(PdfAConformance.PdfA3A)]
        [TestCase(PdfAConformance.PdfA3B)]
        [TestCase(PdfAConformance.PdfA3U)]
        public void SelectablePdfAProfilesFailBeforeWritingUntilTheirBackendIsImplemented(PdfAConformance profile)
        {
            AssertUnsupported(new DocumentSettings { PdfAConformance = profile });
        }

        [Test]
        public void PdfUaFailsBeforeWritingUntilSemanticTaggingIsImplemented() =>
            AssertUnsupported(new DocumentSettings { PdfUaConformance = PdfUaConformance.PdfUa1 });

        [TestCase("disable")]
        [TestCase("extra")]
        [TestCase("linearize")]
        public void CompressionOverridesFailBeforeWritingUntilSupported(string mode) =>
            AssertUnsupported(new DocumentSettings
            {
                CompressDocument = mode != "disable",
                EnableAdditionalCompression = mode == "extra",
                Linearize = mode == "linearize"
            });

        [TestCase(ImageOutputFormat.Jpeg)]
        [TestCase(ImageOutputFormat.Webp)]
        public void UnavailableImageFormatsFailBeforeCompositionAndPathSelection(ImageOutputFormat format)
        {
            var composed = false;
            var pathSelected = false;
            var document = MarkerDocument(() => composed = true);
            Assert.Throws<NotSupportedException>(() => document.GenerateImages(
                _ => { pathSelected = true; return "must-not-be-written"; },
                new ImageGenerationSettings { Format = format }));
            Assert.That(composed, Is.False);
            Assert.That(pathSelected, Is.False);
        }

        [TestCase(0, 100, ImageOutputFormat.Png)]
        [TestCase(-1, 100, ImageOutputFormat.Png)]
        [TestCase(72, -1, ImageOutputFormat.Png)]
        [TestCase(72, 101, ImageOutputFormat.Png)]
        [TestCase(72, 100, (ImageOutputFormat)99)]
        public void InvalidImageOptionsAreRejected(int dpi, int quality, ImageOutputFormat format)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MarkerDocument().GenerateImages(
                new ImageGenerationSettings { RasterDpi = dpi, CompressionQuality = quality, Format = format }));
        }

        [Test]
        public void PdfOnlyOptionsDoNotPreventIndependentPngExport()
        {
            var document = MarkerDocument().WithSettings(new DocumentSettings
            {
                PdfAConformance = PdfAConformance.PdfA3A,
                Linearize = true
            });
            Assert.That(document.GenerateImages().Count(), Is.EqualTo(1));
        }

        [Test]
        public void SuccessfulGenerationLeavesCallerStreamOpen()
        {
            using var stream = new MemoryStream();
            MarkerDocument().WithSettings(new DocumentSettings { ImageRasterDpi = 144 }).GeneratePdf(stream);
            Assert.That(stream.CanWrite, Is.True);
            Assert.That(Encoding.ASCII.GetString(stream.ToArray(), 0, 5), Is.EqualTo("%PDF-"));
            Assert.DoesNotThrow(() => stream.WriteByte(0));
        }

        private static void AssertUnsupported(DocumentSettings settings)
        {
            var composed = false;
            using var stream = new MemoryStream();
            stream.WriteByte(42);
            Assert.Throws<NotSupportedException>(() =>
                MarkerDocument(() => composed = true).WithSettings(settings).GeneratePdf(stream));
            Assert.That(composed, Is.False);
            Assert.That(stream.ToArray(), Is.EqualTo(new byte[] { 42 }));
        }

        private static Document MarkerDocument(Action beforeCompose = null, bool explicitPageDirection = false) =>
            Document.Create(container =>
            {
                beforeCompose?.Invoke();
                container.Page(page =>
                {
                    page.Size(120, 80);
                    page.Margin(10);
                    page.PageColor("#ffffff");
                    if (explicitPageDirection)
                        page.ContentFromLeftToRight();
                    page.Content().Row(row =>
                    {
                        row.ConstantItem(20).Height(20).Background("#ff0000");
                        row.ConstantItem(20).Height(20).Background("#0000ff");
                    });
                });
            });

        private static Document Pages(int count) => Document.Create(container =>
        {
            for (var index = 0; index < count; index++)
                container.Page(page => page.Content().Text("Page " + index));
        });

        // Represents an existing consumer compiled against the unchanged two-member IDocument.
        private sealed class LegacyDocument : IDocument
        {
            private readonly DocumentMetadata Metadata;
            private readonly Action BeforeCompose;
            internal int MetadataReads { get; private set; }

            internal LegacyDocument(DocumentMetadata metadata, Action beforeCompose = null)
            {
                Metadata = metadata;
                BeforeCompose = beforeCompose;
            }

            public DocumentMetadata GetMetadata()
            {
                MetadataReads++;
                return Metadata;
            }

            public void Compose(IDocumentContainer container)
            {
                BeforeCompose?.Invoke();
                container.Page(page =>
                {
                    page.Size(120, 80);
                    page.Content().Text("Legacy IDocument");
                });
            }
        }
    }
}
