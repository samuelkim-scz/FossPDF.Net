using System;
using System.IO;
using FossPDF.Drawing;
using FossPDF.Infrastructure;
using FossPDF.PdfOperations;

namespace FossPDF.Fluent
{
    /// <summary>
    /// Produces PDF/A-2 and PDF/A-3 documents from the semantic structure captured by
    /// <see cref="GenerateExtensions.GenerateTaggedContent(IDocument)"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Select a level through <see cref="DocumentSettings.PdfAConformance"/> and call
    /// <see cref="GeneratePdfA(IDocument)"/>, or pass the level explicitly. The six supported
    /// profiles are PDF/A-2a, 2b, 2u, 3a, 3b and 3u. The written document always contains an sRGB
    /// output intent, embedded fonts with Unicode mappings, document identification metadata, the
    /// semantic structure tree and, for PDF/A-3, embedded files listed in the associated-files
    /// array.
    /// </para>
    /// <para>
    /// A and U levels require a document language for the accessible level; without
    /// <see cref="DocumentMetadata.Language"/> an A-level document is rejected rather than given an
    /// undeclared language. Attachments are permitted only in PDF/A-3. Tagged generation cannot be
    /// combined with the qpdf compression/linearization pass, and PDF/A output is never encrypted;
    /// both combinations are rejected or unavailable rather than silently degraded.
    /// </para>
    /// <para>
    /// The legacy <see cref="DocumentMetadata.PdfA"/> flag carries no part or conformance level. In
    /// this writer it maps to PDF/A-2B and the XMP metadata states that level explicitly. The flag
    /// alone is never presented as a validated profile.
    /// </para>
    /// </remarks>
    public static class PdfAExtensions
    {
        /// <summary>
        /// Renders the document and returns it as PDF/A, using the profile selected through
        /// <see cref="DocumentSettings.PdfAConformance"/> or the legacy
        /// <see cref="DocumentMetadata.PdfA"/> flag.
        /// </summary>
        /// <exception cref="InvalidOperationException">No PDF/A profile was selected.</exception>
        public static byte[] GeneratePdfA(this IDocument document)
        {
            return Generate(document, null, null);
        }

        /// <summary>
        /// Renders the document and returns it as PDF/A, using the profile selected through
        /// <see cref="DocumentSettings.PdfAConformance"/> and the supplied options.
        /// </summary>
        public static byte[] GeneratePdfA(this IDocument document, PdfAOptions? options)
        {
            return Generate(document, null, options);
        }

        /// <summary>
        /// Renders the document and returns it as the requested PDF/A profile.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The level is not a supported profile.</exception>
        /// <exception cref="InvalidOperationException">
        /// The document settings select a different PDF/A profile.
        /// </exception>
        public static byte[] GeneratePdfA(this IDocument document, PdfAConformance conformance)
        {
            return Generate(document, conformance, null);
        }

        /// <summary>
        /// Renders the document and returns it as the requested PDF/A profile with the supplied
        /// options.
        /// </summary>
        public static byte[] GeneratePdfA(
            this IDocument document,
            PdfAConformance conformance,
            PdfAOptions? options)
        {
            return Generate(document, conformance, options);
        }

        /// <summary>Renders the document and writes it as PDF/A to a file.</summary>
        public static void GeneratePdfA(this IDocument document, string filePath)
        {
            WriteFile(filePath, document.GeneratePdfA());
        }

        /// <summary>Renders the document and writes the requested PDF/A profile to a file.</summary>
        public static void GeneratePdfA(this IDocument document, string filePath, PdfAConformance conformance)
        {
            WriteFile(filePath, document.GeneratePdfA(conformance));
        }

        /// <summary>Renders the document and writes the requested PDF/A profile to a file.</summary>
        public static void GeneratePdfA(
            this IDocument document,
            string filePath,
            PdfAConformance conformance,
            PdfAOptions? options)
        {
            WriteFile(filePath, document.GeneratePdfA(conformance, options));
        }

