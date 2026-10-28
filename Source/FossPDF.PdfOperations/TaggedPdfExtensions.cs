using System;
using System.IO;
using FossPDF.Drawing;
using FossPDF.Infrastructure;
using FossPDF.PdfOperations;

namespace FossPDF.Fluent
{
    /// <summary>
    /// Produces tagged PDF files from the semantic structure captured by
    /// <see cref="GenerateExtensions.GenerateTaggedContent(IDocument)"/>. The writer lives in the
    /// optional <c>FossPDF.PdfOperations</c> package; the core package only captures the semantic
    /// tree and the ordered paint batches.
    /// </summary>
    /// <remarks>
    /// A tagged PDF keeps the visual rendering unchanged while adding a logical structure tree,
    /// marked content identifiers, alternative text and the document language. Content that is not
    /// handled by standard text/image/link/table tags or explicit semantics is decorative.
    /// Documents that use <c>ZIndex</c> or
    /// the qpdf compression/linearization pass cannot be captured and are rejected by the core
    /// package before this writer runs.
    /// </remarks>
    public static class TaggedPdfExtensions
    {
        /// <summary>Renders the document and returns the tagged PDF as a byte array.</summary>
        public static byte[] GenerateTaggedPdf(this IDocument document)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            return document.GenerateTaggedContent().ToTaggedPdf();
        }

        /// <summary>Renders the document and writes the tagged PDF to a file.</summary>
        public static void GenerateTaggedPdf(this IDocument document, string filePath)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            if (filePath == null)
                throw new ArgumentNullException(nameof(filePath));

            var data = document.GenerateTaggedPdf();

            if (File.Exists(filePath))
                File.Delete(filePath);

            File.WriteAllBytes(filePath, data);
        }

        /// <summary>
        /// Renders the document and writes the tagged PDF to the supplied writable stream. The
        /// stream is neither closed nor seeked by this method.
        /// </summary>
        public static void GenerateTaggedPdf(this IDocument document, Stream stream)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (!stream.CanWrite)
                throw new ArgumentException("The stream must be writable.", nameof(stream));

            var data = document.GenerateTaggedPdf();
            stream.Write(data, 0, data.Length);
        }

        /// <summary>
        /// Assembles previously captured semantic content into a tagged PDF.
        /// </summary>
        public static byte[] ToTaggedPdf(this TaggedDocumentContent content)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            return TaggedPdfAssembly.Assemble(content);
        }
    }
}
