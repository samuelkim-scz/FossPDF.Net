#if NET9_0
using System;
using System.Text;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.PdfOperations;
using NUnit.Framework;

namespace FossPDF.UnitTests.Parity
{
    public partial class ParityAcceptanceTests
    {
        [ParityFixtureRunner("ACC-404")]
        private static void RunEInvoice(ParityFixture fixture)
        {
            EInvoiceTests.RequireValidators();
            var qpdf = QpdfTestTool.Require();
            var xml = EInvoiceTests.InvoiceXml();
            SaveArtifact(fixture.Id, "invoice.xml", xml);
            SaveArtifact(fixture.Id, "reference.pdf", EInvoiceTests.CreateDocument()
                .WithSettings(new DocumentSettings { PdfAConformance = PdfAConformance.None }).GeneratePdf());
            foreach (var profile in Enum.GetValues<EInvoiceProfile>())
            foreach (var archival in new[] { PdfAConformance.PdfA3A, PdfAConformance.PdfA3B, PdfAConformance.PdfA3U })
            foreach (var mode in archival == PdfAConformance.PdfA3B
                ? new[] { "default", "compressed", "linearized", "uncompressed" } : new[] { "default" })
            {
                var document = EInvoiceTests.CreateDocument().WithSettings(new DocumentSettings
                {
                    PdfAConformance = archival, EnableAdditionalCompression = mode == "compressed",
                    Linearize = mode == "linearized", CompressDocument = mode != "uncompressed", QpdfExecutablePath = qpdf
                });
                var bytes = document.GenerateEInvoice(xml, profile, out var report);
                var stem = profile + "-" + archival + "-" + mode;
                SaveArtifact(fixture.Id, stem + ".pdf", bytes);
                SaveArtifact(fixture.Id, stem + "-verapdf.json", Encoding.UTF8.GetBytes(report.ArchivalReport.Json));
                SaveArtifact(fixture.Id, stem + "-xml.xml", Encoding.UTF8.GetBytes(report.InvoiceXmlReport.Xml));
                SaveArtifact(fixture.Id, stem + "-package.xml", Encoding.UTF8.GetBytes(report.PackageReport.Xml));
            }
            var accessible = EInvoiceTests.CreateDocument().WithSettings(new DocumentSettings
                { PdfAConformance = PdfAConformance.PdfA3A, PdfUaConformance = PdfUaConformance.PdfUa1 });
            var combined = accessible.GenerateEInvoice(xml, EInvoiceProfile.FacturX1092En16931, out var combinedReport,
                new EInvoiceOptions
                {
                    MetadataExtensions = "<dc:source xmlns:dc='http://purl.org/dc/elements/1.1/'>Example accounting system</dc:source>",
                    Attachments = new[] { new PdfAAttachment(Encoding.UTF8.GetBytes("item,net\nreport,12.00\n"),
                        new DocumentOperation.DocumentAttachment { AttachmentName = "support.csv", MimeType = "text/csv",
                            Relationship = DocumentOperation.DocumentAttachmentRelationship.Supplement,
                            ModificationDate = new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc) }) }
                });
            Assert.That(combinedReport.AccessibilityReport!.MachineChecksPassed, Is.True);
            SaveArtifact(fixture.Id, "combined-ua1.pdf", combined);
            SaveArtifact(fixture.Id, "combined-ua1.json", Encoding.UTF8.GetBytes(combinedReport.AccessibilityReport.Json));
        }
    }
}
#endif
