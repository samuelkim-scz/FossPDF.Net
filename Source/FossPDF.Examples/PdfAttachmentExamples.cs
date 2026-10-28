using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FossPDF.Fluent;
using FossPDF.PdfOperations;
using NUnit.Framework;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace FossPDF.Examples
{
    /// <summary>
    /// Shows how an existing PDF receives an embedded attachment with an associated-file
    /// relationship and how its XMP metadata is extended. All examples write their artifacts under
    /// the test directory.
    /// </summary>
    public class PdfAttachmentExamples
    {
        [Test]
        public void AttachAFileWithAnAssociatedFileRelationship()
        {
            var directory = CreateOutputDirectory("pdf-operations-attachments");
            var sourcePath = Path.Combine(directory, "invoice.pdf");
            var attachmentPath = Path.Combine(directory, "terms.txt");
            var outputPath = Path.Combine(directory, "invoice-with-attachment.pdf");

            File.WriteAllBytes(sourcePath, CreateInvoice());
            File.WriteAllText(attachmentPath, "Payment is due within 30 days.\n", Encoding.UTF8);

            // The attachment payload is read when AddAttachment is called. The file name, media type,
            // description and relationship are all written into the PDF, so ordinary readers can
            // list and extract the file without rasterizing the document.
            DocumentOperation
                .LoadFile(sourcePath)
                .AddAttachment(new DocumentOperation.DocumentAttachment
                {
                    FilePath = attachmentPath,
                    AttachmentName = "terms.txt",
                    MimeType = "text/plain",
                    Description = "Payment terms",
                    Relationship = DocumentOperation.DocumentAttachmentRelationship.Supplement
                })
                .Save(outputPath);

            var output = File.ReadAllBytes(outputPath);
            var text = Encoding.Latin1.GetString(output);

            Assert.That(text, Does.Contain("/EmbeddedFiles"));
            Assert.That(text, Does.Contain("/AFRelationship"));
            Assert.That(text, Does.Contain("/Supplement"));

            var attachment = ReadAttachment(output);

            Assert.That(attachment.Key, Is.EqualTo("terms.txt"));
            Assert.That(attachment.MimeType, Is.EqualTo("text/plain"));
            Assert.That(attachment.Relationship, Is.EqualTo("Supplement"));
            Assert.That(attachment.Data, Is.EqualTo(File.ReadAllBytes(attachmentPath)));

            // Loading the result and saving it again keeps the attachment.
            var copiedPath = Path.Combine(directory, "invoice-copied.pdf");
            DocumentOperation.LoadFile(outputPath).Save(copiedPath);
            Assert.That(ReadAttachment(File.ReadAllBytes(copiedPath)).Data, Is.EqualTo(attachment.Data));
        }

        [Test]
        public void ExtendXmpMetadataWithACustomNamespace()
        {
            var directory = CreateOutputDirectory("pdf-operations-metadata");
            var sourcePath = Path.Combine(directory, "invoice.pdf");
            var outputPath = Path.Combine(directory, "invoice-with-metadata.pdf");

            File.WriteAllBytes(sourcePath, CreateInvoice());

            // The fragment must be well-formed XML and declare the namespaces it uses. It is appended
            // inside the XMP packet's first rdf:Description element and written as a PDF incremental
            // update, so the original bytes and page content stay unchanged.
            DocumentOperation
                .LoadFile(sourcePath)
                .ExtendMetadata(
                    "<invoice:reference xmlns:invoice=\"urn:example:invoices\">2026-002</invoice:reference>")
                .Save(outputPath);

            var output = File.ReadAllBytes(outputPath);
            var metadata = ReadMetadata(output);

            Assert.That(metadata, Does.Contain("urn:example:invoices"));

            var document = XDocument.Parse(metadata);
            var reference = document.Descendants(XName.Get("reference", "urn:example:invoices")).Single();

            Assert.That(reference.Value, Is.EqualTo("2026-002"));

            // The incremental update preserves the original document information entry.
            using var stream = new MemoryStream(output, writable: false);
            using var reopened = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

            Assert.That(reopened.PageCount, Is.EqualTo(1));
        }

        private static byte[] CreateInvoice()
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(300, 200);
                    page.Margin(20);

                    page.Content().Column(column =>
                    {
                        column.Item().Text("Invoice 2026-002").FontSize(20).Bold();
                        column.Item().PaddingTop(10).Text("Consulting services");
                        column.Item().PaddingTop(20).Text("Total: 1,200.00 EUR").Bold();
                    });
                });
            }).GeneratePdf();
        }

        private static AttachmentInfo ReadAttachment(byte[] pdf)
        {
            using var stream = new MemoryStream(pdf, writable: false);
            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

            var names = document.Internals.Catalog.Elements.GetDictionary("/Names");
            var embedded = names?.Elements.GetDictionary("/EmbeddedFiles");
            var entries = embedded?.Elements.GetArray("/Names");
            var key = ((PdfString)entries.Elements[0]).Value;

            var specification = ResolveDictionary(entries.Elements[1]);
            var files = specification.Elements.GetDictionary("/EF");
            var fileStream = ResolveDictionary(files.Elements.GetObject("/F"));

            if (fileStream.Stream.IsFiltered())
                fileStream.Stream.TryUncompress();

            return new AttachmentInfo
            {
                Key = key,
                MimeType = TrimName(fileStream.Elements.GetName("/Subtype")),
                Relationship = TrimName(specification.Elements.GetName("/AFRelationship")),
                Data = fileStream.Stream.UnfilteredValue
            };
        }

        private static string ReadMetadata(byte[] pdf)
        {
            using var stream = new MemoryStream(pdf, writable: false);
            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

            var metadata = ResolveDictionary(document.Internals.Catalog.Elements.GetObject("/Metadata"));

            if (metadata.Stream.IsFiltered())
                metadata.Stream.TryUncompress();

            return Encoding.UTF8.GetString(metadata.Stream.UnfilteredValue);
        }

        private static PdfDictionary ResolveDictionary(PdfItem item)
        {
            if (item is PdfReference reference)
                return reference.Value as PdfDictionary;

            return item as PdfDictionary;
        }

        private static string TrimName(string value)
        {
            return value.StartsWith("/", StringComparison.Ordinal) ? value.Substring(1) : value;
        }

        private static string CreateOutputDirectory(string name)
        {
            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, name);
            Directory.CreateDirectory(directory);
            return directory;
        }

        private sealed class AttachmentInfo
        {
            public string Key { get; set; }
            public string MimeType { get; set; }
            public string Relationship { get; set; }
            public byte[] Data { get; set; }
        }
    }
}
