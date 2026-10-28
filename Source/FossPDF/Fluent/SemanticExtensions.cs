using System;
using FossPDF.Elements;
using FossPDF.Infrastructure;

namespace FossPDF.Fluent
{
    /// <summary>
    /// Fluent API for the accessibility structure tree. Semantic wrappers are layout-transparent:
    /// they never change size or position. They only take effect when the document is rendered into
    /// tagged content (see <c>FossPDF.PdfOperations.GenerateTaggedPdf</c>); every other output renders
    /// the wrapped element exactly as before.
    ///
    /// Standard text, links, figures and tables receive automatic tags. Semantic wrappers override
    /// those defaults and add headings, lists, alternative text and language. Use
    /// <see cref="SemanticIgnore"/> to exclude decorative content and its descendants.
    /// </summary>
    public static class SemanticExtensions
    {
        public static IContainer SemanticSection(this IContainer element)
        {
            return Wrap(element, SemanticRoles.Section);
        }

        public static IContainer SemanticArticle(this IContainer element)
        {
            return Wrap(element, SemanticRoles.Article);
        }

        public static IContainer SemanticDivision(this IContainer element)
        {
            return Wrap(element, SemanticRoles.Division);
        }

        public static IContainer SemanticHeading1(this IContainer element)
        {
            return Wrap(element, SemanticRoles.Heading1);
        }

        public static IContainer SemanticHeading2(this IContainer element)
        {
            return Wrap(element, SemanticRoles.Heading2);
        }

        public static IContainer SemanticHeading3(this IContainer element)
        {
            return Wrap(element, SemanticRoles.Heading3);
        }

        public static IContainer SemanticHeading4(this IContainer element)
        {
            return Wrap(element, SemanticRoles.Heading4);
        }

        public static IContainer SemanticHeading5(this IContainer element)
        {
            return Wrap(element, SemanticRoles.Heading5);
        }

        public static IContainer SemanticHeading6(this IContainer element)
        {
            return Wrap(element, SemanticRoles.Heading6);
        }

        public static IContainer SemanticParagraph(this IContainer element)
        {
            return Wrap(element, SemanticRoles.Paragraph);
        }

        public static IContainer SemanticBlockQuotation(this IContainer element)
        {
            return Wrap(element, SemanticRoles.BlockQuotation);
        }

        public static IContainer SemanticList(this IContainer element)
        {
            return Wrap(element, SemanticRoles.List);
        }

        public static IContainer SemanticListItem(this IContainer element)
        {
            return Wrap(element, SemanticRoles.ListItem);
        }

        public static IContainer SemanticListLabel(this IContainer element)
        {
            return Wrap(element, SemanticRoles.ListLabel);
        }

        public static IContainer SemanticListItemBody(this IContainer element)
        {
            return Wrap(element, SemanticRoles.ListItemBody);
        }

        public static IContainer SemanticTable(this IContainer element)
        {
            return Wrap(element, SemanticRoles.Table);
        }

        public static IContainer SemanticHorizontalHeader(this IContainer element)
        {
            return Wrap(element, SemanticRoles.TableHeader, tableHeaderScope: "Column");
        }

        /// <summary>Identifies a table header for its row.</summary>
        public static IContainer SemanticVerticalHeader(this IContainer element)
        {
            return Wrap(element, SemanticRoles.TableHeader, tableHeaderScope: "Row");
        }

        public static IContainer SemanticTableOfContents(this IContainer element)
        {
            return Wrap(element, SemanticRoles.TableOfContents);
        }

        public static IContainer SemanticTableOfContentsItem(this IContainer element)
        {
            return Wrap(element, SemanticRoles.TableOfContentsItem);
        }

        public static IContainer SemanticIndex(this IContainer element)
        {
            return Wrap(element, SemanticRoles.Index);
        }

        /// <summary>
        /// Inline span. The alternative text, when provided, expands an abbreviation or otherwise
        /// replaces the visual text for assistive technology.
        /// </summary>
        public static IContainer SemanticSpan(this IContainer element, string? alternativeText = null)
        {
            return Wrap(element, SemanticRoles.Span, alternativeText);
        }

        public static IContainer SemanticQuote(this IContainer element)
        {
            return Wrap(element, SemanticRoles.Quote);
        }

        public static IContainer SemanticCode(this IContainer element)
        {
            return Wrap(element, SemanticRoles.Code);
        }

        /// <summary>
        /// A link. The description is exposed as the link's alternative text; combine it with
        /// <see cref="ElementExtensions.Hyperlink"/> or <see cref="ElementExtensions.SectionLink"/>.
        /// </summary>
        public static IContainer SemanticLink(this IContainer element, string? description = null)
        {
            return Wrap(element, SemanticRoles.Link, description);
        }

        /// <summary>A figure or image. The alternative text is required by accessibility profiles.</summary>
        public static IContainer SemanticFigure(this IContainer element, string? alternativeText = null)
        {
            return Wrap(element, SemanticRoles.Figure, alternativeText);
        }

        /// <summary>An alias for <see cref="SemanticFigure"/>.</summary>
        public static IContainer SemanticImage(this IContainer element, string? alternativeText = null)
        {
            return Wrap(element, SemanticRoles.Figure, alternativeText);
        }

        public static IContainer SemanticFormula(this IContainer element, string? alternativeText = null)
        {
            return Wrap(element, SemanticRoles.Formula, alternativeText);
        }

        public static IContainer SemanticCaption(this IContainer element)
        {
            return Wrap(element, SemanticRoles.Caption);
        }

        /// <summary>
        /// Overrides the natural language of the wrapped content. The tag is written on the structure
        /// element and applies to its descendants until another language scope changes it.
        /// </summary>
        public static IContainer SemanticLanguage(this IContainer element, string language)
        {
            if (string.IsNullOrWhiteSpace(language))
                throw new ArgumentException("The document language cannot be null or whitespace.", nameof(language));

            return Wrap(element, SemanticRoles.Span, language: language);
        }

        /// <summary>
        /// Marks the wrapped content and all descendants as decorative. The content stays visible but
        /// is excluded from the structure tree.
        /// </summary>
        public static IContainer SemanticIgnore(this IContainer element)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));

            return element.Element(new SemanticIgnoreElement());
        }

        private static IContainer Wrap(
            IContainer element,
            string role,
            string? alternativeText = null,
            string? language = null,
            string? tableHeaderScope = null)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));

            return element.Element(new SemanticElement(role, alternativeText, language, tableHeaderScope));
        }
    }

    internal static class SemanticRoles
    {
        public const string Document = "Document";
        public const string Section = "Sect";
        public const string Article = "Art";
        public const string Division = "Div";
        public const string Heading1 = "H1";
        public const string Heading2 = "H2";
        public const string Heading3 = "H3";
        public const string Heading4 = "H4";
        public const string Heading5 = "H5";
        public const string Heading6 = "H6";
        public const string Paragraph = "P";
        public const string BlockQuotation = "BlockQuote";
        public const string List = "L";
        public const string ListItem = "LI";
        public const string ListLabel = "Lbl";
        public const string ListItemBody = "LBody";
        public const string Table = "Table";
        public const string TableHeader = "TH";
        public const string TableOfContents = "TOC";
        public const string TableOfContentsItem = "TOCI";
        public const string Index = "Index";
        public const string Span = "Span";
        public const string Quote = "Quote";
        public const string Code = "Code";
        public const string Link = "Link";
        public const string Figure = "Figure";
        public const string Formula = "Formula";
        public const string Caption = "Caption";
    }
}
