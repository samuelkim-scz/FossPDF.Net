using System;
using System.Collections.Generic;
using System.Diagnostics;
using FossPDF.Drawing;
using FossPDF.Elements.Text.Calculation;
using FossPDF.Helpers;
using FossPDF.Infrastructure;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Size = FossPDF.Infrastructure.Size;

namespace FossPDF.Elements.Text.Items
{
    internal class TextBlockSpan : ITextBlockItem
    {
        public string Text { get; set; } = string.Empty;
        public TextStyle Style { get; set; } = TextStyle.Default;
        private TextShapingResult? TextShapingResult { get; set; }

        private Dictionary<MeasurementCacheKey, TextMeasurementResult?> MeasureCache = new ();
        protected virtual bool EnableTextCache => true;

        public void ClearTextShapingResult()
        {
            TextShapingResult = null;
        }

        private record struct MeasurementCacheKey
        {
            public int StartIndex { get; set; }
            public float AvailableWidth { get; set; }

            public bool IsFirstElementInBlock { get; set; }
            public bool IsFirstElementInLine { get; set; }
        }

        public virtual TextMeasurementResult? Measure(TextMeasurementRequest request)
        {
            var cacheKey = new MeasurementCacheKey
            {
                StartIndex = request.StartIndex,
                AvailableWidth = request.AvailableWidth,
                IsFirstElementInBlock = request.IsFirstElementInBlock,
                IsFirstElementInLine = request.IsFirstElementInLine
            };

            if (!MeasureCache.ContainsKey(cacheKey))
            {
                MeasureCache[cacheKey] = MeasureWithoutCache(request);
            }

            return MeasureCache[cacheKey];
        }

        internal TextMeasurementResult? MeasureWithoutCache(TextMeasurementRequest request)
        {
            if (!EnableTextCache)
                TextShapingResult = null;

            var textShaper = request.PageContext.FontManager.ToTextShaper(Style);
            TextShapingResult ??= textShaper.Shape(Text);

            var fontMetrics = request.PageContext.FontManager.ToFontMetrics(Style);

            var startIndex = request.StartIndex;

            // if the element is the first one within the line,
            // ignore leading spaces
            if (request is { IsFirstElementInBlock: false, IsFirstElementInLine: true })
            {
                while (startIndex < TextShapingResult.Length && TextShapingResult[startIndex].IsSpace)
                    startIndex++;
            }

            if (TextShapingResult.Length == 0 || startIndex == TextShapingResult.Length)
            {
                return new TextMeasurementResult
                {
                    Width = 0,

                    LineHeight = Style.LineHeight ?? 1,
                    Ascent = fontMetrics.Ascent,
                    Descent = fontMetrics.Descent
                };
            }

            // start breaking text from requested position
            var endIndex = TextShapingResult.BreakText(startIndex, request.AvailableWidth, Style.RemoveExtents == true);

            if (endIndex < startIndex)
                return null;

            // break text only on spaces
            var wrappedText = WrapText(TextShapingResult, startIndex, endIndex, request.IsFirstElementInLine);

            if (wrappedText == null)
                return null;

            // measure final text
            var width = TextShapingResult.MeasureWidth(startIndex, wrappedText.Value.endIndex);

            var result = new TextMeasurementResult
            {
                Width = width.Width,
                FirstGlyphBearing = width.FirstGlyphBearing,
                LastGlyphBearing = width.LastGlyphBearing,
                Ascent = fontMetrics.Ascent,
                Descent = fontMetrics.Descent,

                LineHeight = Style.LineHeight ?? 1,

                SpaceCount = width.SpaceCount,

                StartIndex = startIndex,
                EndIndex = wrappedText.Value.endIndex,
                NextIndex = wrappedText.Value.nextIndex,
                TotalIndex = TextShapingResult.Length - 1
            };

            if (Style.RemoveExtents == true)
            {
                result.Width = width.Width - width.FirstGlyphBearing - width.LastGlyphBearing;
            }

            return result;
        }

