using System;
using System.IO;
using System.Linq;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.Examples
{
    /// <summary>
    /// Demonstrates mixed-direction text with an explicit font fallback chain and CJK wrapping.
    /// The examples run against the licensed Noto subset fonts shared with the parity corpus, so
    /// they do not depend on fonts installed on the machine.
    /// </summary>
    public class ComplexScriptExamples
    {
        private const int PageWidth = 280;
        private const int PageHeight = 280;
        private const int PageMargin = 16;

        [Test]
        public void MixedDirectionTextRendersEachScriptWithItsFallbackFont()
        {
            var document = CreateDocument(page =>
            {
                page.DefaultTextStyle(style => CreateStyle(style, 14));

                page.Content().Column(column =>
                {
                    column.Spacing(8);

                    column.Item().Text("Invoice رقم 123 (USD)");
                    column.Item().Text("العربية 42 English");
                    column.Item().Text("שלום עולם 42");
                    column.Item().Text("हिन्दी नमस्ते 42");
                    column.Item().Text("あいうえお 漢字 42");
                    column.Item().Text("Emoji 😀 😃 🎉");
                });
            });

            var pages = Generate(document, out var pdf);

            Assert.That(pages, Has.Length.EqualTo(1), "The mixed-direction sample fits on one page.");
            Assert.That(pdf.Length, Is.GreaterThan(0));

            using var page = SKBitmap.Decode(pages.Single());
            var bounds = GetInkBounds(page);

            Assert.That(bounds, Is.Not.Null, "Mixed-direction text must draw ink.");
            Assert.That(bounds!.Value.Left, Is.GreaterThanOrEqualTo(PageMargin - 1),
                "Text must stay inside the page margin.");
            Assert.That(bounds.Value.Right, Is.LessThanOrEqualTo(PageWidth - PageMargin + 1),
                "Text must stay inside the page margin.");

            var repeated = Generate(document, out _).Single();
            using var repeatedPage = SKBitmap.Decode(repeated);

            Assert.That(CountInkPixels(repeatedPage), Is.EqualTo(CountInkPixels(page)),
                "Repeated generation must produce the same page ink.");

            SaveArtifacts("complex-script-mixed", pdf, pages);
        }

        [Test]
        public void CjkTextWrapsBetweenCharactersWithinTheDeclaredWidth()
        {
            const int textWidth = 72;

            var document = CreateDocument(page =>
            {
                page.DefaultTextStyle(style => CreateStyle(style, 12).LineHeight(1.8f));

                page.Content().Width(textWidth).Text(text =>
                    text.Span("あいうえ。」かきくけこさしすせそ"));
            });

            var pages = Generate(document, out var pdf);

            Assert.That(pages, Has.Length.EqualTo(1));
            Assert.That(pdf.Length, Is.GreaterThan(0));

            using var page = SKBitmap.Decode(pages.Single());

            Assert.That(CountInkRows(page), Is.GreaterThanOrEqualTo(3),
                "Text without spaces must wrap across several lines.");
            Assert.That(GetInkBounds(page)!.Value.Right, Is.LessThanOrEqualTo(PageMargin + textWidth + 1),
                "Wrapped lines must stay inside the declared text width.");

            SaveArtifacts("complex-script-cjk", pdf, pages);
        }

        [Test]
        public void ScriptCorpusRendersEverySupportedScript()
        {
            var document = CreateDocument(page =>
            {
                page.DefaultTextStyle(style => CreateStyle(style, 14));

                page.Content().Column(column =>
                {
                    column.Spacing(8);

                    column.Item().Text("السَّلَامُ عَلَيْكُمْ");
                    column.Item().Text("שָׁלוֹם");
                    column.Item().Text("हिन्दी नमस्ते दुनिया");
                    column.Item().Text("「あいうえお」かきくけこ");
                    column.Item().Text("👍🏽 ❤️");
                    column.Item().Text("éééé");
                });
            });

            var pages = Generate(document, out var pdf);

            Assert.That(pages, Has.Length.EqualTo(1));

            using var page = SKBitmap.Decode(pages.Single());

            Assert.That(GetInkBounds(page), Is.Not.Null,
                "The script corpus must draw ink for every line.");

            SaveArtifacts("complex-script-corpus", pdf, pages);
        }

        /// <summary>
        /// Latin primary font with an explicit fallback chain. Every fallback style resolves its
        /// own direction from the first strong character of each run.
        /// </summary>
        private static TextStyle CreateStyle(TextStyle style, float fontSize)
        {
            var chain = TextStyle.Default
                .FontFamily(ComplexScriptFonts.Emoji)
                .FontSize(fontSize)
                .DirectionAuto()
                .ApplyGlobalStyle();

            chain = TextStyle.Default.FontFamily(ComplexScriptFonts.Cjk).FontSize(fontSize).DirectionAuto()
                .Fallback(chain).ApplyGlobalStyle();
            chain = TextStyle.Default.FontFamily(ComplexScriptFonts.Devanagari).FontSize(fontSize).DirectionAuto()
                .Fallback(chain).ApplyGlobalStyle();
            chain = TextStyle.Default.FontFamily(ComplexScriptFonts.Hebrew).FontSize(fontSize).DirectionAuto()
                .Fallback(chain).ApplyGlobalStyle();
            chain = TextStyle.Default.FontFamily(ComplexScriptFonts.Arabic).FontSize(fontSize).DirectionAuto()
                .Fallback(chain).ApplyGlobalStyle();

            return style
                .FontFamily(ComplexScriptFonts.Latin)
                .FontSize(fontSize)
                .DirectionAuto()
                .Fallback(chain)
                .ApplyGlobalStyle();
        }

        private static Document CreateDocument(Action<PageDescriptor> content)
        {
            ComplexScriptFonts.Register();

            return Document.Create(container => container.Page(page =>
            {
                page.Size(PageWidth, PageHeight);
                page.Margin(PageMargin);
                page.PageColor("#ffffff");
                content(page);
            }));
        }

        private static byte[][] Generate(IDocument document, out byte[] pdf)
        {
            pdf = document.GeneratePdf();

            return document.GenerateImages(new ImageGenerationSettings
            {
                // Pixel coordinates then match points, so the assertions below can use the
                // declared page and text geometry directly.
                RasterDpi = 72,
                Format = ImageOutputFormat.Png
            }).ToArray();
        }

        private static int CountInkRows(SKBitmap page)
        {
            var bands = 0;
            var insideBand = false;

            for (var y = 0; y < page.Height; y++)
            {
                var hasInk = false;

                for (var x = 0; x < page.Width; x++)
                {
                    if (!IsInk(page.GetPixel(x, y)))
                        continue;

                    hasInk = true;
                    break;
                }

                if (hasInk && !insideBand)
                    bands++;

                insideBand = hasInk;
            }

            return bands;
        }

        private static int CountInkPixels(SKBitmap page)
        {
            var count = 0;

            for (var y = 0; y < page.Height; y++)
            for (var x = 0; x < page.Width; x++)
            {
                if (IsInk(page.GetPixel(x, y)))
                    count++;
            }

            return count;
        }

        private static (int Left, int Top, int Right, int Bottom)? GetInkBounds(SKBitmap page)
        {
            var left = int.MaxValue;
            var top = int.MaxValue;
            var right = int.MinValue;
            var bottom = int.MinValue;

            for (var y = 0; y < page.Height; y++)
            for (var x = 0; x < page.Width; x++)
            {
                if (!IsInk(page.GetPixel(x, y)))
                    continue;

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }

            return right > left ? (left, top, right, bottom) : null;
        }

        private static bool IsInk(SKColor color) =>
            color.Red != 255 || color.Green != 255 || color.Blue != 255;

        private static void SaveArtifacts(string name, byte[] pdf, byte[][] pages)
        {
            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "complex-script-example");
            Directory.CreateDirectory(directory);

            File.WriteAllBytes(Path.Combine(directory, name + ".pdf"), pdf);

            for (var index = 0; index < pages.Length; index++)
                File.WriteAllBytes(Path.Combine(directory, $"{name}-page-{index + 1}.png"), pages[index]);
        }
    }
}
