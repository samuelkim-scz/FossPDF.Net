#if NET9_0
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using FossPDF.Drawing;
using FossPDF.Elements;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.Svg;
using FossPDF.UnitTests.TestEngine;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.UnitTests
{
    /// <summary>
    /// FP-201: SVG input, preloading and vector replay. The companion package targets net9.0, so the
    /// whole fixture is compiled only for that framework.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class SvgImageTests
    {
        private const string CustomFontAlias = "FossPdfSvgTestAlias";
        private const string Svg80x40 =
            "<svg xmlns='http://www.w3.org/2000/svg' width='80' height='40'>" +
            "<rect x='2' y='2' width='76' height='36' fill='#cc0000'/>" +
            "<circle cx='40' cy='20' r='12' fill='#00aa00'/>" +
            "</svg>";

        private static string DefaultFamily { get; set; }

        [OneTimeSetUp]
        public void RegisterCustomFont()
        {
            var fontPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Resources", "FontContent.ttf");

            using (var stream = File.OpenRead(fontPath))
                FontManager.RegisterFontWithCustomName(CustomFontAlias, stream);

            using var typeface = SKTypeface.FromFile(fontPath);
            DefaultFamily = typeface.FamilyName;
        }

        // ------------------------------------------------------------------ parsing and preloading

        [Test]
        public void FromText_ReportsNaturalSize()
        {
            using var image = SvgImage.FromText(Svg80x40);

            image.Size.Should().Be(new Size(80, 40));
        }

        [Test]
        public void FromText_UsesViewBoxWhenAbsoluteSizeIsMissing()
        {
            using var image = SvgImage.FromText(
                "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 120 60'>" +
                "<rect width='120' height='60' fill='#123456'/></svg>");

            image.Size.Should().Be(new Size(120, 60));
        }

        [Test]
        public void FromBytes_MatchesFromText()
        {
            using var fromText = SvgImage.FromText(Svg80x40);
            using var fromBytes = SvgImage.FromBytes(Encoding.UTF8.GetBytes(Svg80x40));

            Render(fromText).Should().Equal(Render(fromBytes));
        }

        [Test]
        public void FromStream_ReadsFromCurrentPositionAndLeavesStreamOpen()
        {
            using var stream = new MemoryStream();
            stream.Write(new byte[] { 1, 2, 3, 4 }, 0, 4);
            var payload = Encoding.UTF8.GetBytes(Svg80x40);
            stream.Write(payload, 0, payload.Length);
            stream.Position = 4;

            using var image = SvgImage.FromStream(stream);

            image.Size.Should().Be(new Size(80, 40));
            stream.CanRead.Should().BeTrue("the caller owns the stream");
            stream.Position.Should().Be(4 + payload.Length);
        }

        [Test]
        public void FromFile_LoadsTheDocument()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".svg");

            try
            {
                File.WriteAllText(path, Svg80x40);

                using var fromFile = SvgImage.FromFile(path);
                using var fromText = SvgImage.FromText(Svg80x40);

                Render(fromFile).Should().Equal(Render(fromText));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void NullArguments_AreRejected()
        {
            FluentActions.Invoking(() => SvgImage.FromText(null!)).Should().Throw<ArgumentNullException>();
            FluentActions.Invoking(() => SvgImage.FromBytes(null!)).Should().Throw<ArgumentNullException>();
            FluentActions.Invoking(() => SvgImage.FromStream(null!)).Should().Throw<ArgumentNullException>();
            FluentActions.Invoking(() => SvgImage.FromFile(null!)).Should().Throw<ArgumentNullException>();
        }

        [TestCase("<svg xmlns='http://www.w3.org/2000/svg'/>")]
        [TestCase("<svg xmlns='http://www.w3.org/2000/svg' width='100%' height='100%'/>")]
        [TestCase("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 0 0'/>")]
        public void DocumentWithoutIntrinsicSize_IsRejected(string source)
        {
            FluentActions.Invoking(() => SvgImage.FromText(source))
                .Should().Throw<SvgParseException>()
                .WithMessage("*intrinsic size*");
        }

        [Test]
        public void AbsoluteUnits_AreAccepted()
        {
            using var image = SvgImage.FromText(
                "<svg xmlns='http://www.w3.org/2000/svg' width='2cm' height='1cm'>" +
                "<rect width='100%' height='100%' fill='#123456'/></svg>");

            image.Size.Width.Should().BeGreaterThan(0);
            image.Size.Height.Should().BeGreaterThan(0);
        }

        [Test]
        public void OversizedInput_IsRejectedBeforeParsing()
        {
            using var stream = new OversizedStream(32 * 1024 * 1024 + 1);

            FluentActions.Invoking(() => SvgImage.FromStream(stream))
                .Should().Throw<SvgParseException>()
                .WithMessage("*maximum supported size*");
        }

        [TestCase("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><rect></svg>", "well-formed")]
        [TestCase("<html xmlns='http://www.w3.org/1999/xhtml' width='10' height='10'/>", "root element")]
        [TestCase("<!DOCTYPE svg [<!ENTITY xxe SYSTEM 'file:///etc/passwd'>]><svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'>&xxe;</svg>", null)]
        [TestCase("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><script>alert(1)</script></svg>", "script")]
        [TestCase("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><use href='file:///etc/passwd'/></svg>", "external resource")]
        [TestCase("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><image href='https://example.com/a.png'/></svg>", "external resource")]
        [TestCase("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><rect width='10' height='10' fill='url(https://example.com/p.svg#x)'/></svg>", "external resource")]
        [TestCase("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><style>@import url('https://example.com/a.css');</style></svg>", "import")]
        [TestCase("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><style>.a{fill:url(javascript:alert(1))}</style></svg>", "javascript")]
        [TestCase("<?xml-stylesheet href='https://example.com/a.css'?><svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'/>", "stylesheet")]
        public void UnsafeInput_IsRejected(string source, string expectedMessage)
        {
            var assertion = FluentActions.Invoking(() => SvgImage.FromText(source))
                .Should().Throw<SvgParseException>();

            if (expectedMessage != null)
                assertion.WithMessage($"*{expectedMessage}*");
        }

        // ------------------------------------------------------------------ drawing and ownership

        [Test]
        public void Draw_IsDeterministicWhenTheImageIsReused()
        {
            using var image = SvgImage.FromText(Svg80x40);
            var first = Render(image);
            var second = Render(image);
            var third = Render(image);

            first.Should().Equal(second);
            second.Should().Equal(third);
        }

        [Test]
        public void Draw_IsSafeFromConcurrentDocuments()
        {
            using var image = SvgImage.FromText(Svg80x40);
            var expected = Render(image);

            var tasks = Enumerable.Range(0, 4)
                .Select(_ => Task.Run(() => Render(image)))
                .ToArray();

            Task.WaitAll(tasks);

            foreach (var task in tasks)
                task.Result.Should().Equal(expected);
        }

        [Test]
        public void Dispose_RejectsFurtherDrawingAndIsIdempotent()
        {
            var image = SvgImage.FromText(Svg80x40);
            image.Dispose();
            image.Dispose();

            using var bitmap = new SKBitmap(80, 40);
            using var canvas = new SKCanvas(bitmap);

            FluentActions.Invoking(() => image.Draw(canvas, new Size(80, 40)))
                .Should().Throw<ObjectDisposedException>();
        }

        [Test]
        public void FluentApi_RejectsInvalidArguments()
        {
            using var image = SvgImage.FromText(Svg80x40);
            var container = new Container();

            FluentActions.Invoking(() => container.Svg((SvgImage)null!)).Should().Throw<ArgumentNullException>();
            FluentActions.Invoking(() => container.Svg((string)null!)).Should().Throw<ArgumentNullException>();
            FluentActions.Invoking(() => container.Svg(image, (ImageScaling)999))
                .Should().Throw<ArgumentOutOfRangeException>();
        }

        // ------------------------------------------------------------------ fit modes

        [Test]
        public void FitWidth_ScalesToAvailableWidth()
        {
            using var image = SvgImage.FromText(Svg80x40);

            TestPlan
                .For(_ =>
                {
                    var container = new Container();
                    container.Svg(image, ImageScaling.FitWidth);
                    return container;
                })
                .MeasureElement(new Size(300, 200))
                .CheckMeasureResult(SpacePlan.FullRender(300, 150));
        }

        [Test]
        public void FitHeight_ScalesToAvailableHeight()
        {
            using var image = SvgImage.FromText(Svg80x40);

            TestPlan
                .For(_ =>
                {
                    var container = new Container();
                    container.Svg(image, ImageScaling.FitHeight);
                    return container;
                })
                .MeasureElement(new Size(500, 200))
                .CheckMeasureResult(SpacePlan.FullRender(400, 200));
        }

        [Test]
        public void FitArea_UsesTheSmallerFittingDimension()
        {
            using var image = SvgImage.FromText(Svg80x40);

            TestPlan
                .For(_ =>
                {
                    var container = new Container();
                    container.Svg(image, ImageScaling.FitArea);
                    return container;
                })
                .MeasureElement(new Size(300, 200))
                .CheckMeasureResult(SpacePlan.FullRender(300, 150));
        }

        [Test]
        public void Resize_TakesTheWholeAvailableSpace()
        {
            using var image = SvgImage.FromText(Svg80x40);

            TestPlan
                .For(_ =>
                {
                    var container = new Container();
                    container.Svg(image, ImageScaling.Resize);
                    return container;
                })
                .MeasureElement(new Size(300, 200))
                .CheckMeasureResult(SpacePlan.FullRender(300, 200));
        }

        // ------------------------------------------------------------------ fonts

        [Test]
        public void RegisteredFont_RendersTheSameUnderItsCustomNameAndItsInternalFamily()
        {
            var customName = RenderText(CustomFontAlias);
            var internalName = RenderText(DefaultFamily);

            customName.Should().Equal(internalName,
                "a font registered with RegisterFontWithCustomName must resolve through either name");
        }

        [Test]
        public void TextWithoutFontFamily_UsesTheFossPdfDefaultFont()
        {
            var withoutFamily = RenderText(null);
            var lato = RenderText("Lato");

            withoutFamily.Should().Equal(lato);
        }

        [Test]
        public void UnknownFontFamily_FallsBackAndStillRenders()
        {
            var image = RenderText("DefinitelyMissingFont123");

            using var bitmap = SKBitmap.Decode(image);
            bitmap.Width.Should().Be(240);
            bitmap.Height.Should().Be(50);
            HasNonTransparentPixel(bitmap).Should().BeTrue();
        }

        // ------------------------------------------------------------------ vector and raster output

        [Test]
        public void VectorOutput_KeepsPathsAndAddsNoImageXObject()
        {
            using var image = SvgImage.FromText(Svg80x40);
            var pdf = CreateDocument(image, rasterized: false).GeneratePdf();

            var pdfText = Encoding.Latin1.GetString(pdf);
            pdfText.Should().NotContain("/Subtype /Image");

            var content = DecodeContentStreams(pdf);
            content.Should().Contain(" re", "the SVG rectangle is replayed as a path");
            content.Should().Contain(" m", "the SVG circle is replayed as a path");
            content.Should().Contain(" c", "the SVG circle is replayed with cubic curves");
        }

        [Test]
        public void RasterFallback_ProducesAnImageAtTheRequestedDpi()
        {
            using var image = SvgImage.FromText(Svg80x40);
            var pdf = CreateDocument(image, rasterized: true).GeneratePdf();

            var pdfText = Encoding.Latin1.GetString(pdf);
            pdfText.Should().Contain("/Subtype /Image");
            pdfText.Should().Contain("/Width 160");
            pdfText.Should().Contain("/Height 80");
        }

        [Test]
        public void RasterFallback_ValidatesOptions()
        {
            using var image = SvgImage.FromText(Svg80x40);
            var container = new Container();

            FluentActions.Invoking(() => container.SvgRasterized(image, new SvgRasterizationOptions { Dpi = 0 }))
                .Should().Throw<ArgumentOutOfRangeException>();

            FluentActions.Invoking(() => container.SvgRasterized(image, new SvgRasterizationOptions { Background = "not-a-color" }))
                .Should().Throw<ArgumentException>();

            FluentActions.Invoking(() => container.SvgRasterized(image, new SvgRasterizationOptions { Scaling = (ImageScaling)999 }))
                .Should().Throw<ArgumentOutOfRangeException>();
        }

        // ------------------------------------------------------------------ helpers

        private static Document CreateDocument(SvgImage image, bool rasterized)
        {
            return Document.Create(container => container.Page(page =>
            {
                page.Size(80, 40);
                page.Margin(0);

                if (rasterized)
                    page.Content().SvgRasterized(image);
                else
                    page.Content().Svg(image);
            }));
        }

        private static byte[] Render(SvgImage image)
        {
            return Document.Create(container => container.Page(page =>
            {
                page.Size(80, 40);
                page.Margin(0);
                page.Content().Svg(image);
            })).GenerateImages().Single();
        }

        private static byte[] RenderText(string fontFamily)
        {
            var familyAttribute = fontFamily == null ? string.Empty : $" font-family='{fontFamily}'";

            var source =
                "<svg xmlns='http://www.w3.org/2000/svg' width='240' height='50'>" +
                "<rect width='240' height='50' fill='white'/>" +
                $"<text x='4' y='34'{familyAttribute} font-size='28'>Hamburgefonstiv</text>" +
                "</svg>";

            using var image = SvgImage.FromText(source);

            return Document.Create(container => container.Page(page =>
            {
                page.Size(240, 50);
                page.Margin(0);
                page.Content().Svg(image);
            })).GenerateImages().Single();
        }

        private static bool HasNonTransparentPixel(SKBitmap bitmap)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y).Alpha > 0)
                        return true;
                }
            }

            return false;
        }

        private static string DecodeContentStreams(byte[] pdf)
        {
            var raw = Encoding.Latin1.GetString(pdf);
            var result = new StringBuilder();
            var index = 0;

            while ((index = raw.IndexOf("stream", index, StringComparison.Ordinal)) >= 0)
            {
                var start = index + "stream".Length;

                if (start < raw.Length && raw[start] == '\r')
                    start++;

                if (start < raw.Length && raw[start] == '\n')
                    start++;

                var end = raw.IndexOf("endstream", start, StringComparison.Ordinal);

                if (end < 0)
                    break;

                var length = end - start;
                var bytes = new byte[length];
                Array.Copy(pdf, start, bytes, 0, length);

                try
                {
                    using var compressed = new MemoryStream(bytes, writable: false);
                    using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    zlib.CopyTo(output);
                    result.Append(Encoding.Latin1.GetString(output.ToArray()));
                }
                catch (InvalidDataException)
                {
                    result.Append(Encoding.Latin1.GetString(bytes));
                }

                index = end + "endstream".Length;
            }

            return result.ToString();
        }

        private sealed class OversizedStream : Stream
        {
            private long _remaining;

            public OversizedStream(long length)
            {
                _remaining = length;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_remaining == 0)
                    return 0;

                var produced = (int)Math.Min(count, _remaining);
                Array.Clear(buffer, offset, produced);
                _remaining -= produced;

                return produced;
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
#endif
