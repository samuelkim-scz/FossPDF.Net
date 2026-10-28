using System;
using System.Linq;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.UnitTests
{
    [TestFixture]
    public class TextFeatureTests
    {
        [Test]
        public void DecorationStyle_Mutates()
        {
            var style = TextStyle.LibraryDefault
                .Underline()
                .DecorationDouble()
                .DecorationThickness(2)
                .DecorationColor("#ff0000");

            Assert.That(style.HasUnderline, Is.True);
            Assert.That(style.DecorationStyle, Is.EqualTo(TextDecorationStyle.Double));
            Assert.That(style.DecorationThickness, Is.EqualTo(2));
            Assert.That(style.DecorationColor, Is.EqualTo("#ff0000"));
        }

        [Test]
        public void FontFeatureToggle_ChangesLigatureShaping()
        {
            var fontManager = FontManager.MakeDocumentSpecific();

            try
            {
                var enabledStyle = TextStyle.LibraryDefault
                    .FontFamily("Lato")
                    .FontSize(20)
                    .EnableFontFeature("liga");

                var disabledStyle = enabledStyle.DisableFontFeature("liga");

                var enabled = new TextShaper(enabledStyle, fontManager).Shape("fi");
                var disabled = new TextShaper(disabledStyle, fontManager).Shape("fi");

                Assert.That(enabled.Length, Is.EqualTo(1), "Lato's fi ligature should use one glyph.");
                Assert.That(disabled.Length, Is.EqualTo(2), "Disabling liga should restore separate f and i glyphs.");
            }
            finally
            {
                fontManager.DisposeDocumentSpecificObjects();
            }
        }

        [Test]
        public void FontFeatureToggle_LastSettingWins()
        {
            var fontManager = FontManager.MakeDocumentSpecific();

            try
            {
                var style = TextStyle.LibraryDefault
                    .FontFamily("Lato")
                    .FontSize(20)
                    .EnableFontFeature("liga")
                    .DisableFontFeature("liga");

                var result = new TextShaper(style, fontManager).Shape("fi");
                Assert.That(result.Length, Is.EqualTo(2));
            }
            finally
            {
                fontManager.DisposeDocumentSpecificObjects();
            }
        }

        [Test]
        public void Decorations_RenderAllSupportedStylesAndColors()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(220, 320);
                page.Margin(10);
                page.PageColor("#ffffff");
                page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(24));

                page.Content().Column(column =>
                {
                    column.Spacing(8);

                    column.Item().Text(text => text.Span("Underline")
                        .Underline()
                        .DecorationThickness(3)
                        .DecorationColor("#ff0000"));

                    column.Item().Text(text => text.Span("Overline")
                        .Overline()
                        .DecorationThickness(3)
                        .DecorationColor("#00ff00"));

                    column.Item().Text(text => text.Span("Strikeout")
                        .Strikethrough()
                        .DecorationThickness(3)
                        .DecorationColor("#0000ff"));

                    column.Item().Text(text => text.Span("Double")
                        .Underline()
                        .DecorationDouble()
                        .DecorationThickness(3)
                        .DecorationColor("#ff00ff"));

                    column.Item().Text(text => text.Span("Dotted")
                        .Underline()
                        .DecorationDotted()
                        .DecorationThickness(3)
                        .DecorationColor("#00ffff"));

                    column.Item().Text(text => text.Span("Dashed")
                        .Underline()
                        .DecorationDashed()
                        .DecorationThickness(3)
                        .DecorationColor("#ffff00"));

                    column.Item().Text(text => text.Span("Wavy")
                        .Underline()
                        .DecorationWavy()
                        .DecorationThickness(3)
                        .DecorationColor("#ff8800"));
                });
            }));

            using var page = SKBitmap.Decode(document.GenerateImages().First());
            Assert.That(CountColor(page, "#ff0000"), Is.GreaterThan(0));
            Assert.That(CountColor(page, "#00ff00"), Is.GreaterThan(0));
            Assert.That(CountColor(page, "#0000ff"), Is.GreaterThan(0));
            Assert.That(CountColor(page, "#ff00ff"), Is.GreaterThan(0));
            Assert.That(CountColor(page, "#00ffff"), Is.GreaterThan(0));
            Assert.That(CountColor(page, "#ffff00"), Is.GreaterThan(0));
            Assert.That(CountColor(page, "#ff8800"), Is.GreaterThan(0));
        }

        [Test]
        public void Decorations_RepaintOnEachWrappedLine()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(100, 140);
                page.Margin(10);
                page.PageColor("#ffffff");
                page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(16));

                page.Content().Text(text => text.Span("word word word word word word word")
                    .Underline()
                    .DecorationThickness(3)
                    .DecorationColor("#ff0000"));
            }));

            using var page = SKBitmap.Decode(document.GenerateImages().First());

            var paintedRows = 0;
            for (var y = 0; y < page.Height; y++)
            {
                var painted = false;
                for (var x = 0; x < page.Width && !painted; x++)
                {
                    var pixel = page.GetPixel(x, y);
                    painted = pixel.Red > 120 && pixel.Red > pixel.Green + 20 && pixel.Red > pixel.Blue + 20;
                }

                if (painted)
                    paintedRows++;
            }

            Assert.That(paintedRows, Is.GreaterThanOrEqualTo(2),
                "Each wrapped line should receive its own decoration.");
        }

        [Test]
        public void DecorationThickness_IncreasesThePaintedArea()
        {
            var thin = RenderDecoratedText(1);
            var thick = RenderDecoratedText(3);

            using var thinPage = SKBitmap.Decode(thin);
            using var thickPage = SKBitmap.Decode(thick);

            Assert.That(CountBlueish(thickPage), Is.GreaterThan(CountBlueish(thinPage)));
        }

        [Test]
        public void Decorations_WorkOnSuperscriptAndSubscriptSpans()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(220, 80);
                page.Margin(10);
                page.PageColor("#ffffff");
                page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(24));

                page.Content().Text(text =>
                {
                    text.Span("E = mc");
                    text.Span("2").Superscript().Underline().DecorationThickness(3)
                        .DecorationColor("#ff0000");
                    text.Span(" + H");
                    text.Span("2").Subscript().Overline().DecorationThickness(3)
                        .DecorationColor("#0000ff");
                    text.Span("O");
                });
            }));

            using var page = SKBitmap.Decode(document.GenerateImages().First());
            Assert.That(CountColor(page, "#ff0000"), Is.GreaterThan(0));
            Assert.That(CountColor(page, "#0000ff"), Is.GreaterThan(0));
        }

        private static byte[] RenderDecoratedText(float thickness)
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(160, 60);
                page.Margin(10);
                page.PageColor("#ffffff");
                page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(24));

                page.Content().Text(text => text.Span("Thickness")
                    .Underline()
                    .DecorationThickness(thickness)
                    .DecorationColor("#0000ff"));
            }));

            return document.GenerateImages().Single();
        }

        private static int CountBlueish(SKBitmap bitmap)
        {
            var count = 0;

            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Blue > 80 && pixel.Blue > pixel.Red + 30 && pixel.Blue > pixel.Green + 30)
                    count++;
            }

            return count;
        }

        private static int CountColor(SKBitmap bitmap, string color)
        {
            var target = SKColor.Parse(color);
            var count = 0;

            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red == target.Red && pixel.Green == target.Green && pixel.Blue == target.Blue)
                    count++;
            }

            return count;
        }
    }
}
