using System;
using System.Collections.Generic;
using System.Linq;
using FossPDF.Elements.Text.Items;
using FossPDF.Infrastructure;

namespace FossPDF.Elements.Text.Calculation
{
    internal class TextLine
    {
        public ICollection<TextLineElement> Elements { get; private set; } = Array.Empty<TextLineElement>();

        public float TextHeight { get; private set; }
        public float LineHeight { get; private set; }

        public float Ascent { get; private set; }
        public float Descent { get; private set; }

        /// <summary>
        /// Width of the rendered content: the sum of glyph advances, including configured
        /// word spacing, but excluding any justification added while drawing.
        /// </summary>
        public float Width { get; private set; }

        /// <summary>
        /// Width available to this line. It excludes the first line indentation, which is
        /// reported separately in <see cref="Indentation"/>.
        /// </summary>
        public float BoxWidth { get; private set; }

        /// <summary>
        /// Indentation applied at the logical start edge of the line. It is non-zero only
        /// for the first line of a paragraph.
        /// </summary>
        public float Indentation { get; private set; }

        /// <summary>
        /// Number of space glyphs rendered on this line. Used to distribute justification.
        /// </summary>
        public int SpaceCount { get; private set; }

        /// <summary>
        /// True when this line is the final line of the paragraph (and therefore must not
        /// be justified).
        /// </summary>
        public bool IsLastLineOfParagraph { get; private set; }

        public static TextLine From(ICollection<TextLineElement> elements, float indentation, float boxWidth, bool isLastLineOfParagraph)
        {
            if (elements.Count == 0)
            {
                return new TextLine
                {
                    Elements = elements,
                    Indentation = indentation,
                    BoxWidth = boxWidth,
                    IsLastLineOfParagraph = isLastLineOfParagraph
                };
            }

            var textHeight = elements.Max(x => x.Measurement.Height);
            var lineHeight = elements.Max(x => x.Measurement.LineHeight * x.Measurement.Height);

            var textElements = elements
                .Where(x => x.Measurement.InjectedElementAlignment == null)
                .ToList();

            var injectedElements = elements
                .Where(x => x.Measurement.InjectedElementAlignment.HasValue)
                .ToList();

            if (injectedElements.Count == 0)
            {
                return new TextLine
                {
                    Elements = elements,

                    TextHeight = textHeight,
                    LineHeight = lineHeight,

                    Ascent = elements.Min(x => x.Measurement.Ascent) - (lineHeight - textHeight) / 2,
                    Descent = elements.Max(x => x.Measurement.Descent) + (lineHeight - textHeight) / 2,

                    Width = elements.Sum(x => x.Measurement.Width),
                    SpaceCount = elements.Sum(x => x.Measurement.SpaceCount),

                    Indentation = indentation,
                    BoxWidth = boxWidth,
                    IsLastLineOfParagraph = isLastLineOfParagraph
                };
            }

            var textAscent = -textHeight;
            var textDescent = 0f;

            if (textElements.Count > 0)
            {
                var textOnlyHeight = textElements.Max(x => x.Measurement.Height);
                var textOnlyLineHeight = textElements.Max(x => x.Measurement.LineHeight * x.Measurement.Height);

                textAscent = textElements.Min(x => x.Measurement.Ascent) - (textOnlyLineHeight - textOnlyHeight) / 2;
                textDescent = textElements.Max(x => x.Measurement.Descent) + (textOnlyLineHeight - textOnlyHeight) / 2;
            }

            var ascent = textAscent;
            var descent = textDescent;

            foreach (var element in injectedElements)
            {
                var height = element.Measurement.Height;
                var top = GetInjectedElementTop(element.Measurement.InjectedElementAlignment!.Value, height, textAscent, textDescent);

                element.InjectedElementTop = top;
                ascent = Math.Min(ascent, top);
                descent = Math.Max(descent, top + height);
            }

            return new TextLine
            {
                Elements = elements,

                TextHeight = textHeight,
                LineHeight = Math.Max(lineHeight, descent - ascent),

                Ascent = ascent,
                Descent = descent,

                Width = elements.Sum(x => x.Measurement.Width),
                SpaceCount = elements.Sum(x => x.Measurement.SpaceCount),

                Indentation = indentation,
                BoxWidth = boxWidth,
                IsLastLineOfParagraph = isLastLineOfParagraph
            };
        }

        private static float GetInjectedElementTop(
            TextInjectedElementAlignment alignment,
            float height,
            float textAscent,
            float textDescent)
        {
            return alignment switch
            {
                TextInjectedElementAlignment.AboveBaseline => -height,
                TextInjectedElementAlignment.BelowBaseline => 0,
                TextInjectedElementAlignment.Top => textAscent,
                TextInjectedElementAlignment.Bottom => textAscent - height,
                TextInjectedElementAlignment.Middle => (textAscent + textDescent - height) / 2,
                _ => -height
            };
        }
    }
}
