using System.IO;
using System.Linq;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.Examples
{
    public class TextFeatureExamples
    {
        [Test]
        public void ToggleOpenTypeLigatures()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(240, 150);
                page.Margin(10);
                page.PageColor("#ffffff");
                page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(28));

                page.Content().Column(column =>
                {
                    column.Item().Text(text => text.Span("office").EnableFontFeature("liga"));
                    column.Item().Text(text => text.Span("office").DisableFontFeature("liga"));
                });
            }));

            var pdf = document.GeneratePdf();
            using var page = SKBitmap.Decode(document.GenerateImages().Single());

            Assert.That(page.Width, Is.GreaterThan(0));
            Assert.That(pdf.Length, Is.GreaterThan(5));
            Save("font-features.pdf", pdf);
        }

        [Test]
        public void RenderDecorationStyles()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(260, 240);
                page.Margin(10);
                page.PageColor("#ffffff");
                page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(24));

                page.Content().Column(column =>
                {
                    column.Spacing(8);

                    column.Item().Text(text => text.Span("Solid")
                        .Underline()
                        .DecorationThickness(2)
                        .DecorationColor("#c62828"));

                    column.Item().Text(text => text.Span("Double")
                        .Underline()
                        .DecorationDouble()
                        .DecorationThickness(2)
                        .DecorationColor("#2e7d32"));

                    column.Item().Text(text => text.Span("Dotted")
                        .Underline()
                        .DecorationDotted()
                        .DecorationThickness(2)
                        .DecorationColor("#1565c0"));

                    column.Item().Text(text => text.Span("Dashed")
                        .Underline()
                        .DecorationDashed()
                        .DecorationThickness(2)
                        .DecorationColor("#6a1b9a"));

                    column.Item().Text(text => text.Span("Wavy")
                        .Underline()
                        .DecorationWavy()
                        .DecorationThickness(2)
                        .DecorationColor("#ef6c00"));

                    column.Item().Text(text => text.Span("Overline and strikethrough")
                        .Overline()
                        .Strikethrough()
                        .DecorationColor("#37474f"));
                });
            }));

            var pdf = document.GeneratePdf();
            using var page = SKBitmap.Decode(document.GenerateImages().First());

            Assert.That(page.Width, Is.GreaterThan(0));
            Save("decorations.pdf", pdf);
        }

        private static void Save(string fileName, byte[] pdf)
        {
            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "text-feature-example");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, fileName), pdf);
        }
    }
}
