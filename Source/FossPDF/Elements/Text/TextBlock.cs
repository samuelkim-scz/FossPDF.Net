using System;
using System.Collections.Generic;
using System.Linq;
using FossPDF.Drawing;
using FossPDF.Elements.Text.Calculation;
using FossPDF.Elements.Text.Items;
using FossPDF.Infrastructure;

namespace FossPDF.Elements.Text
{
    internal class TextBlock : Element, IStateResettable, IStateSnapshot, IContentDirectionAware
    {
        public ContentDirection ContentDirection { get; set; }

        public TextHorizontalAlignment? Alignment { get; set; }

        /// <summary>
        /// Indentation applied to the first line of the paragraph, measured from the logical
        /// start edge. Lines that continue a paragraph on the next page are not indented.
        /// </summary>
        public float FirstLineIndentation { get; set; }

        public List<ITextBlockItem> Items { get; set; } = new List<ITextBlockItem>();

        public int? LineClamp { get; set; }
        public string LineClampEllipsis { get; set; } = "…";

        public string Text => string.Join(" ", Items.OfType<TextBlockSpan>().Select(x => x.Text));

        private Queue<ITextBlockItem> RenderingQueue { get; } = new();
        private int CurrentElementIndex { get; set; }
        private int RenderedLineCount { get; set; }

        private bool FontFallbackApplied { get; set; } = false;

        public void ResetState()
        {
            ApplyFontFallback();
            InitializeQueue();
            CurrentElementIndex = 0;
            RenderedLineCount = 0;

            void InitializeQueue()
            {
                RenderingQueue.Clear();

                foreach (var item in Items)
                    RenderingQueue.Enqueue(item);
            }

            void ApplyFontFallback()
            {
                if (FontFallbackApplied)
                    return;

                Items = Items.ApplyFontFallback(PageContext).ToList();
                FontFallbackApplied = true;
            }
        }

        object? IStateSnapshot.CaptureState()
        {
            return (
                new Queue<ITextBlockItem>(RenderingQueue),
                CurrentElementIndex,
                FontFallbackApplied);
        }

        void IStateSnapshot.RestoreState(object? state)
        {
            if (state is not ValueTuple<Queue<ITextBlockItem>, int, bool> snapshot)
                return;

            var (queue, elementIndex, fontFallbackApplied) = snapshot;

            RenderingQueue.Clear();

            foreach (var item in queue)
                RenderingQueue.Enqueue(item);

            CurrentElementIndex = elementIndex;
            FontFallbackApplied = fontFallbackApplied;
        }

        void SetDefaultAlignment()
        {
            Alignment ??= TextHorizontalAlignment.Start;
        }

        internal override SpacePlan Measure(Size availableSpace)
        {
            SetDefaultAlignment();

            if (!RenderingQueue.Any())
                return SpacePlan.FullRender(Size.Zero);

            var lines = CalculateLines(availableSpace.Width, availableSpace.Height, out var clampReached);

            if (!lines.Any())
                return clampReached ? SpacePlan.FullRender(Size.Zero) : SpacePlan.Wrap();

            var width = lines.Max(GetLineWidth);
            var height = lines.Sum(x => x.LineHeight);

            if (width > availableSpace.Width + Size.Epsilon || height > availableSpace.Height + Size.Epsilon)
                return SpacePlan.Wrap();

            if (clampReached)
                return SpacePlan.FullRender(width, height);

            var fullyRenderedItemsCount = lines
                .SelectMany(x => x.Elements)
                .GroupBy(x => x.Item)
                .Count(x => x.Any(y => y.Measurement.IsLast));

            if (fullyRenderedItemsCount == RenderingQueue.Count)
                return SpacePlan.FullRender(width, height);

            return SpacePlan.PartialRender(width, height);

            float GetLineWidth(TextLine line)
            {
                var alignment = TextLineLayout.ResolveAlignment(Alignment.GetValueOrDefault(TextHorizontalAlignment.Start), line.IsLastLineOfParagraph, line.SpaceCount);

                // A justified line always occupies the full width of its line box.
                return alignment == TextHorizontalAlignment.Justify ? line.BoxWidth : line.Width;
            }
        }

