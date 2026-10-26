using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FossPDF.Drawing.Exceptions;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;
using SkiaSharp;
using SharedImage = FossPDF.Infrastructure.Image;

namespace FossPDF.UnitTests
{
    [NonParallelizable]
    [Category("ImageExport")]
    public class ImageExportTests
    {
        [TestCase(ImageOutputFormat.Png, 72)]
        [TestCase(ImageOutputFormat.Jpeg, 72)]
        [TestCase(ImageOutputFormat.Webp, 72)]
        [TestCase(ImageOutputFormat.Png, 144)]
        [TestCase(ImageOutputFormat.Jpeg, 144)]
        [TestCase(ImageOutputFormat.Webp, 144)]
        public void FormatsDecodeWithRequestedDpiAndPageOrder(ImageOutputFormat format, int dpi)
        {
            var pages = TwoPages().GenerateImages(new ImageGenerationSettings
            {
                Format = format, RasterDpi = dpi, CompressionQuality = 90
            }).ToArray();
            Assert.That(pages.Length, Is.EqualTo(2));
            for (var page = 0; page < pages.Length; page++)
            {
                using var data = SKData.CreateCopy(pages[page]);
                using var codec = SKCodec.Create(data);
                Assert.That(codec.EncodedFormat, Is.EqualTo(EncodedFormat(format)));
                using var image = SKBitmap.Decode(pages[page]);
                Assert.That(image.Width, Is.EqualTo(80 * dpi / 72));
                Assert.That(image.Height, Is.EqualTo(60 * dpi / 72));
                AssertColor(image.GetPixel(image.Width / 2, image.Height / 2),
                    page == 0 ? SKColors.Red : SKColors.Blue, format == ImageOutputFormat.Png ? 0 : 8);
            }
        }

        [TestCase(ImageOutputFormat.Png, 100)]
        [TestCase(ImageOutputFormat.Webp, 100)]
        [TestCase(ImageOutputFormat.Webp, 50)]
        public void PngAndWebpPreserveTransparentAndSemitransparentPixels(ImageOutputFormat format, int quality)
        {
            using var image = SKBitmap.Decode(AlphaPage().GenerateImages(new ImageGenerationSettings
            {
                Format = format, CompressionQuality = quality
            }).Single());
            Assert.That(image.GetPixel(56, 56).Alpha, Is.Zero);
            var foreground = image.GetPixel(16, 16);
            Assert.That(foreground.Alpha, Is.InRange(127, 129));
            AssertColor(foreground, new SKColor(255, 0, 0, 128), quality == 100 ? 1 : 8);
        }

        [TestCase(null, 255, 255, 255, 255, 127, 127)]
        [TestCase("#0000ff", 0, 0, 255, 128, 0, 127)]
        public void JpegFlattensAlphaOverAnOpaqueBackground(string background, byte r, byte g, byte b,
            byte foregroundR, byte foregroundG, byte foregroundB)
        {
            using var image = SKBitmap.Decode(AlphaPage().GenerateImages(new ImageGenerationSettings
            {
                Format = ImageOutputFormat.Jpeg, BackgroundColor = background
            }).Single());
            AssertColor(image.GetPixel(56, 56), new SKColor(r, g, b), 3);
            AssertColor(image.GetPixel(16, 16), new SKColor(foregroundR, foregroundG, foregroundB), 4);
            Assert.That(image.GetPixel(16, 16).Alpha, Is.EqualTo(255));
        }

        [TestCase(ImageOutputFormat.Png)]
        [TestCase(ImageOutputFormat.Webp)]
        public void AlphaFormatsSupportTranslucentOutputBackgrounds(ImageOutputFormat format)
        {
            using var image = SKBitmap.Decode(AlphaPage().GenerateImages(new ImageGenerationSettings
            {
                Format = format, BackgroundColor = "#400000ff"
            }).Single());
            AssertColor(image.GetPixel(56, 56), new SKColor(0, 0, 255, 64), 1);
            AssertColor(image.GetPixel(16, 16), new SKColor(204, 0, 51, 160), 2);
        }

        [TestCase(ImageOutputFormat.Png)]
        [TestCase(ImageOutputFormat.Jpeg)]
        [TestCase(ImageOutputFormat.Webp)]
        public void ExplicitPageColorPaintsOverTheExportBackground(ImageOutputFormat format)
        {
            using var image = SKBitmap.Decode(AlphaPage(whitePage: true).GenerateImages(new ImageGenerationSettings
            {
                Format = format, BackgroundColor = "#0000ff"
            }).Single());
            AssertColor(image.GetPixel(56, 56), SKColors.White, 2);
        }

