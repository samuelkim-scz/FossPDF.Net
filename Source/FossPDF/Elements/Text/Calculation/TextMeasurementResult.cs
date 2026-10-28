using System;
using FossPDF.Drawing;
using FossPDF.Infrastructure;

namespace FossPDF.Elements.Text.Calculation
{
    internal class TextMeasurementResult
    {
        public float Width { get; set; }
        public float FirstGlyphBearing { get; set; }
        public float LastGlyphBearing { get; set; }
        public float Height => Math.Abs(Descent) + Math.Abs(Ascent);

        public float Ascent { get; set; }
        public float Descent { get; set; }

        public float LineHeight { get; set; }

        /// <summary>
        /// Number of space glyphs rendered by this measurement. Used to distribute
        /// justification across the line.
        /// </summary>
        public int SpaceCount { get; set; }

        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public int NextIndex { get; set; }
        public int TotalIndex { get; set; }

        public bool IsLast => EndIndex == TotalIndex;

        /// <summary>
        /// Set for an embedded element; null for a text span. The line uses this value to place
        /// the element relative to the surrounding text metrics.
        /// </summary>
        public TextInjectedElementAlignment? InjectedElementAlignment { get; set; }
    }
}