        internal override void Draw(Size availableSpace)
        {
            SetDefaultAlignment();

            var lines = CalculateLines(availableSpace.Width, availableSpace.Height, out var clampReached);

            if (!lines.Any())
                return;

            var topOffset = 0f;

            foreach (var line in lines)
            {
                var alignment = TextLineLayout.ResolveAlignment(Alignment.GetValueOrDefault(TextHorizontalAlignment.Start), line.IsLastLineOfParagraph, line.SpaceCount);
                var lineBox = TextLineLayout.GetLineBox(availableSpace.Width, ContentDirection, line.Indentation);
                var additionalWordSpacing = TextLineLayout.GetAdditionalWordSpacing(alignment, lineBox, line.Width, line.SpaceCount);

                var renderedWidth = line.Width + line.SpaceCount * additionalWordSpacing;

                // For left-to-right content, the cursor is the left edge of the next item.
                // For right-to-left content, it is the right edge of the next item.
                var cursor = TextLineLayout.GetContentStart(alignment, ContentDirection, lineBox, renderedWidth);

                foreach (var item in line.Elements.Where(x => x.Measurement.Width > 0.0))
                {
                    var itemWidth = item.Measurement.Width + item.Measurement.SpaceCount * additionalWordSpacing;

                    var itemOffsetX = ContentDirection == ContentDirection.LeftToRight
                        ? cursor
                        : cursor - itemWidth;

                    var textDrawingRequest = new TextDrawingRequest
                    {
                        Canvas = Canvas,
                        PageContext = PageContext,

                        StartIndex = item.Measurement.StartIndex,
                        EndIndex = item.Measurement.EndIndex,

                        TextSize = new Size(itemWidth, line.LineHeight),
                        TotalAscent = line.Ascent,

                        AdditionalWordSpacing = additionalWordSpacing
                    };

                    var canvasOffset = new Position(itemOffsetX, topOffset - line.Ascent);

                    Canvas.Translate(canvasOffset);
                    item.Item.Draw(textDrawingRequest);
                    Canvas.Translate(canvasOffset.Reverse());

                    cursor = ContentDirection == ContentDirection.LeftToRight
                        ? cursor + itemWidth
                        : cursor - itemWidth;
                }

                topOffset += line.LineHeight;
            }

            if (clampReached)
            {
                RenderingQueue.Clear();
                CurrentElementIndex = 0;
            }
            else
            {
                lines
                    .SelectMany(x => x.Elements)
                    .GroupBy(x => x.Item)
                    .Where(x => x.Any(y => y.Measurement.IsLast))
                    .Select(x => x.Key)
                    .ToList()
                    .ForEach(x => RenderingQueue.Dequeue());

                var lastElementMeasurement = lines.Last().Elements.Last().Measurement;
                CurrentElementIndex = lastElementMeasurement.IsLast ? 0 : lastElementMeasurement.NextIndex;
                RenderedLineCount += lines.Count;
            }

            if (!RenderingQueue.Any())
                ResetState();
        }

        public IEnumerable<TextLine> DivideTextItemsIntoLines(float availableWidth, float availableHeight)
        {
            return DivideTextItemsIntoLines(availableWidth, availableHeight, int.MaxValue, 0);
        }

        internal List<TextLine> CalculateLines(float availableWidth, float availableHeight, out bool clampReached)
        {
            clampReached = false;

            if (!RenderingQueue.Any())
                return new List<TextLine>();

            if (LineClamp.HasValue)
            {
                var remainingLines = Math.Max(0, LineClamp.Value - RenderedLineCount);

                if (remainingLines == 0)
                {
                    clampReached = true;
                    return new List<TextLine>();
                }

                var ellipsis = LineClampEllipsis ?? string.Empty;
                var probeLimit = remainingLines == int.MaxValue ? int.MaxValue : remainingLines + 1;
                var probe = DivideTextItemsIntoLines(availableWidth, availableHeight, probeLimit, 0).ToList();

                if (probe.Count <= remainingLines)
                    return probe;

                var lastVisibleLine = probe[remainingLines - 1];
                var ellipsisStyle = lastVisibleLine.Elements
                                        .Select(x => x.Item)
                                        .OfType<TextBlockSpan>()
                                        .LastOrDefault()?.Style
                                    ?? Items.OfType<TextBlockSpan>().LastOrDefault()?.Style;

                if (ellipsisStyle == null || string.IsNullOrEmpty(ellipsis))
                {
                    clampReached = true;
                    return probe.Take(remainingLines).ToList();
                }

                var ellipsisMeasurement = MeasureEllipsis(ellipsis, ellipsisStyle, availableWidth);
                var lines = DivideTextItemsIntoLines(
                    availableWidth,
                    availableHeight,
                    remainingLines,
                    ellipsisMeasurement?.Width ?? 0).ToList();

                if (ellipsisMeasurement != null)
                {
                    var ellipsisElement = new TextLineElement
                    {
                        Item = new TextBlockSpan
                        {
                            Text = ellipsis,
                            Style = ellipsisStyle
                        },
                        Measurement = ellipsisMeasurement
                    };

                    if (lines.Any())
                    {
                        var lastLine = lines[^1];
                        lines[^1] = TextLine.From(
                            lastLine.Elements.Concat(new[] { ellipsisElement }).ToArray(),
                            lastLine.Indentation,
                            lastLine.BoxWidth,
                            lastLine.IsLastLineOfParagraph);
                    }
                    else
                    {
                        lines.Add(TextLine.From(new[] { ellipsisElement }, 0, availableWidth, true));
                    }
                }

                clampReached = true;
                return lines;
            }

            return DivideTextItemsIntoLines(availableWidth, availableHeight, int.MaxValue, 0).ToList();
        }

