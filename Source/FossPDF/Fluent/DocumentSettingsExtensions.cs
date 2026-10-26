using System;
using FossPDF.Drawing;
using FossPDF.Infrastructure;

namespace FossPDF.Fluent
{
    public static class DocumentSettingsExtensions
    {
        /// <summary>
        /// Applies an immutable settings override to any IDocument without requiring new
        /// interface members. The source is borrowed and must compose fresh elements per render.
        /// </summary>
        public static IDocument WithSettings(this IDocument document, DocumentSettings settings)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            return new ConfiguredDocument(document, settings);
        }

        private sealed class ConfiguredDocument : IDocument, IDocumentSettingsProvider
        {
            private readonly IDocument Source;
            private readonly DocumentSettings Settings;

            internal ConfiguredDocument(IDocument source, DocumentSettings settings)
            {
                Source = source;
                Settings = settings;
            }

            public DocumentMetadata GetMetadata() => Source.GetMetadata();
            public void Compose(IDocumentContainer container) => Source.Compose(container);
            public DocumentSettings GetSettings() => Settings;
        }
    }
}
