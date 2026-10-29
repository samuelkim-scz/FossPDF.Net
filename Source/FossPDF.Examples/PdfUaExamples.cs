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
    public class PdfUaExamples
    {
        [Test]
        public void CreateMachineValidatedAccessibleArchivalReport()
        {
            if (!File.Exists(Environment.GetEnvironmentVariable("FOSSPDF_VERAPDF_JAR")))
                Assert.Ignore("Set FOSSPDF_VERAPDF_JAR to the official veraPDF 1.30.3 CLI JAR.");
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(320, 240); page.Margin(20);
                page.Header().Text("Decorative report header");
                page.Content().Column(column =>
                {
                    column.Item().SemanticHeading1().Text("Service report");
                    column.Item().Text("Twelve requests completed; four requests awaiting a response.");
                    column.Item().SemanticLink("Read the report documentation")
                        .Hyperlink("https://example.org/report").Text("Report documentation");
                });
            })).WithMetadata(new DocumentMetadata { Title = "Service report", Language = "en-US" })
                .WithSettings(new DocumentSettings
                {
                    PdfUaConformance = PdfUaConformance.PdfUa1,
                    PdfAConformance = PdfAConformance.PdfA3A
                });
            var attachment = new PdfAAttachment(Encoding.UTF8.GetBytes("<requests completed='12' awaiting='4'/>"),
                new DocumentOperation.DocumentAttachment
                {
                    AttachmentName = "requests.xml", MimeType = "application/xml",
                    ModificationDate = DateTime.UtcNow,
                    Relationship = DocumentOperation.DocumentAttachmentRelationship.Data
                });
            var bytes = document.GeneratePdfUa(out var report, new PdfUaOptions { Attachments = new[] { attachment } });
            var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "pdfua-example");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "report.pdf"), bytes);
            File.WriteAllText(Path.Combine(directory, "report-ua1.json"), report.Json);
            File.WriteAllText(Path.Combine(directory, "report-pdfa3a.json"), report.ArchivalReport!.Json);
            Assert.That(report.MachineChecksPassed, Is.True);
            Assert.That(report.ArchivalReport.IsCompliant, Is.True);
            // The application author also reviews reading order, descriptions and usability
            // in an accessibility tool. A machine report cannot perform those human checks.
        }
    }
}
