using System;
using System.Collections.Generic;
using System.Linq;
using FossPDF.Drawing;
using FossPDF.Elements;
using FossPDF.Elements.Text;
using FossPDF.Elements.Text.Items;
using FossPDF.Infrastructure;
using static System.String;

namespace FossPDF.Fluent
{
    public class TextSpanDescriptor
    {
        internal TextStyle TextStyle = TextStyle.Default;
        internal Action<TextStyle> AssignTextStyle { get; }

        internal TextSpanDescriptor(Action<TextStyle> assignTextStyle)
        {
            AssignTextStyle = assignTextStyle;
        }

        internal void MutateTextStyle(Func<TextStyle, TextStyle> handler)
        {
            TextStyle = handler(TextStyle);
            AssignTextStyle(TextStyle);
        }
    }

    public delegate string PageNumberFormatter(int? pageNumber);

    public class TextPageNumberDescriptor : TextSpanDescriptor
    {
        internal Action<PageNumberFormatter> AssignFormatFunction { get; }

        internal TextPageNumberDescriptor(Action<TextStyle> assignTextStyle, Action<PageNumberFormatter> assignFormatFunction) : base(assignTextStyle)
        {
            AssignFormatFunction = assignFormatFunction;
            AssignFormatFunction(x => x?.ToString() ?? string.Empty);
        }

        public TextPageNumberDescriptor Format(PageNumberFormatter formatter)
        {
            AssignFormatFunction(formatter);
            return this;
        }
    }

    public class TextDescriptor
    {
        public const string DefaultLineClampEllipsis = "…";

        private ICollection<TextBlock> TextBlocks { get; } = new List<TextBlock>();
        private TextStyle? DefaultStyle { get; set; }
        internal TextHorizontalAlignment? Alignment { get; set; }
        private float FirstLineIndentation { get; set; } = 0f;
        private float Spacing { get; set; } = 0f;
        private int? LineClampValue { get; set; }
        private string LineClampEllipsisValue { get; set; } = DefaultLineClampEllipsis;

        public void DefaultTextStyle(TextStyle style)
        {
            DefaultStyle = style;
        }

        public void DefaultTextStyle(Func<TextStyle, TextStyle> style)
        {
            DefaultStyle = style(TextStyle.Default);
        }

        public void AlignLeft()
        {
            Alignment = TextHorizontalAlignment.Left;
        }

        public void AlignCenter()
        {
            Alignment = TextHorizontalAlignment.Center;
        }

        public void AlignRight()
        {
            Alignment = TextHorizontalAlignment.Right;
        }

        /// <summary>
        /// Aligns text to the logical start edge: the left edge for left-to-right content
        /// and the right edge for right-to-left content.
        /// </summary>
        public void AlignStart()
        {
            Alignment = TextHorizontalAlignment.Start;
        }

        /// <summary>
        /// Aligns text to the logical end edge: the right edge for left-to-right content
        /// and the left edge for right-to-left content.
        /// </summary>
        public void AlignEnd()
        {
            Alignment = TextHorizontalAlignment.End;
        }

        /// <summary>
        /// Stretches every line except the last one in each paragraph to the full available
        /// width by expanding the spaces between words. Glyphs themselves are not stretched.
        /// </summary>
        public void Justify()
        {
            Alignment = TextHorizontalAlignment.Justify;
        }

        /// <summary>
        /// Indents the first line of every paragraph. The indentation is applied at the
        /// logical start edge and does not apply to lines that continue a paragraph.
        /// </summary>
        public void ParagraphFirstLineIndentation(float value, Unit unit = Unit.Point)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Indentation must be a finite number.");

            var points = value.ToPoints(unit);

            if (points < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Indentation cannot be negative.");

            FirstLineIndentation = points;
        }

        public void ParagraphSpacing(float value, Unit unit = Unit.Point)
        {
            Spacing = value.ToPoints(unit);
        }

