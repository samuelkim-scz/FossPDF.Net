using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FossPDF.Drawing;
using FossPDF.Elements.Text;
using FossPDF.Elements.Text.Calculation;
using FossPDF.Elements.Text.Items;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.UnitTests.TestEngine;
using FossPDF.UnitTests.TestEngine.Operations;
using NUnit.Framework;

namespace FossPDF.UnitTests
{
    [TestFixture]
    public class TextParagraphLayoutTests
    {
        private const float Tolerance = 0.001f;

        [Test]
        public void Draw_FirstLineIndentation_AppliesOnlyToFirstLine()
        {
            // arrange
            var span = new StubSpan(widths: new[] { 50f, 60f });
            var block = Prepare(CreateBlock(span, TextHorizontalAlignment.Left, firstLineIndentation: 10f));
            var canvas = Draw(block, new Size(200, 1000));

            // assert
            var translations = Translations(canvas);
            translations.Should().HaveCount(4);

            translations[0].Position.Should().Be(new Position(10, 8));
            translations[2].Position.Should().Be(new Position(0, 18));
        }

        [Test]
        public void Draw_ParagraphContinuingOnNextPage_DoesNotIndentContinuation()
        {
            // arrange
            var span = new StubSpan(widths: new[] { 50f, 70f });
            var block = Prepare(CreateBlock(span, TextHorizontalAlignment.Left, firstLineIndentation: 10f));

            // act - the first page fits only a single line
            Draw(block, new Size(200, 10));
            var secondPage = Draw(block, new Size(200, 1000));

            // assert - the continuation line keeps the paragraph origin
            var translations = Translations(secondPage);
            translations.Should().HaveCount(2);
            translations[0].Position.X.Should().BeApproximately(0, Tolerance);
            span.Requests.Last().TextSize.Width.Should().BeApproximately(70, Tolerance);
        }

        [Test]
        public void Measure_JustifiedLine_ReportsFullBoxWidth()
        {
            // arrange
            var span = new StubSpan(widths: new[] { 80f, 50f }, spaces: new[] { 2, 1 });
            var block = Prepare(CreateBlock(span, TextHorizontalAlignment.Justify));

            // act
            var measurement = block.Measure(new Size(100, 1000));

            // assert
            measurement.Width.Should().BeApproximately(100, Tolerance);
            measurement.Height.Should().BeApproximately(20, Tolerance);
        }

        [Test]
        public void Measure_JustifiedLineWithIndentation_ReportsReducedBoxWidth()
        {
            // arrange
            var span = new StubSpan(widths: new[] { 80f, 50f }, spaces: new[] { 2, 1 });
            var block = Prepare(CreateBlock(span, TextHorizontalAlignment.Justify, firstLineIndentation: 10f));

            // act
            var measurement = block.Measure(new Size(100, 1000));

            // assert
            measurement.Width.Should().BeApproximately(90, Tolerance);
        }

        [Test]
        public void Draw_Justify_DistributesExtraSpaceAcrossWordGaps()
        {
            // arrange
            var span = new StubSpan(widths: new[] { 80f, 50f }, spaces: new[] { 2, 1 });
            var block = Prepare(CreateBlock(span, TextHorizontalAlignment.Justify));

            // act
            Draw(block, new Size(100, 1000));

            // assert
            span.Requests.Should().HaveCount(2);

            var justifiedLine = span.Requests[0];
            justifiedLine.AdditionalWordSpacing.Should().BeApproximately(10, Tolerance);
            justifiedLine.TextSize.Width.Should().BeApproximately(100, Tolerance);

            var finalLine = span.Requests[1];
            finalLine.AdditionalWordSpacing.Should().BeApproximately(0, Tolerance);
            finalLine.TextSize.Width.Should().BeApproximately(50, Tolerance);
        }

