using System;
using System.IO;
using System.Text;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.PdfOperations;
using NUnit.Framework;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace FossPDF.Examples
{
    /// <summary>
    /// Shows how to write selectable PDF/A-2 and PDF/A-3 conformance levels through the optional
    /// FossPDF.PdfOperations package: the level is chosen in the document settings or passed as an
    /// argument, attachments are permitted only for PDF/A-3, and the legacy PdfA flag maps to
    /// PDF/A-2B. All examples write their artifacts under the test directory.
    /// </summary>
    public class PdfAExamples
    {
        [Test]
        public void CreatePdfA2BFromDocumentSettings()
        {
            var directory = CreateOutputDirectory("pdfa");
            var path = Path.Combine(directory, "report-pdfa-2b.pdf");

            var document = CreateSemanticDocument("Quarterly totals", "Revenue is twelve units.")
                .WithSettings(new DocumentSettings
                {
                    PdfAConformance = PdfAConformance.PdfA2B
                });

            // Select the level once in the settings; GeneratePdfA writes the identification,
            // sRGB output intent, embedded fonts and tagged structure.
            document.GeneratePdfA(path);

            var xmp = ReadXmp(path);

            Assert.That(xmp, Does.Contain("<pdfaid:part>2</pdfaid:part>"));
            Assert.That(xmp, Does.Contain("<pdfaid:conformance>B</pdfaid:conformance>"));

            using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);

            Assert.That(pdf.Internals.Catalog.Elements.GetArray("/OutputIntents"), Is.Not.Null);
            Assert.That(pdf.Internals.Catalog.Elements.GetDictionary("/StructTreeRoot"), Is.Not.Null);
        }

        [Test]
        public void CreatePdfA3UWithAnEmbeddedInvoice()
        {
            var directory = CreateOutputDirectory("pdfa");
            var path = Path.Combine(directory, "report-pdfa-3u.pdf");
            var attachmentPath = Path.Combine(directory, "invoice.xml");

            File.WriteAllText(
                attachmentPath,
                "<invoice><total>12.00</total></invoice>",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var document = CreateSemanticDocument("Invoice", "The total is twelve units.");

            // PDF/A-3 permits embedded files. The attachment is listed in the name tree and in the
            // catalog's associated-files array; the relationship describes its role.
            document.GeneratePdfA(path, PdfAConformance.PdfA3U, new PdfAOptions
            {
                Attachments = new[]
                {
                    new DocumentOperation.DocumentAttachment
                    {
                        FilePath = attachmentPath,
                        AttachmentName = "invoice.xml",
                        MimeType = "application/xml",
                        Relationship = DocumentOperation.DocumentAttachmentRelationship.Data
                    }
                }
            });

            var xmp = ReadXmp(path);

            Assert.That(xmp, Does.Contain("<pdfaid:part>3</pdfaid:part>"));
            Assert.That(xmp, Does.Contain("<pdfaid:conformance>U</pdfaid:conformance>"));

            using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            var names = pdf.Internals.Catalog.Elements.GetDictionary("/Names");
            var embedded = names.Elements.GetDictionary("/EmbeddedFiles");

            Assert.That(embedded.Elements.GetArray("/Names"), Is.Not.Null);
            Assert.That(pdf.Internals.Catalog.Elements.GetArray("/AF"), Is.Not.Null);
        }

        [Test]
        public void RejectAttachmentsForPdfA2()
        {
            var directory = CreateOutputDirectory("pdfa");
            var attachmentPath = Path.Combine(directory, "payload.txt");
            File.WriteAllText(attachmentPath, "payload");

            var document = CreateSemanticDocument("Report", "Body");

            // PDF/A-2 has no associated-file concept, so the combination is rejected instead of
            // silently dropping the attachment or emitting an invalid compliance claim.
            var exception = Assert.Throws<ArgumentException>(() =>
                document.GeneratePdfA(PdfAConformance.PdfA2U, new PdfAOptions
                {
                    Attachments = new[]
                    {
                        new DocumentOperation.DocumentAttachment
                        {
                            FilePath = attachmentPath,
                            AttachmentName = "payload.txt",
                            MimeType = "text/plain"
                        }
                    }
                }));

            Assert.That(exception.Message, Does.Contain("PDF/A-3"));
        }

        [Test]
        public void MapTheLegacyPdfAFlagToPdfA2B()
        {
            var directory = CreateOutputDirectory("pdfa");
            var path = Path.Combine(directory, "legacy-pdfa.pdf");

            // DocumentMetadata.PdfA carries no part or level. The writer states the concrete
            // profile it produced instead of leaving an ambiguous claim.
            CreateSemanticDocument("Legacy report", "Body")
                .WithMetadata(new DocumentMetadata
                {
                    Title = "Legacy report",
                    Language = "en-US",
                    PdfA = true
                })
                .GeneratePdfA(path);

            var xmp = ReadXmp(path);

            Assert.That(xmp, Does.Contain("<pdfaid:part>2</pdfaid:part>"));
            Assert.That(xmp, Does.Contain("<pdfaid:conformance>B</pdfaid:conformance>"));
        }

        private static Document CreateSemanticDocument(string heading, string paragraph)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(320, 200);
                    page.Margin(20);
                    page.DefaultTextStyle(style => style.FontSize(12));

                    page.Header().SemanticIgnore().Text("Quarterly report");

                    page.Content().Column(column =>
                    {
                        column.Item().SemanticHeading1().Text(heading);
                        column.Item().SemanticParagraph().Text(paragraph);
                    });
                });
            }).WithMetadata(new DocumentMetadata
            {
                Title = heading,
                Author = "FossPDF",
                Language = "en-US"
            });
        }

        private static string ReadXmp(string path)
        {
            using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            var metadata = Resolve(pdf.Internals.Catalog.Elements.GetObject("/Metadata"));

            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata.Stream, Is.Not.Null);

            if (metadata.Stream.IsFiltered())
                Assert.That(metadata.Stream.TryUncompress(), Is.True);

            return Encoding.UTF8.GetString(metadata.Stream.UnfilteredValue);
        }

        private static PdfDictionary Resolve(PdfItem item)
        {
            return item switch
            {
                PdfReference reference => reference.Value as PdfDictionary,
                PdfDictionary dictionary => dictionary,
                _ => null
            };
        }

        private static string CreateOutputDirectory(string name)
        {
            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, name);
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
