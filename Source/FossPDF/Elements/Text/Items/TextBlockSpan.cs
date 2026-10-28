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

            // CJK text has no spaces. Break at the last position allowed by the Unicode line
            // breaking rules, so closing punctuation is not pushed to the start of the next line
            // and opening punctuation does not end the current one.
            if (!shapingResult.IsRightToLeft &&
                TryFindCjkBreakOpportunity(shapingResult, startIndex, endIndex, out var cjkBreakIndex))
            {
                var nextIndex = cjkBreakIndex + 1;

                // consume the space after the break, as space based wrapping does
                if (nextIndex < shapingResult.Length && shapingResult[nextIndex].IsSpace)
                    nextIndex++;

                return (cjkBreakIndex, nextIndex);
            }

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
            if (!isFirstElementInLine)
                return null;

            // safe mode must not cut a grapheme cluster in half: keep combining marks and emoji
            // sequences attached to their base character. If the remaining glyphs of the cluster
            // do not fit, the whole cluster is still placed on the line.
            while (endIndex + 1 < shapingResult.Length &&
                   shapingResult[endIndex + 1].Cluster == shapingResult[endIndex].Cluster)
            {
                endIndex++;
            }

            return (endIndex, endIndex + 1);
        }

        private bool TryFindCjkBreakOpportunity(
            TextShapingResult shapingResult,
            int startIndex,
            int endIndex,
            out int breakIndex)
        {
            breakIndex = -1;

            for (var index = endIndex; index >= startIndex; index--)
            {
                if (index + 1 >= shapingResult.Length)
                    break;

                // glyphs that belong to the same grapheme cluster must not be separated
                if (shapingResult[index].Cluster == shapingResult[index + 1].Cluster)
                    continue;

                var nextCharacterIndex = shapingResult[index + 1].Cluster;

                if (nextCharacterIndex <= 0 || nextCharacterIndex >= Text.Length)
                    continue;

                if (!TryGetCodePointBefore(Text, nextCharacterIndex, out var before))
                    continue;

                var after = char.ConvertToUtf32(Text, nextCharacterIndex);

                if (!CjkLineBreak.CanBreakBetween(before, after))
                    continue;

                breakIndex = index;
                return true;
            }

            return false;
        }

        private static bool TryGetCodePointBefore(string text, int characterIndex, out int codePoint)
        {
            codePoint = 0;

            if (characterIndex <= 0 || characterIndex > text.Length)
                return false;

            var previousIndex = characterIndex - 1;

            if (previousIndex > 0 &&
                char.IsLowSurrogate(text[previousIndex]) &&
                char.IsHighSurrogate(text[previousIndex - 1]))
            {
                previousIndex--;
            }

            codePoint = char.ConvertToUtf32(text, previousIndex);
            return true;
        }

        /// <summary>
        /// Maps a glyph index (the coordinate used by <see cref="TextMeasurementResult"/> and
        /// drawing requests) to the index of the first UTF-16 character of its cluster.
        /// </summary>
        internal int GetCharacterIndex(int glyphIndex)
        {
            if (TextShapingResult == null || glyphIndex < 0 || glyphIndex >= TextShapingResult.Length)
                return glyphIndex;

            return TextShapingResult[glyphIndex].Cluster;
        }

        internal int GetGlyphCount()
        {
            return TextShapingResult?.Length ?? Text.Length;
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

            if (Style.HasUnderline ?? false)
            {
                var underlineOffset = Style.FontPosition == FontPosition.Superscript ? 0 : glyphOffsetY;
                DrawDecoration(fontMetrics.UnderlinePosition + underlineOffset, fontMetrics.UnderlineThickness);
            }

            if (Style.HasStrikethrough ?? false)
            {
                var strikeoutThickness = fontMetrics.StrikeoutThickness;
                strikeoutThickness *= (Style.FontPosition ?? FontPosition.Normal) == FontPosition.Normal ? 1f : 0.625f;

                DrawDecoration(fontMetrics.StrikeoutPosition + glyphOffsetY, strikeoutThickness);
            }

            if (Style.HasOverline ?? false)
                DrawDecoration(fontMetrics.Ascent + glyphOffsetY, fontMetrics.UnderlineThickness);

            void DrawDecoration(float top, float baseThickness)
            {
                var width = request.TextSize.Width;
                if (width <= 0)
                    return;

                var thickness = Math.Max(0.1f, baseThickness * (Style.DecorationThickness ?? 1f));
                var color = Style.DecorationColor ?? Style.Color ?? Colors.Black;
                var decorationStyle = Style.DecorationStyle ?? TextDecorationStyle.Solid;

                void Draw(float x, float y, float w, float h)
                {
                    if (w > 0 && h > 0)
                        request.Canvas.DrawRectangle(new Position(x, y), new Size(w, h), color);
                }

                switch (decorationStyle)
                {
                    case TextDecorationStyle.Solid:
                        Draw(0, top, width, thickness);
                        break;

                    case TextDecorationStyle.Double:
                    {
                        var lineThickness = Math.Max(0.1f, thickness * 0.5f);
                        Draw(0, top, width, lineThickness);
                        Draw(0, top + lineThickness * 2, width, lineThickness);
                        break;
                    }

                    case TextDecorationStyle.Dotted:
                    {
                        var step = Math.Max(thickness * 2, 0.2f);
                        for (var x = 0f; x < width; x += step)
                            Draw(x, top, Math.Min(thickness, width - x), thickness);
                        break;
                    }

                    case TextDecorationStyle.Dashed:
                    {
                        var dash = Math.Max(thickness * 4, 0.2f);
                        var step = dash + Math.Max(thickness * 2, 0.2f);
                        for (var x = 0f; x < width; x += step)
                            Draw(x, top, Math.Min(dash, width - x), thickness);
                        break;
                    }

                    case TextDecorationStyle.Wavy:
                    {
                        var step = Math.Max(thickness, 0.2f);
                        var period = Math.Max(thickness * 6, 1f);
                        var amplitude = Math.Max(thickness, 0.5f);
                        for (var x = 0f; x < width; x += step)
                        {
                            var y = top + (float) Math.Sin(x / period * Math.PI * 2) * amplitude;
                            Draw(x, y, Math.Min(step * 1.1f, width - x), Math.Max(thickness * 0.6f, 0.1f));
                        }
                        break;
                    }
                }
            }

            float GetGlyphOffset()
            {
                var fontSize = Style.Size ?? 12f;

                var offsetFactor = (Style.FontPosition ?? FontPosition.Normal) switch
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
