using System;
using System.IO;
using System.Text;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.PdfOperations;
using NUnit.Framework;

namespace FossPDF.Examples
{
    public class PdfAExamples
    {
        [Test]
        public void CreateValidatedPdfA3AWithAssociatedXml()
        {
            if (!File.Exists(Environment.GetEnvironmentVariable("FOSSPDF_VERAPDF_JAR")))
                Assert.Ignore("Set FOSSPDF_VERAPDF_JAR to the official veraPDF 1.30.3 CLI JAR.");
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(320, 240);
                page.Margin(20);
                page.Content().Column(column =>
                {
                    column.Item().SemanticHeading1().Text("Archived report");
                    column.Item().Text("Revenue: twelve units. Unicode: caf\u00e9.");
                });
            })).WithMetadata(new DocumentMetadata
            {
                Title = "Archived report", Language = "en-US", Author = "FossPDF"
            }).WithSettings(new DocumentSettings { PdfAConformance = PdfAConformance.PdfA3A });
            var attachment = new PdfAAttachment(Encoding.UTF8.GetBytes("<report><revenue>12</revenue></report>"),
                new DocumentOperation.DocumentAttachment
                {
                    AttachmentName = "report.xml", MimeType = "application/xml",
                    Relationship = DocumentOperation.DocumentAttachmentRelationship.Data,
                    ModificationDate = DateTime.UtcNow
                });
            var bytes = document.GeneratePdfA(out var report, new PdfAOptions { Attachments = new[] { attachment } });
            var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "pdfa-example");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "report.pdf"), bytes);
            File.WriteAllText(Path.Combine(directory, "report-verapdf.json"), report.Json);
            Assert.That(report.IsCompliant, Is.True);
        }
    }
}