        /// <summary>
        /// Limits the paragraph to the given number of visible lines. When the content is truncated,
        /// the ellipsis is appended to the last visible line. A null ellipsis uses the default "…";
        /// an empty string renders no ellipsis.
        /// </summary>
        public void ClampLines(int maxLines, string ellipsis = DefaultLineClampEllipsis)
        {
            if (maxLines <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxLines), "The maximum line count must be greater than zero.");

            LineClampValue = maxLines;
            LineClampEllipsisValue = ellipsis ?? DefaultLineClampEllipsis;

            foreach (var textBlock in TextBlocks)
            {
                textBlock.LineClamp = maxLines;
                textBlock.LineClampEllipsis = LineClampEllipsisValue;
            }
        }

        private void AddItemToLastTextBlock(ITextBlockItem item)
        {
            if (!TextBlocks.Any())
                TextBlocks.Add(new TextBlock());

            var lastTextBlock = TextBlocks.Last();

            if (LineClampValue.HasValue)
            {
                lastTextBlock.LineClamp = LineClampValue;
                lastTextBlock.LineClampEllipsis = LineClampEllipsisValue;
            }

            // TextBlock with only one Span with empty text is a special case.
            // It represents an empty line with a given text style (e.g. text height).
            // When more content is put to text block, the first items should be ignored (removed in this case).
            // This change fixes inconsistent line height problem.
            if (lastTextBlock.Items.Count == 1 && lastTextBlock.Items[0] is TextBlockSpan { Text: "" })
            {
                lastTextBlock.Items[0] = item;
                return;
            }

            lastTextBlock.Items.Add(item);
        }

        [Obsolete("This element has been renamed since version 2022.3. Please use the overload that returns a TextSpanDescriptor object which allows to specify text style.")]
        public void Span(string? text, TextStyle style)
        {
            Span(text).Style(style);
        }

        public TextSpanDescriptor Span(string? text)
        {
            if (text == null)
                return new TextSpanDescriptor(_ => { });

            var items = text
                .Replace("\r", string.Empty)
                .Split(new[] { '\n' }, StringSplitOptions.None)
                .Select(x => new TextBlockSpan
                {
                    Text = x
                })
                .ToList();

            AddItemToLastTextBlock(items.First());

            items
                .Skip(1)
                .Select(x => new TextBlock
                {
                    Items = new List<ITextBlockItem> { x }
                })
                .ToList()
                .ForEach(TextBlocks.Add);

            return new TextSpanDescriptor(x => items.ForEach(y => y.Style = x));
        }

        public TextSpanDescriptor Line(string? text)
        {
            text ??= string.Empty;
            return Span(text + Environment.NewLine);
        }

        public TextSpanDescriptor EmptyLine()
        {
            return Span(Environment.NewLine);
        }

        private TextPageNumberDescriptor PageNumber(Func<IPageContext, int?> pageNumber)
        {
            var textBlockItem = new TextBlockPageNumber();
            AddItemToLastTextBlock(textBlockItem);

            return new TextPageNumberDescriptor(x => textBlockItem.Style = x, x => textBlockItem.Source = context => x(pageNumber(context)));
        }

        public TextPageNumberDescriptor CurrentPageNumber()
        {
            return PageNumber(x => x.CurrentPage);
        }

        public TextPageNumberDescriptor TotalPages()
        {
            return PageNumber(x => x.GetLocation(PageContext.DocumentLocation)?.Length);
        }

        [Obsolete("This element has been renamed since version 2022.3. Please use the BeginPageNumberOfSection method.")]
        public void PageNumberOfLocation(string locationName, TextStyle? style = null)
        {
            BeginPageNumberOfSection(locationName).Style(style);
        }

        public TextPageNumberDescriptor BeginPageNumberOfSection(string locationName)
        {
            return PageNumber(x => x.GetLocation(locationName)?.PageStart);
        }

        public TextPageNumberDescriptor EndPageNumberOfSection(string locationName)
        {
            return PageNumber(x => x.GetLocation(locationName)?.PageEnd);
        }

