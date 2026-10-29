using System;
using System.IO;
using System.Threading;
using FossPDF.Infrastructure;
using FossPDF.PdfOperations;

namespace FossPDF.Fluent
{
    /// <summary>Generates and validates PDF/A-2/3 in the optional .NET 9 operations package.</summary>
    public static class PdfAExtensions
    {
        /// <summary>
        /// Renders the selected DocumentSettings.PdfAConformance profile and validates the final
        /// bytes with veraPDF 1.30.3. Legacy or an inherited PdfA=true maps to PDF/A-2b.
        /// A-level profiles use the semantic writer; every profile requires complete glyph coverage.
        /// </summary>
        public static byte[] GeneratePdfA(this IDocument document, PdfAOptions? options = null,
            CancellationToken cancellationToken = default) => document.GeneratePdfA(out _, options, cancellationToken);

        /// <summary>Generates validated bytes and returns the complete veraPDF report for audit or CI.</summary>
        public static byte[] GeneratePdfA(this IDocument document, out PdfAValidationReport report,
            PdfAOptions? options = null, CancellationToken cancellationToken = default)
        {
            var result = PdfConformanceWriter.Generate(document, options, true, false, cancellationToken);
            report = result.Archival!;
            return result.Bytes;
        }

        /// <summary>Stages validated bytes, then writes at the current position without closing or seeking the stream.</summary>
        public static void GeneratePdfA(this IDocument document, Stream stream, PdfAOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (!stream.CanWrite)
                throw new ArgumentException("The stream must be writable.", nameof(stream));
            var bytes = document.GeneratePdfA(options, cancellationToken);
            stream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>Generates and validates before replacing the destination file.</summary>
        public static void GeneratePdfA(this IDocument document, string filePath, PdfAOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (filePath == null)
                throw new ArgumentNullException(nameof(filePath));
            var bytes = document.GeneratePdfA(options, cancellationToken);
            File.WriteAllBytes(filePath, bytes);
        }

    }
}