        [Test]
        public void Draw_Justify_LineWithoutSpacesIsAlignedToStart()
        {
            // arrange
            var span = new StubSpan(widths: new[] { 60f, 40f }, spaces: new[] { 0, 0 });
            var block = Prepare(CreateBlock(span, TextHorizontalAlignment.Justify));

            // act
            var canvas = Draw(block, new Size(100, 1000));

            // assert
            Translations(canvas)[0].Position.X.Should().BeApproximately(0, Tolerance);
            span.Requests[0].AdditionalWordSpacing.Should().BeApproximately(0, Tolerance);
            span.Requests[0].TextSize.Width.Should().BeApproximately(60, Tolerance);
        }

        [Test]
        public void Draw_MultiSpanCenteredLine_PlacesItemsSequentiallyWithoutOverlap()
        {
            // arrange
            var first = new StubSpan(widths: new[] { 40f });
            var second = new StubSpan(widths: new[] { 40f });
            var block = Prepare(CreateBlock(new ITextBlockItem[] { first, second }, TextHorizontalAlignment.Center));

            // act
            var canvas = Draw(block, new Size(200, 1000));

            // assert - free space (120) is split around the 80-point line
            var translations = Translations(canvas);
            translations[0].Position.X.Should().BeApproximately(60, Tolerance);
            translations[2].Position.X.Should().BeApproximately(100, Tolerance);
        }

        [Test]
        public void Draw_MultiSpanRightAlignedLine_PlacesItemsSequentiallyWithoutOverlap()
        {
            // arrange
            var first = new StubSpan(widths: new[] { 40f });
            var second = new StubSpan(widths: new[] { 40f });
            var block = Prepare(CreateBlock(new ITextBlockItem[] { first, second }, TextHorizontalAlignment.Right));

            // act
            var canvas = Draw(block, new Size(200, 1000));

            // assert
            var translations = Translations(canvas);
            translations[0].Position.X.Should().BeApproximately(120, Tolerance);
            translations[2].Position.X.Should().BeApproximately(160, Tolerance);
        }

        [Test]
        public void Draw_LtrStartAndEnd_ResolveToPhysicalEdges()
        {
            // arrange
            var start = Prepare(CreateBlock(new StubSpan(widths: new[] { 80f }), TextHorizontalAlignment.Start));
            var end = Prepare(CreateBlock(new StubSpan(widths: new[] { 80f }), TextHorizontalAlignment.End));

            // act
            var startCanvas = Draw(start, new Size(200, 1000));
            var endCanvas = Draw(end, new Size(200, 1000));

            // assert
            Translations(startCanvas)[0].Position.X.Should().BeApproximately(0, Tolerance);
            Translations(endCanvas)[0].Position.X.Should().BeApproximately(120, Tolerance);
        }

        [Test]
        public void Draw_RtlStartAndEnd_ResolveToLogicalEdges()
        {
            // arrange
            var startSpan = new StubSpan(widths: new[] { 40f });
            var startOther = new StubSpan(widths: new[] { 60f });
            var endSpan = new StubSpan(widths: new[] { 40f });
            var endOther = new StubSpan(widths: new[] { 60f });
            var start = Prepare(CreateBlock(new ITextBlockItem[] { startSpan, startOther }, TextHorizontalAlignment.Start,
                contentDirection: ContentDirection.RightToLeft));
            var end = Prepare(CreateBlock(new ITextBlockItem[] { endSpan, endOther }, TextHorizontalAlignment.End,
                contentDirection: ContentDirection.RightToLeft));

            // act
            var startCanvas = Draw(start, new Size(200, 1000));
            var endCanvas = Draw(end, new Size(200, 1000));

            // assert - start is the right edge, end is the left edge
            Translations(startCanvas)[0].Position.X.Should().BeApproximately(160, Tolerance);
            Translations(endCanvas)[0].Position.X.Should().BeApproximately(60, Tolerance);
        }

        [Test]
        public void Draw_RtlIndentedLine_IndentsAtLogicalStart()
        {
            // arrange
            var span = new StubSpan(widths: new[] { 40f });
            var block = Prepare(CreateBlock(span, TextHorizontalAlignment.Start, firstLineIndentation: 10f,
                contentDirection: ContentDirection.RightToLeft));

            // act
            var canvas = Draw(block, new Size(200, 1000));

            // assert - right-to-left indentation reduces the right edge
            Translations(canvas)[0].Position.X.Should().BeApproximately(150, Tolerance);
        }