        public TextPageNumberDescriptor PageNumberWithinSection(string locationName)
        {
            return PageNumber(x => x.CurrentPage + 1 - x.GetLocation(locationName)?.PageStart);
        }

        public TextPageNumberDescriptor TotalPagesWithinSection(string locationName)
        {
            return PageNumber(x => x.GetLocation(locationName)?.Length);
        }

        public TextSpanDescriptor SectionLink(string? text, string sectionName)
        {
            if (IsNullOrEmpty(sectionName))
                throw new ArgumentException("Section name cannot be null or empty", nameof(sectionName));

            if (IsNullOrEmpty(text))
                return new TextSpanDescriptor(_ => { });

            var textBlockItem = new TextBlockSectionLink
            {
                Text = text,
                SectionName = sectionName
            };

            AddItemToLastTextBlock(textBlockItem);
            return new TextSpanDescriptor(x => textBlockItem.Style = x);
        }

        [Obsolete("This element has been renamed since version 2022.3. Please use the SectionLink method.")]
        public void InternalLocation(string? text, string locationName, TextStyle? style = null)
        {
            SectionLink(text, locationName).Style(style);
        }

        public TextSpanDescriptor Hyperlink(string? text, string url)
        {
            if (IsNullOrEmpty(url))
                throw new ArgumentException("Url cannot be null or empty", nameof(url));

            if (IsNullOrEmpty(text))
                return new TextSpanDescriptor(_ => { });

            var textBlockItem = new TextBlockHyperlink
            {
                Text = text,
                Url = url
            };

            AddItemToLastTextBlock(textBlockItem);
            return new TextSpanDescriptor(x => textBlockItem.Style = x);
        }

        [Obsolete("This element has been renamed since version 2022.3. Please use the Hyperlink method.")]
        public void ExternalLocation(string? text, string url, TextStyle? style = null)
        {
            Hyperlink(text, url).Style(style);
        }

        public IContainer Element()
        {
            var container = new Container();

            AddItemToLastTextBlock(new TextBlockElement
            {
                Element = container
            });

            return container.AlignBottom().MinimalBox();
        }

        internal void Compose(IContainer container)
        {
            TextBlocks.ToList().ForEach(x =>
            {
                x.Alignment ??= Alignment;
                x.FirstLineIndentation = FirstLineIndentation;
            });

            if (DefaultStyle != null)
                container = container.DefaultTextStyle(DefaultStyle);

            if (TextBlocks.Count == 1)
            {
                container.Element(TextBlocks.First());
                return;
            }

            container.Column(column =>
            {
                column.Spacing(Spacing);

                foreach (var textBlock in TextBlocks)
                    column.Item().Element(textBlock);
            });
        }
    }

    public static class TextExtensions
    {
        public static void Text(this IContainer element, Action<TextDescriptor> content)
        {
            var descriptor = new TextDescriptor();

            if (element is Alignment alignment)
            {
                descriptor.Alignment = alignment.Horizontal switch
                {
                    HorizontalAlignment.Left => TextHorizontalAlignment.Left,
                    HorizontalAlignment.Center => TextHorizontalAlignment.Center,
                    HorizontalAlignment.Right => TextHorizontalAlignment.Right,
                    _ => null
                };
            }

            content?.Invoke(descriptor);
            descriptor.Compose(element);
        }

        [Obsolete("This method has been deprecated since version 2022.3. Please use the overload that returns a TextSpanDescriptor object which allows to specify text style.")]
        public static void Text(this IContainer element, object? text, TextStyle style)
        {
            element.Text(text).Style(style);
        }

        [Obsolete("This method has been deprecated since version 2022.12. Please use an overload where the text parameter is passed explicitly as a string.")]
        public static TextSpanDescriptor Text(this IContainer element, object? text)
        {
            return element.Text(text?.ToString());
        }

        public static TextSpanDescriptor Text(this IContainer element, string? text)
        {
            var descriptor = (TextSpanDescriptor) null!;
            element.Text(x => descriptor = x.Span(text));
            return descriptor;
        }
    }
}
