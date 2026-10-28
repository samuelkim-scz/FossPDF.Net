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

        public string Text => string.Join(" ", Items.OfType<TextBlockSpan>().Select(x => x.Text));

        private Queue<ITextBlockItem> RenderingQueue { get; } = new();
        private int CurrentElementIndex { get; set; }

        private bool FontFallbackApplied { get; set; } = false;

        public void ResetState()
        {
            ApplyFontFallback();
            InitializeQueue();
            CurrentElementIndex = 0;

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

            var lines = DivideTextItemsIntoLines(availableSpace.Width, availableSpace.Height).ToList();

            if (!lines.Any())
                return SpacePlan.Wrap();

            var width = lines.Max(GetLineWidth);
            var height = lines.Sum(x => x.LineHeight);

            if (width > availableSpace.Width + Size.Epsilon || height > availableSpace.Height + Size.Epsilon)
                return SpacePlan.Wrap();

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

            var lines = DivideTextItemsIntoLines(availableSpace.Width, availableSpace.Height).ToList();

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

            lines
                .SelectMany(x => x.Elements)
                .GroupBy(x => x.Item)
                .Where(x => x.Any(y => y.Measurement.IsLast))
                .Select(x => x.Key)
                .ToList()
                .ForEach(x => RenderingQueue.Dequeue());

            var lastElementMeasurement = lines.Last().Elements.Last().Measurement;
            CurrentElementIndex = lastElementMeasurement.IsLast ? 0 : lastElementMeasurement.NextIndex;

            if (!RenderingQueue.Any())
                ResetState();
        }

        public IEnumerable<TextLine> DivideTextItemsIntoLines(float availableWidth, float availableHeight)
        {
            var queue = new Queue<ITextBlockItem>(RenderingQueue);
            var currentItemIndex = CurrentElementIndex;
            var currentHeight = 0f;

            // The first line of a paragraph is indented. Continuation lines and parts of a
            // paragraph that flow onto the next page keep the paragraph origin.
            var isFirstLineOfParagraph = currentItemIndex == 0 &&
                                         queue.Any() &&
                                         ReferenceEquals(queue.Peek(), Items.First());

            while (queue.Any())
            {
                var indentation = isFirstLineOfParagraph ? FirstLineIndentation : 0;
                var line = GetNextLine(indentation);

                if (!line.Elements.Any())
                    yield break;

                if (currentHeight + line.LineHeight > availableHeight + Size.Epsilon)
                    yield break;

                currentHeight += line.LineHeight;
                isFirstLineOfParagraph = false;
                yield return line;
            }

            TextLine GetNextLine(float indentation)
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
                        AvailableWidth = Math.Max(0, lineBoxWidth - currentWidth),

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
