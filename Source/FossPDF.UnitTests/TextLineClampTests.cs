using System;
using System.Linq;
using FossPDF.Drawing;
using FossPDF.Elements.Text;
using FossPDF.Elements.Text.Items;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.UnitTests
{
    [TestFixture]
    [NonParallelizable]
    public class TextLineClampTests
    {
        private DocumentSpecificFontManager FontManager { get; set; } = null!;

        [SetUp]
        public void SetUp()
        {
            FontManager = FossPDF.Drawing.FontManager.MakeDocumentSpecific();
        }

        [TearDown]
        public void TearDown()
        {
            FontManager.DisposeDocumentSpecificObjects();
        }

        [Test]
        public void ClampLines_LimitsLinesAndAppendsCustomEllipsis()
        {
            var block = CreateBlock(LongText, 2, "[...]");

            var lines = block.CalculateLines(100, 1000, out var clampReached);

            Assert.That(clampReached, Is.True);
            Assert.That(lines, Has.Count.EqualTo(2));

            var ellipsis = lines.Last().Elements.Last().Item as TextBlockSpan;
            Assert.That(ellipsis?.Text, Is.EqualTo("[...]"));
            Assert.That(lines.Last().Width, Is.LessThanOrEqualTo(100.1f));
        }

        [Test]
        public void ClampLines_DoesNotAppendEllipsisWhenContentFits()
        {
            var block = CreateBlock("short text", 5, "[...]");

            var lines = block.CalculateLines(500, 1000, out var clampReached);

            Assert.That(clampReached, Is.False);
            Assert.That(lines, Has.Count.EqualTo(1));
            Assert.That(lines.SelectMany(x => x.Elements).Any(x => x.Item is TextBlockSpan { Text: "[...]" }),
                Is.False);
        }

        [Test]
        public void ClampLines_UsesTheDefaultEllipsis()
        {
            var descriptor = new TextDescriptor();

            Assert.DoesNotThrow(() => descriptor.ClampLines(1));

            var block = CreateBlock(LongText, 1);
            var lines = block.CalculateLines(120, 1000, out var clampReached);

            Assert.That(clampReached, Is.True);
            Assert.That((lines.Last().Elements.Last().Item as TextBlockSpan)?.Text,
                Is.EqualTo(TextDescriptor.DefaultLineClampEllipsis));
        }

        [Test]
        public void ClampLines_SupportsAnEmptyEllipsis()
        {
            var block = CreateBlock(LongText, 1, string.Empty);

            var lines = block.CalculateLines(100, 1000, out var clampReached);

            Assert.That(clampReached, Is.True);
            Assert.That(lines, Has.Count.EqualTo(1));
            Assert.That(lines.Single().Elements.Any(x => x.Item is TextBlockSpan { Text: "" }), Is.False);
        }

        [Test]
        public void ClampLines_RejectsInvalidLimits()
        {
            var descriptor = new TextDescriptor();

            Assert.Throws<ArgumentOutOfRangeException>(() => descriptor.ClampLines(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => descriptor.ClampLines(-1));
        }

        [Test]
        public void ClampLines_CountsRenderedLinesAcrossPages()
        {
            var block = CreateBlock(LongText, 3, "[...]");

            block.Draw(new Size(100, 20));

            var lines = block.CalculateLines(100, 1000, out var clampReached);

            Assert.That(clampReached, Is.True);
            Assert.That(lines, Has.Count.EqualTo(2));
            Assert.That((lines.Last().Elements.Last().Item as TextBlockSpan)?.Text, Is.EqualTo("[...]"));
        }

        [Test]
        public void ClampLines_RendersThroughThePublicApi()
        {
            var clamped = RenderInkHeight(true);
            var unclamped = RenderInkHeight(false);

            Assert.That(clamped, Is.GreaterThan(0));
            Assert.That(unclamped, Is.GreaterThan(clamped));
        }

        private TextBlock CreateBlock(string text, int? lineClamp = null, string ellipsis = "…")
        {
            var pageContext = new PageContext
            {
                FontManager = FontManager
            };

            var block = new TextBlock
            {
                LineClamp = lineClamp,
                LineClampEllipsis = ellipsis
            };

            block.Items.Add(new TextBlockSpan
            {
                Text = text,
                Style = TextStyle.Default.FontSize(12)
            });

            block.PageContext = pageContext;
            block.Canvas = new FreeCanvas();
            block.ResetState();

            return block;
        }

        private static int RenderInkHeight(bool clamp)
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(120, 300);
                page.Margin(10);
                page.PageColor("#ffffff");
                page.Content().Text(text =>
                {
                    if (clamp)
                        text.ClampLines(1, "[...]");

                    text.Span("one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen")
                        .FontSize(12);
                });
            }));

            using var bitmap = SKBitmap.Decode(document.GenerateImages().Single());
            var minY = int.MaxValue;
            var maxY = int.MinValue;

            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);

                if (color.Red >= 245 && color.Green >= 245 && color.Blue >= 245)
                    continue;

                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }

            return maxY < minY ? 0 : maxY - minY + 1;
        }

        private const string LongText =
            "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen " +
            "sixteen seventeen eighteen nineteen twenty twenty-one twenty-two twenty-three twenty-four";
    }
}