        [Test]
        public void Draw_JustifiedDecoratedSpan_DrawsExpandedDecorationWidth()
        {
            // arrange
            var span = new StubSpan(widths: new[] { 80f, 50f }, spaces: new[] { 2, 0 }, drawDecoration: true);
            var block = Prepare(CreateBlock(span, TextHorizontalAlignment.Justify));

            // act
            var canvas = Draw(block, new Size(100, 1000));

            // assert
            var rectangle = canvas.Operations.OfType<CanvasDrawRectangleOperation>().First();
            rectangle.Size.Width.Should().BeApproximately(100, Tolerance);
        }

        [Test]
        public void ParagraphFirstLineIndentation_RejectsInvalidValues()
        {
            // arrange
            var descriptor = new TextDescriptor();

            // assert
            Assert.Throws<ArgumentOutOfRangeException>(() => descriptor.ParagraphFirstLineIndentation(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => descriptor.ParagraphFirstLineIndentation(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => descriptor.ParagraphFirstLineIndentation(float.PositiveInfinity));
        }

        private static TextBlock CreateBlock(
            StubSpan span,
            TextHorizontalAlignment alignment,
            float firstLineIndentation = 0,
            ContentDirection contentDirection = ContentDirection.LeftToRight)
        {
            return CreateBlock(new ITextBlockItem[] { span }, alignment, firstLineIndentation, contentDirection);
        }

        private static TextBlock CreateBlock(
            ITextBlockItem[] items,
            TextHorizontalAlignment alignment,
            float firstLineIndentation = 0,
            ContentDirection contentDirection = ContentDirection.LeftToRight)
        {
            return new TextBlock
            {
                Alignment = alignment,
                FirstLineIndentation = firstLineIndentation,
                ContentDirection = contentDirection,
                Items = items.ToList()
            };
        }

        private static TextBlock Prepare(TextBlock block)
        {
            block.PageContext = new PageContext
            {
                FontManager = new DocumentSpecificFontManager(),
                CheckIfAllTextGlyphsAreAvailable = false
            };

            block.ResetState();
            return block;
        }

        private static OperationRecordingCanvas Draw(TextBlock block, Size availableSpace)
        {
            var canvas = new OperationRecordingCanvas();

            block.Canvas = canvas;
            block.Draw(availableSpace);

            return canvas;
        }

        private static List<CanvasTranslateOperation> Translations(OperationRecordingCanvas canvas)
        {
            return canvas.Operations.OfType<CanvasTranslateOperation>().ToList();
        }

        private class StubSpan : TextBlockSpan
        {
            private readonly float[] _widths;
            private readonly int[] _spaces;
            private readonly bool _drawDecoration;

            public StubSpan(float[] widths, int[] spaces = null, bool drawDecoration = false)
            {
                _widths = widths;
                _spaces = spaces ?? new int[widths.Length];
                _drawDecoration = drawDecoration;
            }

            public List<TextDrawingRequest> Requests { get; } = new();

            public override TextMeasurementResult Measure(TextMeasurementRequest request)
            {
                if (request.StartIndex >= _widths.Length)
                    return null;

                var width = _widths[request.StartIndex];

                if (width > request.AvailableWidth + Tolerance)
                    return null;

                return new TextMeasurementResult
                {
                    Width = width,
                    SpaceCount = _spaces[request.StartIndex],

                    Ascent = -8,
                    Descent = 2,
                    LineHeight = 1,

                    StartIndex = request.StartIndex,
                    EndIndex = request.StartIndex,
                    NextIndex = request.StartIndex + 1,
                    TotalIndex = _widths.Length - 1
                };
            }

            public override void Draw(TextDrawingRequest request)
            {
                Requests.Add(request);

                if (_drawDecoration)
                {
                    request.Canvas.DrawRectangle(
                        new Position(0, request.TotalAscent),
                        new Size(request.TextSize.Width, request.TextSize.Height),
                        "#ff0000");
                }
            }
        }
    }
}
