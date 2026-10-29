using System;
using System.IO;
using System.Linq;
using System.Threading;
using FossPDF.Drawing;
using FossPDF.Infrastructure;
using FossPDF.PdfOperations;

namespace FossPDF.Fluent
{
    /// <summary>Packages externally supplied EN16931 XML with validated PDF/A-3 and invoice metadata.</summary>
    public static class EInvoiceExtensions
    {
        public static byte[] GenerateEInvoice(this IDocument document, byte[] invoiceXml, EInvoiceProfile profile,
            EInvoiceOptions? options = null, CancellationToken cancellationToken = default)
            => document.GenerateEInvoice(invoiceXml, profile, out _, options, cancellationToken);

        /// <summary>
        /// Validates XML with Mustang 2.26.0, renders the selected PDF/A-3 profile, then validates the
        /// final PDF with veraPDF and Mustang. The author owns invoice correctness and visual/XML agreement.
        /// Caller XML is copied and never rewritten. No validator is downloaded at runtime.
        /// </summary>
        public static byte[] GenerateEInvoice(this IDocument document, byte[] invoiceXml, EInvoiceProfile profile,
            out EInvoiceGenerationReport report, EInvoiceOptions? options = null, CancellationToken cancellationToken = default)
        {
            EInvoicePackaging.CheckProfile(profile);
            var configuration = GenerationConfiguration.Resolve(document);
            if (configuration.Settings.PdfAConformance is not (PdfAConformance.PdfA3A or PdfAConformance.PdfA3B or PdfAConformance.PdfA3U))
                throw new ArgumentException("GenerateEInvoice requires an explicit PDF/A-3a, PDF/A-3b or PDF/A-3u selection.", nameof(document));
            var xml = EInvoicePackaging.CaptureXml(invoiceXml);
            options ??= new EInvoiceOptions();
            var attachments = (options.Attachments ?? throw new ArgumentException("Attachments must not be null.", nameof(options))).ToArray();
            foreach (var attachment in attachments)
            {
                if (attachment == null) throw new ArgumentException("An attachment must not be null.", nameof(options));
                foreach (var name in new[] { attachment.Input.Key, attachment.Input.Name })
                    if (new[] { "factur-x.xml", "zugferd-invoice.xml", "xrechnung.xml" }.Contains(name, StringComparer.OrdinalIgnoreCase))
                        throw new ArgumentException("An additional attachment uses a reserved invoice filename or key.", nameof(options));
            }
            var metadata = EInvoicePackaging.Metadata(options.MetadataExtensions);
            // Prepare both tools before executing either or composing the document.
            PdfAValidator.Prepare(options, cancellationToken);
            var jar = MustangValidator.Prepare(options, cancellationToken);
            var xmlReport = MustangValidator.Validate(xml, false, options, jar, cancellationToken);
            if (!xmlReport.IsValid) throw new EInvoiceConformanceException(xmlReport);
            var invoice = new PdfAAttachment(xml, new DocumentOperation.DocumentAttachment
            {
                AttachmentName = EInvoicePackaging.FileName, Key = EInvoicePackaging.FileName,
                MimeType = "text/xml", Description = "EN16931 invoice data",
                ModificationDate = configuration.Metadata.ModifiedDate,
                Relationship = DocumentOperation.DocumentAttachmentRelationship.Alternative
            });
            var result = PdfConformanceWriter.Generate(document, options with
            {
                Attachments = new[] { invoice }.Concat(attachments).ToArray(), MetadataExtensions = metadata
            }, true, false, cancellationToken, configuration);
            // Mustang 2.26.0 does not check the required relationship or cross-check an allowed
            // XMP profile with the XML guideline. Check the exact final packaging independently.
            EInvoicePackaging.ValidateOutput(result.Bytes, xml);
            var packageReport = MustangValidator.Validate(result.Bytes, true, options, jar, cancellationToken);
            if (!packageReport.IsValid) throw new EInvoiceConformanceException(packageReport);
            report = new EInvoiceGenerationReport(profile, xmlReport, packageReport, result.Archival!, result.Accessibility);
            return result.Bytes;
        }

        /// <summary>Validates completely, then writes at the current position and leaves the stream open.</summary>
        public static void GenerateEInvoice(this IDocument document, byte[] invoiceXml, EInvoiceProfile profile,
            Stream stream, EInvoiceOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanWrite) throw new ArgumentException("The stream must be writable.", nameof(stream));
            var bytes = document.GenerateEInvoice(invoiceXml, profile, options, cancellationToken);
            stream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>Validates completely before replacing the destination file.</summary>
        public static void GenerateEInvoice(this IDocument document, byte[] invoiceXml, EInvoiceProfile profile,
            string filePath, EInvoiceOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));
            var bytes = document.GenerateEInvoice(invoiceXml, profile, options, cancellationToken);
            File.WriteAllBytes(filePath, bytes);
        }
    }
}