        // TODO: consider introducing text wrapping abstraction (basic, english-like, asian-like)
        private (int endIndex, int nextIndex)? WrapText(TextShapingResult shapingResult, int startIndex, int endIndex, bool isFirstElementInLine)
        {
            // textLength - length of the part of the text that fits in available width (creating a line)

            // entire text fits, no need to wrap
            if (endIndex == shapingResult.Length - 1)
                return (endIndex, endIndex);

            // breaking anywhere
            if (Style.WrapAnywhere ?? false)
                return (endIndex, endIndex + 1);

            // current line ends at word, next character is space, perfect place to wrap
            if (!shapingResult[endIndex].IsSpace && shapingResult[endIndex + 1].IsSpace)
                return (endIndex, endIndex + 2);

            // find last space within the available text to wrap
            var lastSpaceIndex = endIndex;

            while (lastSpaceIndex >= startIndex)
            {
                if (shapingResult[lastSpaceIndex].IsSpace)
                    break;

                lastSpaceIndex--;
            }

            // text contains space that can be used to wrap
            if (lastSpaceIndex > 1 && lastSpaceIndex >= startIndex)
                return (lastSpaceIndex - 1, lastSpaceIndex + 1);

            // there is no available space to wrap text
            // if the item is first within the line, perform safe mode and chop the word
            // otherwise, move the item into the next line
            return isFirstElementInLine ? (endIndex, endIndex + 1) : null;
        }

        public List<uint>? GetGlyphCodepoints()
        {
            if (TextShapingResult == null)
            {
                return null;
            }

            var result = new List<uint>();
            for (var i = 0; i < TextShapingResult.Length; i++)
            {
                var glyph = TextShapingResult[i];
                result.Add(glyph.Codepoint);
            }

            return result;
        }

        public virtual void Draw(TextDrawingRequest request)
        {
            var fontMetrics = request.PageContext.FontManager.ToFontMetrics(Style);

            var glyphOffsetY = GetGlyphOffset();

            // if we're here post-subset, we need to re-shape the text because the glyph IDs will have changed
            var textShaper = request.PageContext.FontManager.ToTextShaper(Style);
            TextShapingResult ??= textShaper.Shape(Text);

            var textDrawingCommand = TextShapingResult.PositionText(request.StartIndex, request.EndIndex, Style, request.AdditionalWordSpacing);

            if (Style.BackgroundColor is { } backgroundColor && backgroundColor != Colors.Transparent)
                request.Canvas.DrawRectangle(new Position(0, request.TotalAscent), new Size(request.TextSize.Width, request.TextSize.Height), backgroundColor);

            if (textDrawingCommand.HasValue)
            {
                request.Canvas.DrawText(textDrawingCommand.Value.SkTextBlob, new Position(0, glyphOffsetY), Style, request.PageContext.FontManager);
                textDrawingCommand.Value.SkTextBlob.Dispose();
            }

            // draw underline
            if (Style.HasUnderline ?? false)
            {
                var underlineOffset = Style.FontPosition == FontPosition.Superscript ? 0 : glyphOffsetY;
                DrawLine(fontMetrics.UnderlinePosition + underlineOffset, fontMetrics.UnderlineThickness);
            }

            // draw stroke
            if (Style.HasStrikethrough ?? false)
            {
                var strikeoutThickness = fontMetrics.StrikeoutThickness;
                strikeoutThickness *= Style.FontPosition == FontPosition.Normal ? 1f : 0.625f;

                DrawLine(fontMetrics.StrikeoutPosition + glyphOffsetY, strikeoutThickness);
            }

            void DrawLine(float offset, float thickness)
            {
                request.Canvas.DrawRectangle(new Position(0, offset), new Size(request.TextSize.Width, thickness), Style.Color ?? Colors.Black);
            }

            float GetGlyphOffset()
            {
                var fontSize = Style.Size ?? 12f;

                var offsetFactor = Style.FontPosition switch
                {
                    FontPosition.Normal => 0,
                    FontPosition.Subscript => 0.1f,
                    FontPosition.Superscript => -0.35f,
                    _ => throw new ArgumentOutOfRangeException()
                };

                return fontSize * offsetFactor;
            }
        }
    }
}
