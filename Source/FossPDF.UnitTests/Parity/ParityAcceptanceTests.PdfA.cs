#if NET9_0
using System;
using System.IO;
using System.Linq;
using System.Text;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.PdfOperations;
using NUnit.Framework;

namespace FossPDF.UnitTests.Parity
{
    public partial class ParityAcceptanceTests
    {
        [ParityFixtureRunner("ACC-402")]
        private static void RunPdfA(ParityFixture fixture)
        {
            PdfATests.RequireValidator();
            var qpdf = QpdfTestTool.Require();
            var profiles = new[] { PdfAConformance.PdfA2A, PdfAConformance.PdfA2B, PdfAConformance.PdfA2U,
                PdfAConformance.PdfA3A, PdfAConformance.PdfA3B, PdfAConformance.PdfA3U };
            foreach (var profile in profiles)
            foreach (var mode in new[] { "default", "compressed", "linearized", "uncompressed" })
            {
                var settings = new DocumentSettings
                {
                    PdfAConformance = profile,
                    EnableAdditionalCompression = mode == "compressed",
                    CompressDocument = mode != "uncompressed",
                    Linearize = mode == "linearized",
                    QpdfExecutablePath = qpdf
                };
                var options = new PdfAOptions
                {
                    Attachments = profile >= PdfAConformance.PdfA3A
                        ? Enum.GetValues<DocumentOperation.DocumentAttachmentRelationship>().Select(relationship =>
                            new PdfAAttachment(Encoding.UTF8.GetBytes("<data>" + relationship + "</data>"),
                                new DocumentOperation.DocumentAttachment
                                {
                                    AttachmentName = relationship + "-caf\u00e9.xml", MimeType = "application/xml",
                                    Relationship = relationship, ModificationDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
                                })).ToArray()
                        : Array.Empty<PdfAAttachment>()
                };
                byte[] bytes;
                PdfAValidationReport report;
                try { bytes = PdfATests.CreateDocument().WithSettings(settings).GeneratePdfA(out report, options); }
                catch (PdfAConformanceException exception)
                {
                    SaveArtifact(fixture.Id, profile + "-" + mode + "-failed.json", Encoding.UTF8.GetBytes(exception.Report.Json));
                    throw;
                }
                Assert.That(report.IsCompliant, Is.True, report.Json);
                Assert.That(report.FailedChecks, Is.Zero);
                SaveArtifact(fixture.Id, profile + "-" + mode + ".pdf", bytes);
                SaveArtifact(fixture.Id, profile + "-" + mode + ".json", Encoding.UTF8.GetBytes(report.Json));
            }
            var legacy = PdfATests.CreateDocument().WithMetadata(PdfATests.Metadata(true));
            var original = legacy.GeneratePdf();
            var legacyReport = PdfAValidator.Validate(original, PdfAConformance.PdfA2B);
            Assert.That(legacyReport.IsCompliant, Is.True, legacyReport.Json);
            SaveArtifact(fixture.Id, "legacy-original.pdf", original);
            SaveArtifact(fixture.Id, "legacy-original.json", Encoding.UTF8.GetBytes(legacyReport.Json));
            var validated = legacy.GeneratePdfA(out var validatedReport);
            SaveArtifact(fixture.Id, "legacy-validated.pdf", validated);
            SaveArtifact(fixture.Id, "legacy-validated.json", Encoding.UTF8.GetBytes(validatedReport.Json));
            SaveArtifact(fixture.Id, "reference.pdf", PdfATests.CreateDocument().GeneratePdf());
        }

        [Test]
        public void PdfAMergedPageNumberStrategies()
        {
            PdfATests.RequireValidator();
            foreach (var profile in new[] { PdfAConformance.PdfA2A, PdfAConformance.PdfA2U })
            foreach (var continuous in new[] { false, true })
            {
                Document Part(string label) => Document.Create(container => container.Page(page =>
                {
                    page.Size(200, 120);
                    page.Content().Text(label);
                    page.Footer().Text(text => { text.Span("Page "); text.CurrentPageNumber(); });
                }));
                var merged = Document.Merge(Part("First"), Part("Second")).WithMetadata(PdfATests.Metadata())
                    .WithSettings(new DocumentSettings { PdfAConformance = profile });
                if (continuous) merged.UseContinuousPageNumbers();
                var bytes = merged.GeneratePdfA(out var report);
                var name = "merged-" + profile + (continuous ? "-continuous" : "-original");
                SaveArtifact("ACC-402", name + ".pdf", bytes);
                SaveArtifact("ACC-402", name + ".json", Encoding.UTF8.GetBytes(report.Json));
            }
        }
    }
}
#endif
