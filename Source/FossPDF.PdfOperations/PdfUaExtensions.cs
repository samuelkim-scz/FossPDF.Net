using System;
using System.IO;
using System.Threading;
using FossPDF.Infrastructure;
using FossPDF.PdfOperations;

namespace FossPDF.Fluent
{
    /// <summary>
    /// Produces PDF/UA-1 with final machine validation. Authors must also check reading order,
    /// alternative text, language and assistive-technology usability. Requires the .NET 9 companion.
    /// </summary>
    public static class PdfUaExtensions
    {
        public static byte[] GeneratePdfUa(this IDocument document, PdfUaOptions? options = null,
            CancellationToken cancellationToken = default) => document.GeneratePdfUa(out _, options, cancellationToken);

        /// <summary>Returns the machine report and, for combined output, its archival report.</summary>
        public static byte[] GeneratePdfUa(this IDocument document, out PdfUaValidationReport report,
            PdfUaOptions? options = null, CancellationToken cancellationToken = default)
        {
            var result = PdfConformanceWriter.Generate(document, options, false, true, cancellationToken);
            report = result.Accessibility!;
            return result.Bytes;
        }

        /// <summary>Stages and validates before writing at the current position; the stream stays open.</summary>
        public static void GeneratePdfUa(this IDocument document, Stream stream, PdfUaOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (!stream.CanWrite)
                throw new ArgumentException("The stream must be writable.", nameof(stream));
            var bytes = document.GeneratePdfUa(options, cancellationToken);
            stream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>Generates and validates before replacing the destination file.</summary>
        public static void GeneratePdfUa(this IDocument document, string filePath, PdfUaOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (filePath == null)
                throw new ArgumentNullException(nameof(filePath));
            var bytes = document.GeneratePdfUa(options, cancellationToken);
            File.WriteAllBytes(filePath, bytes);
        }
    }
}
