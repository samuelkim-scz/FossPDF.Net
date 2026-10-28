using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarfBuzzSharp;
using FossPDF.Infrastructure;
using SkiaSharp;
using Buffer = HarfBuzzSharp.Buffer;

namespace FossPDF.Drawing
{
    public class TextShaper
    {
        private readonly DocumentSpecificFontManager _fontManager;
        public const int FontShapingScale = 512;

        private TextStyle TextStyle { get; }

        private Font ShaperFont => _fontManager.ToShaperFont(TextStyle);
        private SKFont SkiaFont => _fontManager.ToFont(TextStyle);

        public TextShaper(TextStyle textStyle, DocumentSpecificFontManager fontManager)
        {
            _fontManager = fontManager;
            TextStyle = textStyle;
        }

        public TextShapingResult Shape(string text)
        {
            using var buffer = new Buffer();

            // UTF-16 input makes HarfBuzz report clusters as UTF-16 character offsets,
            // which line breaking needs in order to inspect the original characters.
            buffer.AddUtf16(text, 0, text.Length);
            buffer.GuessSegmentProperties();

            if (TextStyle.Direction == TextDirection.LeftToRight)
                buffer.Direction = Direction.LeftToRight;

            if (TextStyle.Direction == TextDirection.RightToLeft)
                buffer.Direction = Direction.RightToLeft;

            var features = TextStyle.FontFeatures?.Items
                .Select(x => new Feature(
                    Tag.Parse(x.Name),
                    x.Enabled ? 1u : 0u,
                    0,
                    uint.MaxValue))
                .ToArray() ?? Array.Empty<Feature>();

            ShaperFont.Shape(buffer, features);

            var length = buffer.Length;
            var glyphInfos = buffer.GlyphInfos;
            var glyphPositions = buffer.GlyphPositions;

            var scaleY = SkiaFont.Size / FontShapingScale;
            var scaleX = scaleY * SkiaFont.ScaleX;

            var xOffset = 0f;
            var yOffset = 0f;

            // used for letter spacing calculation
            var lastCluster = glyphInfos.LastOrDefault().Cluster;
            var letterSpacing = (TextStyle.LetterSpacing ?? 0) * (TextStyle.Size ?? 16);
            var wordSpacing = (TextStyle.WordSpacing ?? 0) * (TextStyle.Size ?? 16);

            var glyphs = new ShapedGlyph[length];

            for (var i = 0; i < length; i++)
            {
                var hasExtents = ShaperFont.TryGetGlyphExtents(glyphInfos[i].Codepoint, out var extents);
                float? lBearing = hasExtents ? scaleX * extents.XBearing : null;
                float? rBearing = hasExtents
                    ? scaleX * (glyphPositions[i].XAdvance - (extents.Width + extents.XBearing))
                    : null;

                // letter spacing should be applied between glyph clusters, not between individual glyphs,
                // different cluster id indicates the end of the glyph cluster
                if (lastCluster != glyphInfos[i].Cluster)
                {
                    lastCluster = glyphInfos[i].Cluster;
                    xOffset += letterSpacing;
                }

                // word spacing expands the advance of space glyphs, so it contributes to
                // line breaking and measurement in the same way as the configured spacing.
                // The source character decides, not the glyph id: some fonts map ignorable
                // characters (for example emoji variation selectors) to the space glyph.
                var cluster = (int)glyphInfos[i].Cluster;
                var isSpace = cluster >= 0 && cluster < text.Length && text[cluster] == ' ';
                var advance = glyphPositions[i].XAdvance * scaleX + (isSpace ? wordSpacing : 0);

                glyphs[i] = new ShapedGlyph
                {
                    Codepoint = (ushort)glyphInfos[i].Codepoint,
                    Cluster = (int)glyphInfos[i].Cluster,
                    Position = new SKPoint(xOffset + glyphPositions[i].XOffset * scaleX,
                        yOffset - glyphPositions[i].YOffset * scaleY),
                    Width = advance,
                    LBearing = lBearing,
                    RBearing = rBearing,
                    IsSpace = isSpace
                };
                xOffset += advance;
                yOffset += glyphPositions[i].YAdvance * scaleY;
            }

            if (glyphs.Length > 0)
            {
                // normalise all the glyphs so they start at no further to the left than X=0
                var minX = glyphs.Min(g => g.Position.X);
                for (var i = 0; i < glyphs.Length; i++)
                {
                    glyphs[i].Position.X -= minX;
                }
            }

            return new TextShapingResult(buffer.Direction, glyphs, scaleX, scaleY, _fontManager);
        }

    }

    public struct ShapedGlyph
    {
        public ushort Codepoint;

        /// <summary>
        /// UTF-16 offset of the first character that produced this glyph. Glyphs that belong to
        /// the same grapheme cluster share the cluster value.
        /// </summary>
        public int Cluster;

        public SKPoint Position;
        public float Width;
        public float? RBearing { get; set; }
        public float? LBearing { get; set; }
        public bool IsSpace { get; set; }
    }

    public struct DrawTextCommand
    {
        public SKTextBlob SkTextBlob;
    }

    public class TextShapingResult : IEnumerable<ShapedGlyph>
    {
        private Direction Direction { get; }
        private ShapedGlyph[] Glyphs { get; }
        public float ScaleX { get; }
        public float ScaleY { get; }

        public int Length => Glyphs.Length;

        public bool IsRightToLeft => Direction == Direction.RightToLeft;

        private DocumentSpecificFontManager FontManager { get; }

        public ShapedGlyph this[int index] =>
            Direction == Direction.LeftToRight
                ? Glyphs[index]
                : Glyphs[Glyphs.Length - 1 - index];