        [TestCase("#00ffffff")]
        [TestCase("#80ffffff")]
        [TestCase("not-a-color")]
        public void InvalidJpegBackgroundFailsBeforeCompositionAndStreamSelection(string background)
        {
            var composed = false;
            var selected = false;
            var document = Document.Create(container => { composed = true; container.Page(page => page.Content().Text("unused")); });
            Assert.Throws<ArgumentException>(() => document.GenerateImagesToStreams(
                _ => { selected = true; return Stream.Null; },
                new ImageGenerationSettings { Format = ImageOutputFormat.Jpeg, BackgroundColor = background }));
            Assert.That(composed, Is.False);
            Assert.That(selected, Is.False);
        }

        [Test]
        public void DefaultPngAndIgnoredPngQualityRetainIdenticalBytes()
        {
            var document = AlphaPage();
            var original = document.GenerateImages().Single();
            Assert.That(document.GenerateImages(new ImageGenerationSettings
                { Format = ImageOutputFormat.Png, CompressionQuality = 0 }).Single(), Is.EqualTo(original));
            Assert.That(document.GenerateImages(new ImageGenerationSettings
                { Format = ImageOutputFormat.Png, BackgroundColor = "#00000000" }).Single(), Is.EqualTo(original));
        }

        [TestCase(ImageOutputFormat.Jpeg)]
        [TestCase(ImageOutputFormat.Webp)]
        public void HigherLossyQualityPreservesMoreOfTheAuthoredGrayscaleSignal(ImageOutputFormat format)
        {
            using var reference = Signal();
            using var source = SKImage.FromBitmap(reference);
            using var encoded = source.Encode(SKEncodedImageFormat.Png, 100);
            using var shared = SharedImage.FromBinaryData(encoded.ToArray());
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(256, 128);
                page.Content().Image(shared);
            }));
            var low = document.GenerateImages(new ImageGenerationSettings { Format = format, CompressionQuality = 10 }).Single();
            var high = document.GenerateImages(new ImageGenerationSettings { Format = format, CompressionQuality = 90 }).Single();
            using var lowImage = SKBitmap.Decode(low);
            using var highImage = SKBitmap.Decode(high);
            Assert.That(Error(reference, highImage), Is.LessThan(Error(reference, lowImage)));
            Assert.That(high, Is.Not.EqualTo(low));
            Save("quality-" + format + "-10", low, format);
            Save("quality-" + format + "-90", high, format);

            if (format == ImageOutputFormat.Webp)
            {
                var lossless = document.GenerateImages(new ImageGenerationSettings
                    { Format = format, CompressionQuality = 100 }).Single();
                using var losslessImage = SKBitmap.Decode(lossless);
                Assert.That(Error(reference, losslessImage), Is.Zero);
                Save("quality-Webp-100", lossless, format);
            }
        }

        [TestCase(ImageOutputFormat.Png)]
        [TestCase(ImageOutputFormat.Jpeg)]
        [TestCase(ImageOutputFormat.Webp)]
        public void FilesAndCallerOwnedNonSeekingStreamsMatchByteArrayOrder(ImageOutputFormat format)
        {
            var document = TwoPages();
            var settings = new ImageGenerationSettings { Format = format, CompressionQuality = 90 };
            var expected = document.GenerateImages(settings).ToArray();
            var directory = Path.Combine(AppContext.BaseDirectory, "parity-artifacts", "OUT-203-files", format.ToString());
            Directory.CreateDirectory(directory);
            document.GenerateImages(index => Path.Combine(directory, index + "." + format), settings);
            for (var index = 0; index < expected.Length; index++)
                Assert.That(File.ReadAllBytes(Path.Combine(directory, index + "." + format)), Is.EqualTo(expected[index]));

            using var first = new NonSeekingOutput();
            using var second = new NonSeekingOutput();
            first.WriteByte(42);
            second.WriteByte(43);
            var indices = new System.Collections.Generic.List<int>();
            document.GenerateImagesToStreams(index =>
            {
                indices.Add(index);
                return index == 0 ? first : second;
            }, settings);
            Assert.That(indices, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(first.Bytes.Skip(1), Is.EqualTo(expected[0]));
            Assert.That(second.Bytes.Skip(1), Is.EqualTo(expected[1]));
            Assert.That(first.Bytes[0], Is.EqualTo(42));
            Assert.That(second.Bytes[0], Is.EqualTo(43));
            Assert.That(first.WasDisposed || second.WasDisposed, Is.False);
        }

        [Test]
        public void StreamsStayOpenWhenWritingFails()
        {
            using var stream = new NonSeekingOutput { FailWrites = true };
            Assert.Throws<IOException>(() => WriteToBorrowedStream(TwoPages(), stream));
            Assert.That(stream.WasDisposed, Is.False);
        }

        [Test]
        public void StreamSelectorsMustReturnWritableStreams()
        {
            using var readOnly = new MemoryStream(new byte[] { 42 }, writable: false);
            Assert.Throws<ArgumentException>(() => WriteToBorrowedStream(TwoPages(), readOnly));
            Assert.That(readOnly.CanRead, Is.True);
            Assert.Throws<ArgumentException>(() => TwoPages().GenerateImagesToStreams(_ => null));
            Assert.Throws<ArgumentNullException>(() => TwoPages().GenerateImagesToStreams(null));
        }

        [TestCase(16384, 2)]
        [TestCase(2, 16384)]
        public void WebpRejectsOversizedPagesBeforeAllocatingTheirRaster(int width, int height)
        {
            var document = Document.Create(container => container.Page(page => page.Size(width / 2f, height / 2f)));
            var error = Assert.Throws<DocumentDrawingException>(() => document.GenerateImages(
                new ImageGenerationSettings { Format = ImageOutputFormat.Webp, RasterDpi = 144 }));
            Assert.That(error.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(error.InnerException.Message, Does.Contain("16383"));
        }

        [Test]
        public void WebpAcceptsItsMaximumAxisDimension()
        {
            var document = Document.Create(container => container.Page(page => page.Size(16383 / 2f, 1)));
            using var image = SKBitmap.Decode(document.GenerateImages(new ImageGenerationSettings
                { Format = ImageOutputFormat.Webp, RasterDpi = 144 }).Single());
            Assert.That(image.Width, Is.EqualTo(16383));
            Assert.That(image.Height, Is.EqualTo(2));
        }

        [Test]
        public async Task ConcurrentFormatsKeepIndependentBackgrounds()
        {
            var document = AlphaPage();
            var pages = await Task.WhenAll(
                Task.Run(() => document.GenerateImages(new ImageGenerationSettings { Format = ImageOutputFormat.Png }).Single()),
                Task.Run(() => document.GenerateImages(new ImageGenerationSettings { Format = ImageOutputFormat.Jpeg }).Single()));
            using var png = SKBitmap.Decode(pages[0]);
            using var jpeg = SKBitmap.Decode(pages[1]);
            Assert.That(png.GetPixel(56, 56).Alpha, Is.Zero);
            AssertColor(jpeg.GetPixel(56, 56), SKColors.White, 2);
        }

        private static void WriteToBorrowedStream(IDocument document, Stream stream) =>
            document.GenerateImagesToStreams(_ => stream);

        private static Document TwoPages() => Document.Create(container =>
        {
            foreach (var color in new[] { "#ff0000", "#0000ff" })
                container.Page(page =>
                {
                    page.Size(80, 60);
                    page.PageColor(color);
                });
        });

        private static Document AlphaPage(bool whitePage = false) => Document.Create(container => container.Page(page =>
        {
            page.Size(64, 64);
            page.Margin(8);
            if (whitePage) page.PageColor("#ffffff");
            page.Content().Width(24).Height(24).Background("#80ff0000");
        }));

        private static SKEncodedImageFormat EncodedFormat(ImageOutputFormat format) => format switch
        {
            ImageOutputFormat.Png => SKEncodedImageFormat.Png,
            ImageOutputFormat.Jpeg => SKEncodedImageFormat.Jpeg,
            _ => SKEncodedImageFormat.Webp
        };

        private static SKBitmap Signal()
        {
            var bitmap = new SKBitmap(256, 128);
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var value = (byte)((x * 29 + y * 17 + (x * y) % 31) % 256);
                bitmap.SetPixel(x, y, new SKColor(value, value, value));
            }
            return bitmap;
        }

        private static double Error(SKBitmap reference, SKBitmap actual)
        {
            long sum = 0;
            for (var y = 0; y < reference.Height; y++)
            for (var x = 0; x < reference.Width; x++)
                sum += Math.Abs(reference.GetPixel(x, y).Red - actual.GetPixel(x, y).Red);
            return sum / (double)(reference.Width * reference.Height);
        }

        private static void AssertColor(SKColor actual, SKColor expected, int tolerance)
        {
            Assert.That((int)actual.Red, Is.EqualTo((int)expected.Red).Within(tolerance));
            Assert.That((int)actual.Green, Is.EqualTo((int)expected.Green).Within(tolerance));
            Assert.That((int)actual.Blue, Is.EqualTo((int)expected.Blue).Within(tolerance));
            Assert.That((int)actual.Alpha, Is.EqualTo((int)expected.Alpha).Within(tolerance));
        }

        private static void Save(string name, byte[] bytes, ImageOutputFormat format)
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "parity-artifacts", "OUT-203-quality");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, name + "." + format.ToString().ToLowerInvariant());
            File.WriteAllBytes(path, bytes);
            TestContext.AddTestAttachment(path);
        }

        private sealed class NonSeekingOutput : Stream
        {
            private readonly MemoryStream Buffer = new();
            internal byte[] Bytes => Buffer.ToArray();
            internal bool WasDisposed { get; private set; }
            internal bool FailWrites { get; init; }
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => !WasDisposed;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() => Buffer.Flush();
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count)
            {
                if (FailWrites) throw new IOException("Fixture write failure.");
                Buffer.Write(buffer, offset, count);
            }

            protected override void Dispose(bool disposing)
            {
                WasDisposed = true;
                if (disposing) Buffer.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
