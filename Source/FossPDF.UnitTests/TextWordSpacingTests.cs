using System.Collections.Generic;
using FluentAssertions;
using FossPDF.Drawing;
using FossPDF.Elements.Text;
using FossPDF.Elements.Text.Items;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;

namespace FossPDF.UnitTests
{
    [TestFixture]
    public class TextWordSpacingTests
    {
        private const float Tolerance = 0.01f;

        [Test]
        public void Measure_WordSpacing_AddsSpacePerWordGapRelativeToFontSize()
        {
            // arrange
            const string text = "one two three four";

            // act
            var defaultWidth = MeasureWidth(CreateBlock(text, CreateStyle(0)));
            var spacedWidth = MeasureWidth(CreateBlock(text, CreateStyle(1)));

            // assert - three spaces, factor 1, 12 points
            spacedWidth.Should().BeApproximately(defaultWidth + 3 * 1 * 12, Tolerance);
        }

        [Test]
        public void Measure_HalfWordSpacing_AddsHalfFontSizePerWordGap()
        {
            // arrange
            const string text = "one two three four";

            // act
            var defaultWidth = MeasureWidth(CreateBlock(text, CreateStyle(0)));
            var spacedWidth = MeasureWidth(CreateBlock(text, CreateStyle(0.5f)));

            // assert
            spacedWidth.Should().BeApproximately(defaultWidth + 3 * 0.5f * 12, Tolerance);
        }

        [Test]
        public void Measure_ZeroWordSpacing_MatchesDefaultStyle()
        {
            // arrange
            const string text = "one two three";
            var explicitZero = CreateStyle(0);
            var withoutWordSpacing = TextStyle.Default
                .FontFamily("Lato")
                .FontSize(12)
                .ApplyGlobalStyle();

            // act
            var explicitZeroWidth = MeasureWidth(CreateBlock(text, explicitZero));
            var defaultWidth = MeasureWidth(CreateBlock(text, withoutWordSpacing));

            // assert
            explicitZeroWidth.Should().BeApproximately(defaultWidth, Tolerance);
        }

        [Test]
        public void Measure_WordSpacing_ContributesToLineBreaking()
        {
            // arrange
            const string text = "alpha beta gamma delta epsilon";
            var defaultBlock = CreateBlock(text, CreateStyle(0));
            var spacedBlock = CreateBlock(text, CreateStyle(4));

            var singleLineWidth = MeasureWidth(defaultBlock);

            // act
            var defaultMeasurement = Measure(defaultBlock, singleLineWidth);
            var spacedMeasurement = Measure(spacedBlock, singleLineWidth);

            // assert
            defaultMeasurement.Height.Should().BeLessThan(spacedMeasurement.Height);
        }

        [Test]
        public void Measure_RtlWordSpacing_AddsSpacePerWordGap()
        {
            // arrange
            const string text = "one two three four";
            var defaultWidth = MeasureWidth(CreateBlock(text, CreateStyle(0, TextDirection.RightToLeft)));
            var spacedWidth = MeasureWidth(CreateBlock(text, CreateStyle(1, TextDirection.RightToLeft)));

            // act & assert
            spacedWidth.Should().BeApproximately(defaultWidth + 3 * 1 * 12, Tolerance);
        }

        [Test]
        public void ApplyInheritedStyle_PropagatesWordSpacing()
        {
            // arrange
            var parent = TextStyle.Default.WordSpacing(1.5f);
            var child = TextStyle.Default.FontSize(10);

            // act
            var result = child.ApplyInheritedStyle(parent);

            // assert
            result.WordSpacing.Should().Be(1.5f);
        }

        [Test]
        public void ApplyGlobalStyle_DefaultsWordSpacingToZero()
        {
            // act
            var result = TextStyle.Default.FontSize(10).ApplyGlobalStyle();

            // assert
            result.WordSpacing.Should().Be(0);
        }

        [Test]
        public void ApplyInheritedStyle_KeepsFallbackWordSpacing()
        {
            // arrange
            var style = TextStyle.Default
                .FontFamily("Lato")
                .Fallback(x => x.FontFamily("Lato").WordSpacing(3));

            // act
            var result = style.ApplyInheritedStyle(TextStyle.Default).ApplyGlobalStyle();

            // assert
            result.Fallback.WordSpacing.Should().Be(3);
        }

        private static TextStyle CreateStyle(float wordSpacing, TextDirection direction = TextDirection.LeftToRight)
        {
            return TextStyle.Default
                .FontFamily("Lato")
                .FontSize(12)
                .WordSpacing(wordSpacing)
                .Mutate(TextStyleProperty.Direction, direction)
                .ApplyGlobalStyle();
        }

        private static TextBlock CreateBlock(string text, TextStyle style)
        {
            return new TextBlock
            {
                Items = new List<ITextBlockItem>
                {
                    new TextBlockSpan
                    {
                        Text = text,
                        Style = style
                    }
                }
            };
        }

        private static float MeasureWidth(TextBlock block, float availableWidth = 1000)
        {
            return Measure(block, availableWidth).Width;
        }

        private static SpacePlan Measure(TextBlock block, float availableWidth)
        {
            block.PageContext = CreatePageContext();
            block.ResetState();

            return block.Measure(new Size(availableWidth, 1000));
        }

        private static PageContext CreatePageContext()
        {
            var fontManager = new DocumentSpecificFontManager();

            using var stream = typeof(TextStyle).Assembly
                .GetManifestResourceStream("FossPDF.Resources.DefaultFont.Lato-Regular.ttf");

            fontManager.RegisterFont(stream);

            return new PageContext
            {
                FontManager = fontManager,
                CheckIfAllTextGlyphsAreAvailable = false
            };
        }
    }
}