        private TextMeasurementResult? MeasureEllipsis(string ellipsis, TextStyle style, float availableWidth)
        {
            var span = new TextBlockSpan
            {
                Text = ellipsis,
                Style = style
            };

            return span.Measure(new TextMeasurementRequest
            {
                Canvas = Canvas,
                PageContext = PageContext,
                StartIndex = 0,
                AvailableWidth = availableWidth,
                IsFirstElementInBlock = true,
                IsFirstElementInLine = true
            });
        }

        internal IEnumerable<TextLine> DivideTextItemsIntoLines(
            float availableWidth,
            float availableHeight,
            int maxLines,
            float lastLineWidthReserve)
        {
            var queue = new Queue<ITextBlockItem>(RenderingQueue);
            var currentItemIndex = CurrentElementIndex;
            var currentHeight = 0f;
            var lineIndex = 0;

            // The first line of a paragraph is indented. Continuation lines and parts of a
            // paragraph that flow onto the next page keep the paragraph origin.
            var isFirstLineOfParagraph = currentItemIndex == 0 &&
                                         queue.Any() &&
                                         ReferenceEquals(queue.Peek(), Items.First());

            while (queue.Any() && lineIndex < maxLines)
            {
                var indentation = isFirstLineOfParagraph ? FirstLineIndentation : 0;
                var lineWidth = lineIndex == maxLines - 1
                    ? Math.Max(0, availableWidth - indentation - lastLineWidthReserve)
                    : Math.Max(0, availableWidth - indentation);

                var line = GetNextLine(indentation, lineWidth);

                if (!line.Elements.Any())
                    yield break;

                if (currentHeight + line.LineHeight > availableHeight + Size.Epsilon)
                    yield break;

                currentHeight += line.LineHeight;
                isFirstLineOfParagraph = false;
                lineIndex++;
                yield return line;
            }

            TextLine GetNextLine(float indentation, float lineWidth)
            {
                var lineBoxWidth = Math.Max(0, availableWidth - indentation);
                var currentWidth = 0f;

                var currentLineElements = new List<TextLineElement>();

                while (true)
                {
                    if (!queue.Any())
                        break;

                    var currentElement = queue.Peek();

                    var measurementRequest = new TextMeasurementRequest
                    {
                        Canvas = Canvas,
                        PageContext = PageContext,

                        StartIndex = currentItemIndex,
                        AvailableWidth = Math.Max(0, lineWidth - currentWidth),

                        IsFirstElementInBlock = currentElement == Items.First(),
                        IsFirstElementInLine = !currentLineElements.Any()
                    };

                    var measurementResponse = currentElement.Measure(measurementRequest);

                    if (measurementResponse == null)
                        break;

                    currentLineElements.Add(new TextLineElement
                    {
                        Item = currentElement,
                        Measurement = measurementResponse
                    });

                    currentWidth += measurementResponse.Width;
                    currentItemIndex = measurementResponse.NextIndex;

                    if (!measurementResponse.IsLast)
                        break;

                    currentItemIndex = 0;
                    queue.Dequeue();
                }

                // The paragraph ends only when all items are consumed and the final item
                // was rendered to its end. A line cut by available height is a continuation.
                var isLastLineOfParagraph = !queue.Any() &&
                                            currentLineElements.Any() &&
                                            currentLineElements.Last().Measurement.IsLast;

                return TextLine.From(currentLineElements, indentation, lineBoxWidth, isLastLineOfParagraph);
            }
        }
    }
}