        /// <summary>
        /// Renders the document and writes it as PDF/A to the supplied writable stream. The stream
        /// is neither closed nor seeked by this method.
        /// </summary>
        public static void GeneratePdfA(this IDocument document, Stream stream)
        {
            WriteStream(stream, document.GeneratePdfA());
        }

        /// <summary>
        /// Renders the document and writes the requested PDF/A profile to the supplied writable
        /// stream. The stream is neither closed nor seeked by this method.
        /// </summary>
        public static void GeneratePdfA(this IDocument document, Stream stream, PdfAConformance conformance)
        {
            WriteStream(stream, document.GeneratePdfA(conformance));
        }

        /// <summary>
        /// Renders the document and writes the requested PDF/A profile to the supplied writable
        /// stream. The stream is neither closed nor seeked by this method.
        /// </summary>
        public static void GeneratePdfA(
            this IDocument document,
            Stream stream,
            PdfAConformance conformance,
            PdfAOptions? options)
        {
            WriteStream(stream, document.GeneratePdfA(conformance, options));
        }

        /// <summary>
        /// Assembles previously captured semantic content as PDF/A. When
        /// <paramref name="conformance"/> is omitted, the profile selected through
        /// <see cref="DocumentSettings.PdfAConformance"/> is used.
        /// </summary>
        public static byte[] ToPdfA(
            this TaggedDocumentContent content,
            PdfAConformance? conformance = null,
            PdfAOptions? options = null)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            ValidateSelection(content, conformance);

            return TaggedPdfAssembly.Assemble(content, conformance, options);
        }

        private static byte[] Generate(IDocument document, PdfAConformance? conformance, PdfAOptions? options)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            if (conformance.HasValue)
                ValidateConformanceArgument(conformance.Value);

            // Capture once so the selected level recorded from the settings can be compared with
            // the explicitly requested level before anything is written.
            var content = document.GenerateTaggedContent();

            ValidateSelection(content, conformance);

            return TaggedPdfAssembly.Assemble(content, conformance, options);
        }

        private static void ValidateSelection(TaggedDocumentContent content, PdfAConformance? requested)
        {
            if (requested.HasValue)
            {
                ValidateConformanceArgument(requested.Value);

                if (PdfAProfile.IsSelectable(content.PdfAConformance ?? PdfAConformance.None) &&
                    content.PdfAConformance != requested)
                {
                    throw new InvalidOperationException(
                        $"The document settings select {DisplayName(content.PdfAConformance!.Value)} but " +
                        $"{DisplayName(requested.Value)} was requested. Select the level in one place.");
                }

                return;
            }

            var profile = PdfAProfile.Resolve(null, content.PdfAConformance, content.Metadata.PdfA);

            if (profile == null)
            {
                throw new InvalidOperationException(
                    "No PDF/A profile was selected. Set DocumentSettings.PdfAConformance to one of " +
                    "PdfA2A, PdfA2B, PdfA2U, PdfA3A, PdfA3B or PdfA3U, or pass the level to GeneratePdfA.");
            }
        }

        private static void ValidateConformanceArgument(PdfAConformance conformance)
        {
            if (!Enum.IsDefined(typeof(PdfAConformance), conformance))
                throw new ArgumentOutOfRangeException(nameof(conformance));

            if (conformance == PdfAConformance.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(conformance),
                    "None disables PDF/A output. Select a PDF/A-2 or PDF/A-3 level.");
            }
        }

        private static string DisplayName(PdfAConformance conformance)
        {
            return PdfAProfile.FromConformance(conformance)?.DisplayName ?? conformance.ToString();
        }

        private static void WriteFile(string filePath, byte[] data)
        {
            if (filePath == null)
                throw new ArgumentNullException(nameof(filePath));

            if (File.Exists(filePath))
                File.Delete(filePath);

            File.WriteAllBytes(filePath, data);
        }

        private static void WriteStream(Stream stream, byte[] data)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (!stream.CanWrite)
                throw new ArgumentException("The stream must be writable.", nameof(stream));

            stream.Write(data, 0, data.Length);
        }
    }
}
