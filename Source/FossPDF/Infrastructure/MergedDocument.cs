using System;
using System.Collections.Generic;
using System.Linq;
using FossPDF.Drawing;

namespace FossPDF.Infrastructure
{
    internal enum MergedDocumentPageNumberStrategy
    {
        Original,
        Continuous
    }

    /// <summary>
    /// Combines multiple generated documents into one document. The merged document owns the output
    /// metadata and settings; the source documents keep their own composition and page layout.
    /// </summary>
    public sealed class MergedDocument : IDocument, IDocumentSettingsProvider
    {
        internal IReadOnlyList<IDocument> Documents { get; }
        internal MergedDocumentPageNumberStrategy PageNumberStrategy { get; private set; } =
            MergedDocumentPageNumberStrategy.Original;

        internal DocumentMetadata Metadata { get; private set; } = DocumentMetadata.Default;
        internal DocumentSettings Settings { get; private set; } = DocumentSettings.Default;

        internal MergedDocument(IEnumerable<IDocument> documents)
        {
            if (documents == null)
                throw new ArgumentNullException(nameof(documents));

            var materialized = documents.ToList();

            if (materialized.Count == 0)
                throw new ArgumentException("At least one document is required.", nameof(documents));

            if (materialized.Any(document => document == null))
                throw new ArgumentException("The merged document list cannot contain null entries.", nameof(documents));

            Documents = materialized;
        }

        /// <summary>
        /// Gives every source document its own page numbers. A two-page document followed by a
        /// three-page document produces the page-number sequence 1, 2, 1, 2, 3. This is the default.
        /// </summary>
        public MergedDocument UseOriginalPageNumbers()
        {
            PageNumberStrategy = MergedDocumentPageNumberStrategy.Original;
            return this;
        }

        /// <summary>
        /// Numbers all pages consecutively across the merged document. A two-page document followed
        /// by a three-page document produces the page-number sequence 1, 2, 3, 4, 5.
        /// </summary>
        public MergedDocument UseContinuousPageNumbers()
        {
            PageNumberStrategy = MergedDocumentPageNumberStrategy.Continuous;
            return this;
        }

        /// <summary>
        /// Sets the metadata used for the merged output. Source document metadata is not inherited.
        /// </summary>
        public MergedDocument WithMetadata(DocumentMetadata metadata)
        {
            Metadata = metadata ?? Metadata;
            return this;
        }

        /// <summary>
        /// Sets the settings used for the merged output. Source document settings are not inherited.
        /// </summary>
        public MergedDocument WithSettings(DocumentSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            return this;
        }

        public DocumentMetadata GetMetadata() => Metadata;

        DocumentSettings? IDocumentSettingsProvider.GetSettings() => Settings;

        /// <summary>
        /// Composes every source into the supplied container. Generation uses the merged rendering
        /// path so that the configured page-numbering strategy is preserved; this member exists for
        /// consumers that render IDocument instances directly.
        /// </summary>
        public void Compose(IDocumentContainer container)
        {
            if (container == null)
                throw new ArgumentNullException(nameof(container));

            foreach (var document in Documents)
                document.Compose(container);
        }
    }
}
