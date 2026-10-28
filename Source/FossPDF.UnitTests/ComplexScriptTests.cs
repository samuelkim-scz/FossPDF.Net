using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using FossPDF.Drawing;
using FossPDF.Elements.Text;
using FossPDF.Elements.Text.Calculation;
using FossPDF.Elements.Text.Items;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.UnitTests
{
    /// <summary>
    /// FP-109 qualification corpus. The tests use subset Noto fonts registered under stable names,
    /// so results do not depend on fonts installed on the test machine. They verify shaping,
    /// per-codepoint fallback, right-to-left layout, CJK line breaking and surrogate pair handling.
    /// </summary>
    [TestFixture]
    public class ComplexScriptTests
    {
        private const float Tolerance = 0.01f;
        private const float FontSize = 12;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ComplexScriptFonts.Register();
        }

        // ---------------------------------------------------------------- shaping

        [Test]
        public void Arabic_Shaping_AppliesContextualForms()
        {
            var result = Shape("بب", CreateStyle(ComplexScriptFonts.Arabic));
            var isolated = Shape("ب", CreateStyle(ComplexScriptFonts.Arabic));

            result.Should().OnlyContain(x => x.Codepoint != 0);

            // Joining context replaces the isolated glyphs with initial and final forms.
            GlyphIds(result).Should().NotEqual(GlyphIds(isolated).Concat(GlyphIds(isolated)).OrderBy(x => x));
        }

        [Test]
        public void Arabic_Shaping_ProducesLamAlefLigature()
        {
            var style = CreateStyle(ComplexScriptFonts.Arabic);
            var ligature = Shape("لا", style);
            var lam = Shape("ل", style);
            var alef = Shape("ا", style);

            ligature.Should().OnlyContain(x => x.Codepoint != 0);

            var ligatureWidth = ligature.MeasureWidth(0, ligature.Length - 1).Width;
            var isolatedWidth = lam.MeasureWidth(0, lam.Length - 1).Width +
                                alef.MeasureWidth(0, alef.Length - 1).Width;

            ligatureWidth.Should().BeLessThan(isolatedWidth, "the mandatory lam-alef ligature is narrower than the two isolated letters");
        }

        [Test]
        public void Arabic_Shaping_SupportsMarksAndArabicIndicDigits()
        {
            var result = Shape("السَّلَامُ ١٢٣", CreateStyle(ComplexScriptFonts.Arabic));

            result.Length.Should().BeGreaterThan(0);
            result.Should().OnlyContain(x => x.Codepoint != 0);
        }

        [Test]
        public void Hebrew_Shaping_SupportsNiqqud()
        {
            var result = Shape("שָׁלוֹם", CreateStyle(ComplexScriptFonts.Hebrew));

            result.Length.Should().BeGreaterThan("שלום".Length, "niqqud are combining marks with their own glyphs");
            result.Should().OnlyContain(x => x.Codepoint != 0);
        }

        [Test]
        public void Devanagari_Shaping_PlacesMatraBeforeBase()
        {
            var baseOnly = Shape("क", CreateStyle(ComplexScriptFonts.Devanagari));
            var withMatra = Shape("कि", CreateStyle(ComplexScriptFonts.Devanagari));

            withMatra.Length.Should().Be(2);
            withMatra.Should().OnlyContain(x => x.Codepoint != 0);

            // Logical order is ka then i-matra; visual order has the matra to the left of the base.
            withMatra[0].Codepoint.Should().NotBe(baseOnly[0].Codepoint);
            withMatra[1].Codepoint.Should().Be(baseOnly[0].Codepoint);
        }

        [Test]
        public void Devanagari_Shaping_ProducesConjuncts()
        {
            var result = Shape("क्षि", CreateStyle(ComplexScriptFonts.Devanagari));

            result.Should().OnlyContain(x => x.Codepoint != 0);
            result.Length.Should().BeLessThan("क्षि".Length, "conjuncts and the matra reduce the glyph count");
        }

        [Test]
        public void Emoji_Shaping_TreatsSurrogatePairAsOneCharacter()
        {
            var result = Shape("😀", CreateStyle(ComplexScriptFonts.Emoji));

            result.Length.Should().Be(1);
            result[0].Codepoint.Should().NotBe(0);
            result.MeasureWidth(0, 0).Width.Should().BeGreaterThan(0);
        }

        [Test]
        public void Emoji_Shaping_HandlesVariationSelectorsAndZwjSequences()
        {
            var result = Shape("❤️ 👨‍👩‍👧‍👦 👍🏽", CreateStyle(ComplexScriptFonts.Emoji));

            result.Should().OnlyContain(x => x.Codepoint != 0);
            result.MeasureWidth(0, result.Length - 1).Width.Should().BeGreaterThan(0);
        }

        // ---------------------------------------------------------------- fallback

        [Test]
        public void Fallback_SplitsMixedTextIntoOneRunPerFont()
        {
            ComplexScriptFonts.Register();

            var style = CreateStyle(ComplexScriptFonts.Latin)
                .Fallback(CreateStyle(ComplexScriptFonts.Arabic));

            var runs = "Invoice رقم 123".SplitWithFontFallback(style, CreateFontManager(), true).ToList();

            runs.Should().HaveCount(3);
            runs[1].Style.FontFamily.Should().Be(ComplexScriptFonts.Arabic);
            runs.Select(x => x.Content).Should().Equal("Invoice ", "رقم", " 123");
            string.Concat(runs.Select(x => x.Content)).Should().Be("Invoice رقم 123");
        }

        [Test]
        public void Fallback_DoesNotSplitSurrogatePairs()
        {
            var style = CreateStyle(ComplexScriptFonts.Latin)
                .Fallback(CreateStyle(ComplexScriptFonts.Emoji));

            var runs = "a😀b".SplitWithFontFallback(style, CreateFontManager(), true).ToList();

            runs.Select(x => x.Content).Should().Equal("a", "😀", "b");
            string.Concat(runs.Select(x => x.Content)).Should().Be("a😀b");
        }

        [Test]
        public void Fallback_ThrowsForCharactersNoConfiguredFontCovers()
        {
            var style = CreateStyle(ComplexScriptFonts.Latin);

            // U+10FFFD is a plane-16 private-use character and is not in any configured font.
            Action action = () => "A􏿽".SplitWithFontFallback(style, CreateFontManager(), true).ToList();

            action.Should().Throw<FossPDF.Drawing.Exceptions.DocumentDrawingException>()
                .WithMessage("*U-10FFFD*");
        }

        [Test]
        public void Fallback_Disabled_LetsThePrimaryFontHandleMissingGlyphs()
        {
            var style = CreateStyle(ComplexScriptFonts.Latin);

            var runs = "A􏿽".SplitWithFontFallback(style, CreateFontManager(), false).ToList();

            runs.Should().ContainSingle();
            runs[0].Style.FontFamily.Should().Be(ComplexScriptFonts.Latin);
        }

        // ---------------------------------------------------------------- layout

        [Test]
        public void Layout_MixedDirection_RendersEachScriptWithItsFallbackFont()
        {
            var style = CreateStyle(ComplexScriptFonts.Latin)
                .Fallback(CreateStyle(ComplexScriptFonts.Arabic).FontColor("#ff0000"));

            var lines = MeasureLines(CreateBlock("Invoice رقم 123 (USD)", style), 160);

            lines.Should().NotBeEmpty();

            var ink = DrawLines(CreateBlock("Invoice رقم 123 (USD)", style), 160);
            var colors = ink.Runs.Select(x => x.Color).Distinct().ToList();

            colors.Should().Contain("#ff0000", "the Arabic fallback carries its own style");
            colors.Should().Contain("#000000", "the Latin text uses the primary style");
        }

        [Test]
        public void Layout_Arabic_RightToLeftDirectionRendersWithoutLoss()
        {
            const string text = "العربية 42 English";

            var leftToRight = MeasureLines(CreateBlock(text, CreateStyle(ComplexScriptFonts.Arabic)), 60);
            var rightToLeft = MeasureLines(
                CreateBlock(text, CreateStyle(ComplexScriptFonts.Arabic, TextDirection.RightToLeft)),
                60);

            leftToRight.Should().NotBeEmpty();
            rightToLeft.Should().NotBeEmpty();

            var ltrInk = DrawLines(CreateBlock(text, CreateStyle(ComplexScriptFonts.Arabic)), 60);
            var rtlInk = DrawLines(
                CreateBlock(text, CreateStyle(ComplexScriptFonts.Arabic, TextDirection.RightToLeft)),
                60);

            ltrInk.HasInk.Should().BeTrue();
            rtlInk.HasInk.Should().BeTrue();
            rtlInk.Left.Should().BeApproximately(ltrInk.Left, 0.01f, "right-to-left text is drawn from the same origin");
        }

        [Test]
        public void Layout_Cjk_WrapsWithoutSpaces()
        {
            const string text = "あいうえおかきくけこさしすせそ";

            var lines = MeasureLines(CreateBlock(text, CreateStyle(ComplexScriptFonts.Cjk)), 60);

            // A full-width character is 12 points wide, so a 60-point line holds five characters.
            lines.Should().HaveCountGreaterThan(1);
            GetLineTexts(lines).Should().Equal("あいうえお", "かきくけこ", "さしすせそ");
        }

        [Test]
        public void Layout_Cjk_DoesNotStartALineWithClosingPunctuation()
        {
            const string text = "あいうえ。」かきくけこ";

            var lines = MeasureLines(CreateBlock(text, CreateStyle(ComplexScriptFonts.Cjk)), 60);
            var texts = GetLineTexts(lines);

            texts.Should().HaveCountGreaterThan(1);
            texts[0].Should().Be("あいう");

            foreach (var line in texts.Skip(1))
            {
                line.Should().NotBeEmpty();
                "。、，．・：；？！）」』】〕".Should().NotContain(line[0].ToString(),
                    $"line '{line}' must not start with closing punctuation");
            }
        }

        [Test]
        public void Layout_Cjk_DoesNotEndALineWithOpeningPunctuation()
        {
            const string text = "あいうえお「かきくけこ」さしすせそ";

            var lines = MeasureLines(CreateBlock(text, CreateStyle(ComplexScriptFonts.Cjk)), 60);
            var texts = GetLineTexts(lines);

            texts.Should().HaveCountGreaterThan(1);

            foreach (var line in texts.Take(texts.Count - 1))
            {
                line.Should().NotEndWith("「");
                line.Should().NotEndWith("『");
                line.Should().NotEndWith("（");
            }
        }

        [Test]
        public void Layout_Cjk_DoesNotSeparateSmallKanaFromThePrecedingCharacter()
        {
            const string text = "あいうえおっかきくけこ";

            var lines = MeasureLines(CreateBlock(text, CreateStyle(ComplexScriptFonts.Cjk)), 60);
            var texts = GetLineTexts(lines);

            texts[0].Should().Be("あいうえ");
            texts[1].Should().StartWith("おっ");
        }

        [Test]
        public void Layout_Cjk_BreaksAfterIdeographsWhenTheLineStartsWithLatin()
        {
            var items = new List<ITextBlockItem>
            {
                new TextBlockSpan { Text = "123 ", Style = CreateStyle(ComplexScriptFonts.Latin) },
                new TextBlockSpan { Text = "あいうえおかきくけこ", Style = CreateStyle(ComplexScriptFonts.Cjk) }
            };

            var lines = MeasureLines(CreateBlock(items), 60);
            var texts = GetLineTexts(lines);

            texts.Should().HaveCountGreaterThan(1);
            texts[0].Should().Be("123 あいう");
        }

        [Test]
        public void Layout_CombiningMarks_StayWithTheirBaseCharacter()
        {
            const string text = "éééééé";

            var lines = MeasureLines(CreateBlock(text, CreateStyle(ComplexScriptFonts.Latin)), 30);
            var texts = GetLineTexts(lines);

            texts.Should().HaveCountGreaterThan(1);
            texts.Should().OnlyContain(x => x.Length % 2 == 0, "each decomposed character occupies two code units");
        }

        [Test]
        public void Layout_Emoji_SurrogatePairsAreNotSplitBetweenLines()
        {
            const string text = "😀😃🎉❤️👍🏽";

            var lines = MeasureLines(CreateBlock(text, CreateStyle(ComplexScriptFonts.Emoji)), 30);
            var texts = GetLineTexts(lines);

            texts.Should().HaveCountGreaterThan(1);

            foreach (var line in texts)
            {
                char.IsLowSurrogate(line[0]).Should().BeFalse($"line '{line}' must not start inside a surrogate pair");
                char.IsHighSurrogate(line[line.Length - 1]).Should().BeFalse($"line '{line}' must not end inside a surrogate pair");
            }
        }

        [Test]
        public void Layout_ComplexScriptCorpus_HasNoMissingGlyphs()
        {
            var corpus = new[]
            {
                "Invoice رقم 123 (USD)",
                "العربية 42 English",
                "السَّلَامُ عَلَيْكُمْ",
                "שלום עולם",
                "שָׁלוֹם",
                "हिन्दी नमस्ते दुनिया",
                "漢字かなカナ 123",
                "これはテストです。漢字、かな。」次は新しい文です。",
                "😀 😃 🎉 ❤️",
                "é Å Ç"
            };

            var style = CreateFallbackStyle();

            foreach (var text in corpus)
            {
                Action action = () => MeasureLines(CreateBlock(text, style), 160);

                action.Should().NotThrow($"the corpus sample '{text}' must render with the bundled fonts");
            }
        }

        [Test]
        public void Layout_ComplexScriptCorpus_IsDeterministic()
        {
            var style = CreateFallbackStyle();
            const string text = "Invoice رقم 123 (USD) 漢字かなカナ";

            var first = DrawLines(CreateBlock(text, style), 100);
            var second = DrawLines(CreateBlock(text, style), 100);

            first.Runs.Should().HaveCount(second.Runs.Count);

            first.Runs.Zip(second.Runs, (a, b) => (a, b))
                .Should().OnlyContain(x => x.a.PenX == x.b.PenX && x.a.PenY == x.b.PenY);
        }

        // ---------------------------------------------------------------- helpers

        private static DocumentSpecificFontManager CreateFontManager()
        {
            ComplexScriptFonts.Register();
            return FontManager.MakeDocumentSpecific();
        }

        private static TextStyle CreateStyle(
            string fontFamily,
            TextDirection direction = TextDirection.Auto,
            float fontSize = FontSize)
        {
            var style = TextStyle.Default
                .FontFamily(fontFamily)
                .FontSize(fontSize);

            style = direction switch
            {
                TextDirection.LeftToRight => style.DirectionFromLeftToRight(),
                TextDirection.RightToLeft => style.DirectionFromRightToLeft(),
                _ => style.DirectionAuto()
            };

            return style.ApplyGlobalStyle();
        }

        private static TextStyle CreateFallbackStyle()
        {
            return CreateStyle(ComplexScriptFonts.Latin)
                .Fallback(CreateStyle(ComplexScriptFonts.Arabic)
                    .Fallback(CreateStyle(ComplexScriptFonts.Hebrew)
                        .Fallback(CreateStyle(ComplexScriptFonts.Devanagari)
                            .Fallback(CreateStyle(ComplexScriptFonts.Cjk)
                                .Fallback(CreateStyle(ComplexScriptFonts.Emoji))))));
        }

        private static TextShapingResult Shape(string text, TextStyle style)
        {
            return new TextShaper(style, CreateFontManager()).Shape(text);
        }

        private static List<ushort> GlyphIds(TextShapingResult result)
        {
            return Enumerable.Range(0, result.Length)
                .Select(x => result[x].Codepoint)
                .OrderBy(x => x)
                .ToList();
        }

        private static TextBlock CreateBlock(string text, TextStyle style)
        {
            return CreateBlock(new List<ITextBlockItem>
            {
                new TextBlockSpan { Text = text, Style = style }
            });
        }

        private static TextBlock CreateBlock(IReadOnlyList<ITextBlockItem> items)
        {
            return new TextBlock
            {
                Alignment = TextHorizontalAlignment.Start,
                Items = items.ToList()
            };
        }

        private static List<TextLine> MeasureLines(TextBlock block, float width)
        {
            block.PageContext = new PageContext
            {
                FontManager = CreateFontManager(),
                CheckIfAllTextGlyphsAreAvailable = true
            };

            block.ResetState();
            return block.DivideTextItemsIntoLines(width, 10000).ToList();
        }

        private static List<string> GetLineTexts(IEnumerable<TextLine> lines)
        {
            return lines.Select(line =>
            {
                var builder = new StringBuilder();

                foreach (var element in line.Elements)
                {
                    if (element.Item is not TextBlockSpan span)
                        continue;

                    var start = element.Measurement.StartIndex;
                    var end = element.Measurement.EndIndex;

                    // measurements use glyph indices; map them back to the shaped character clusters
                    var startCharacter = span.GetCharacterIndex(start);
                    var endCharacter = end + 1 < span.GetGlyphCount()
                        ? span.GetCharacterIndex(end + 1)
                        : span.Text.Length;

                    if (endCharacter > startCharacter)
                        builder.Append(span.Text.Substring(startCharacter, endCharacter - startCharacter));
                }

                return builder.ToString();
            }).ToList();
        }

        private static InkRecordingCanvas DrawLines(TextBlock block, float width)
        {
            block.PageContext = new PageContext
            {
                FontManager = CreateFontManager(),
                CheckIfAllTextGlyphsAreAvailable = true
            };

            block.ResetState();

            var canvas = new InkRecordingCanvas();
            block.Canvas = canvas;
            block.Draw(new Size(width, 10000));

            return canvas;
        }

        private sealed class InkRecordingCanvas : ICanvas
        {
            private float _offsetX;
            private float _offsetY;

            public List<InkRun> Runs { get; } = new();

            public float Left { get; private set; } = float.MaxValue;
            public float Top { get; private set; } = float.MaxValue;
            public float Right { get; private set; } = float.MinValue;
            public float Bottom { get; private set; } = float.MinValue;

            public bool HasInk => Right > Left;

            public void Translate(Position vector)
            {
                _offsetX += vector.X;
                _offsetY += vector.Y;
            }

            public void DrawText(SKTextBlob skTextBlob, Position position, TextStyle style, DocumentSpecificFontManager fontManager)
            {
                var bounds = skTextBlob.Bounds;
                var penX = _offsetX + position.X;
                var penY = _offsetY + position.Y;

                Runs.Add(new InkRun(penX, penY, bounds, style.Color));

                Left = Math.Min(Left, penX + bounds.Left);
                Top = Math.Min(Top, penY + bounds.Top);
                Right = Math.Max(Right, penX + bounds.Right);
                Bottom = Math.Max(Bottom, penY + bounds.Bottom);
            }

            public void DrawRectangle(Position vector, Size size, string color) { }
            public void DrawRectangle(Position vector, Size size, RectangleStyle style) { }
            public void SaveState() { }
            public void RestoreState() { }
            public void ClipRectangle(Position vector, Size size, BorderRadius cornerRadius) { }
            public void DrawImage(SKImage image, Position position, Size size) { }
            public void DrawHyperlink(string url, Size size) { }
            public void DrawSectionLink(string sectionName, Size size) { }
            public void DrawSection(string sectionName) { }
            public void Rotate(float angle) { }
            public void Scale(float scaleX, float scaleY) { }
        }

        private readonly struct InkRun
        {
            public InkRun(float penX, float penY, SKRect bounds, string color)
            {
                PenX = penX;
                PenY = penY;
                Bounds = bounds;
                Color = color;
            }

            public float PenX { get; }
            public float PenY { get; }
            public SKRect Bounds { get; }
            public string Color { get; }
        }
    }
}
