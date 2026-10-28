using System;
using System.Collections.Generic;
using FossPDF.Infrastructure;

namespace FossPDF.Drawing
{
    /// <summary>
    /// A rendered document captured for the tagged-PDF backend. Every page consists of ordered paint
    /// batches; each batch is a one-page PDF fragment containing exactly the content owned by one
    /// semantic node (or by the page itself when <see cref="TaggedPaintBatch.NodeId"/> is
    /// <see langword="null"/>, which means artifact).
    ///
    /// The fragments are assembled and tagged by the optional
    /// <c>FossPDF.PdfOperations</c> package. The model exposes PDF bytes instead of a document object
    /// so the core package never references a PDF library.
    /// </summary>
    public sealed class TaggedDocumentContent
    {
        internal TaggedDocumentContent(
            DocumentMetadata metadata,
            IReadOnlyList<TaggedDocumentPage> pages,
            TaggedStructureNode root,
            int pageCount,
            PdfAConformance? pdfAConformance)
        {
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Pages = pages ?? throw new ArgumentNullException(nameof(pages));
            Root = root ?? throw new ArgumentNullException(nameof(root));
            PageCount = pageCount;
            PdfAConformance = pdfAConformance;
        }

        /// <summary>A snapshot of the document metadata taken before rendering.</summary>
        public DocumentMetadata Metadata { get; }

        /// <summary>
        /// The PDF/A profile requested through <see cref="DocumentSettings.PdfAConformance"/>, or
        /// <see langword="null"/> when the settings did not decide. The optional
        /// <c>FossPDF.PdfOperations</c> package interprets this value when it writes the document.
        /// </summary>
        public PdfAConformance? PdfAConformance { get; }

        /// <summary>Pages in document order, each with its paint batches in paint order.</summary>
        public IReadOnlyList<TaggedDocumentPage> Pages { get; }

        /// <summary>The logical structure root (<c>/Document</c>).</summary>
        public TaggedStructureNode Root { get; }

        /// <summary>The number of rendered pages.</summary>
        public int PageCount { get; }
    }

    /// <summary>A rendered page and its ordered, independently addressable paint batches.</summary>
    public sealed class TaggedDocumentPage
    {
        internal TaggedDocumentPage(float width, float height, IReadOnlyList<TaggedPaintBatch> batches)
        {
            Width = width;
            Height = height;
            Batches = batches ?? throw new ArgumentNullException(nameof(batches));
        }

        public float Width { get; }
        public float Height { get; }

        /// <summary>Batches in the order they must be painted, including artifact batches.</summary>
        public IReadOnlyList<TaggedPaintBatch> Batches { get; }
    }

    /// <summary>
    /// One independently addressable piece of page content. The fragment is a complete one-page PDF
    /// rendered at the destination page size with the content at its final position.
    /// </summary>
    public sealed class TaggedPaintBatch
    {
        internal TaggedPaintBatch(byte[] fragment, string? nodeId, int order)
        {
            Fragment = fragment ?? throw new ArgumentNullException(nameof(fragment));
            NodeId = nodeId;
            Order = order;
        }

        /// <summary>PDF bytes of the one-page fragment.</summary>
        public byte[] Fragment { get; }

        /// <summary>The owning structure node, or <see langword="null"/> for decorative content.</summary>
        public string? NodeId { get; }

        /// <summary>Global paint order; lower values are painted first.</summary>
        public int Order { get; }
    }

    /// <summary>A node of the logical structure tree. Children are in reading order.</summary>
    public sealed class TaggedStructureNode
    {
        internal TaggedStructureNode(
            string id,
            string role,
            string? alternativeText,
            string? language,
            int order,
            IReadOnlyList<TaggedStructureNode> children)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Role = role ?? throw new ArgumentNullException(nameof(role));
            AlternativeText = alternativeText;
            Language = language;
            Order = order;
            Children = children ?? throw new ArgumentNullException(nameof(children));
        }

        public string Id { get; }

        /// <summary>The PDF standard structure type, without the leading slash (for example <c>H1</c>).</summary>
        public string Role { get; }

        /// <summary>Alternative text, when the semantic element declares it.</summary>
        public string? AlternativeText { get; }

        /// <summary>A language override such as <c>es-ES</c>, when declared.</summary>
        public string? Language { get; }

        /// <summary>
        /// Earliest paint order of this node's content or of its descendants. Used to interleave a
        /// parent's own content with its children in reading order.
        /// </summary>
        public int Order { get; }

        public IReadOnlyList<TaggedStructureNode> Children { get; }
    }
}
