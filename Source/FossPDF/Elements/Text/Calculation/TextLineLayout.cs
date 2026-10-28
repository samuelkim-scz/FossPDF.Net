using System;
using FossPDF.Infrastructure;

namespace FossPDF.Elements.Text.Calculation
{
    /// <summary>
    /// Describes the horizontal area available to a single line of text. The area already
    /// excludes the first line indentation, which is applied at the logical start edge.
    /// </summary>
    internal readonly struct LineBox
    {
        public LineBox(float left, float width)
        {
            Left = left;
            Width = width;
        }

        public float Left { get; }
        public float Width { get; }
        public float Right => Left + Width;
    }

    internal static class TextLineLayout
    {
        public static LineBox GetLineBox(float availableWidth, ContentDirection contentDirection, float indentation)
        {
            // Indentation is applied at the logical start edge: the left edge for
            // left-to-right content and the right edge for right-to-left content.
            if (contentDirection == ContentDirection.LeftToRight)
                return new LineBox(indentation, availableWidth - indentation);

            return new LineBox(0, availableWidth - indentation);
        }

        public static TextHorizontalAlignment ResolveAlignment(
            TextHorizontalAlignment alignment,
            bool isLastLineOfParagraph,
            int spaceCount)
        {
            if (alignment != TextHorizontalAlignment.Justify)
                return alignment;

            // The final line of a paragraph and lines without spaces cannot be justified,
            // so they are aligned to the logical start edge instead.
            if (isLastLineOfParagraph || spaceCount == 0)
                return TextHorizontalAlignment.Start;

            return alignment;
        }

        public static float GetAdditionalWordSpacing(
            TextHorizontalAlignment alignment,
            LineBox box,
            float contentWidth,
            int spaceCount)
        {
            if (alignment != TextHorizontalAlignment.Justify || spaceCount <= 0)
                return 0;

            var additionalSpace = box.Width - contentWidth;

            if (additionalSpace <= 0)
                return 0;

            return additionalSpace / spaceCount;
        }

        /// <summary>
        /// Returns the horizontal position where the line content starts. For left-to-right
        /// content this is the left edge of the first item; for right-to-left content this
        /// is the right edge of the first (logically first) item.
        /// </summary>
        public static float GetContentStart(
            TextHorizontalAlignment alignment,
            ContentDirection contentDirection,
            LineBox box,
            float lineWidth)
        {
            var freeSpace = Math.Max(0, box.Width - lineWidth);

            if (contentDirection == ContentDirection.LeftToRight)
            {
                var offset = alignment switch
                {
                    TextHorizontalAlignment.Left => 0,
                    TextHorizontalAlignment.Start => 0,
                    TextHorizontalAlignment.Justify => 0,
                    TextHorizontalAlignment.Center => freeSpace / 2,
                    TextHorizontalAlignment.Right => freeSpace,
                    TextHorizontalAlignment.End => freeSpace,
                    _ => 0
                };

                return box.Left + offset;
            }
            else
            {
                // Right-to-left content flows from the right edge towards the left edge.
                var offsetFromRight = alignment switch
                {
                    TextHorizontalAlignment.Right => 0,
                    TextHorizontalAlignment.Start => 0,
                    TextHorizontalAlignment.Justify => 0,
                    TextHorizontalAlignment.Center => freeSpace / 2,
                    TextHorizontalAlignment.Left => freeSpace,
                    TextHorizontalAlignment.End => freeSpace,
                    _ => 0
                };

                return box.Right - offsetFromRight;
            }
        }
    }
}
