using System;
using System.Collections.Generic;
using System.Linq;
using FossPDF.Elements.Text.Items;

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
    }
}