        public TextShapingResult(Direction direction, ShapedGlyph[] glyphs, float scaleX, float scaleY, DocumentSpecificFontManager fontManager)
        {
            Direction = direction;
            Glyphs = glyphs;
            ScaleX = scaleX;
            ScaleY = scaleY;
            FontManager = fontManager;
        }

        public int BreakText(int startIndex, float maxWidth, bool removeExtents)
        {
            return Direction switch
            {
                Direction.LeftToRight => BreakTextLeftToRight(),
                Direction.RightToLeft => BreakTextRightToLeft(),
                _ => throw new ArgumentOutOfRangeException()
            };

            int BreakTextLeftToRight()
            {
                var index = startIndex;
                maxWidth += Glyphs[startIndex].Position.X;
                var leftAdjust = Glyphs[startIndex].LBearing ?? 0f;

                while (index < Glyphs.Length)
                {
                    var glyph = Glyphs[index];
                    var rightAdjust = glyph.RBearing ?? 0f;
                    var totalAdjust = 0f;
                    if (removeExtents)
                    {
                        totalAdjust += leftAdjust + rightAdjust;
                    }
                    if (glyph.Position.X + glyph.Width >
                        maxWidth + Size.Epsilon + totalAdjust)
                    {
                        break;
                    }

                    index++;
                }

                return index - 1;
            }

            int BreakTextRightToLeft()
            {
                var index = startIndex;

                var startOffset = this[startIndex].Position.X + this[startIndex].Width;

                while (index < Glyphs.Length)
                {
                    if (startOffset - this[index].Position.X > maxWidth + Size.Epsilon)
                        break;

                    index++;
                }

                return index - 1;
            }
        }

        public record struct WidthResult
        {
            public float Width { get; set; }
            public float FirstGlyphBearing { get; set; }
            public float LastGlyphBearing { get; set; }
            public int SpaceCount { get; set; }
        }

        public WidthResult MeasureWidth(int startIndex, int endIndex)
        {
            if (Glyphs.Length == 0)
                return new WidthResult
                {
                    Width = 0,
                    FirstGlyphBearing = 0,
                    LastGlyphBearing = 0,
                    SpaceCount = 0
                };

            var start = this[startIndex];
            var end = this[endIndex];



            var sum = 0f;
            var spaceCount = 0;
            for (var i = startIndex; i <= endIndex; i++)
            {
                var glyph = this[i];
                sum += glyph.Width;

                if (glyph.IsSpace)
                    spaceCount++;
            }

            return new WidthResult
            {
                Width = sum,
                FirstGlyphBearing = start.LBearing ?? 0,
                LastGlyphBearing = end.RBearing ?? 0,
                SpaceCount = spaceCount
            };
        }

        public DrawTextCommand? PositionText(int startIndex, int endIndex, TextStyle textStyle, float additionalWordSpacing = 0)
        {
            if (Glyphs.Length == 0)
                return null;

            if (startIndex > endIndex)
                return null;

            // Renormalize so that the leftmost rendered glyph starts at the blob origin.
            // The indexer presents glyphs in logical order; for right-to-left content the
            // underlying visual array is reversed and cannot be sliced with logical indices.
            var minX = this[endIndex].Position.X;

            for (var index = startIndex; index < endIndex; index++)
                minX = Math.Min(minX, this[index].Position.X);


            using var skTextBlobBuilder = new SKTextBlobBuilder();

            var positionedRunBuffer =
                skTextBlobBuilder.AllocatePositionedRun(FontManager.ToFont(textStyle), endIndex - startIndex + 1);
            var glyphSpan = positionedRunBuffer.Glyphs;
            var positionSpan = positionedRunBuffer.Positions;

            // Additional word spacing is applied to the word that follows the space glyph
            // in logical order. Glyphs are stored in visual left-to-right order, so for
            // right-to-left content the spacing shifts the glyphs that precede the space
            // in logical order (those to the right of it).
            var additionalSpacing = 0f;

            if (Direction == Direction.LeftToRight)
            {
                for (var sourceIndex = startIndex; sourceIndex <= endIndex; sourceIndex++)
                {
                    var glyph = this[sourceIndex];
                    var runIndex = sourceIndex - startIndex;

                    glyphSpan[runIndex] = glyph.Codepoint;
                    positionSpan[runIndex] = new SKPoint(
                        glyph.Position.X - minX + additionalSpacing,
                        glyph.Position.Y);

                    if (glyph.IsSpace)
                        additionalSpacing += additionalWordSpacing;
                }
            }
            else
            {
                for (var sourceIndex = endIndex; sourceIndex >= startIndex; sourceIndex--)
                {
                    var glyph = this[sourceIndex];
                    var runIndex = sourceIndex - startIndex;

                    glyphSpan[runIndex] = glyph.Codepoint;
                    positionSpan[runIndex] = new SKPoint(
                        glyph.Position.X - minX + additionalSpacing,
                        glyph.Position.Y);

                    if (glyph.IsSpace)
                        additionalSpacing += additionalWordSpacing;
                }
            }

            var cmd = new DrawTextCommand
            {
                SkTextBlob = skTextBlobBuilder.Build() ?? throw new InvalidOperationException("Could not build the shaped text blob."),
            };


            return cmd;
        }

        IEnumerator<ShapedGlyph> IEnumerable<ShapedGlyph>.GetEnumerator()
        {
            var startingIndex = 0;
            var endingIndex = Glyphs.Length - 1;
            for (int i = 0; i < endingIndex; i++)
            {
                yield return this[startingIndex + i];
            }
        }

        public IEnumerator GetEnumerator()
        {
            // use the generic version
            return ((IEnumerable<ShapedGlyph>)this).GetEnumerator();
        }
    }
}
