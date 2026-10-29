#if NET9_0
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.PdfOperations;
using NUnit.Framework;
using PdfSharp.Pdf.IO;

namespace FossPDF.UnitTests
{
    [NonParallelizable]
    public class PdfUaTests
    {
        private static readonly string OutputDirectory = Path.Combine(AppContext.BaseDirectory, "parity-artifacts", "ACC-403");

        [Test]
        public void ProduceMachineQualificationCorpus()
        {
            PdfATests.RequireValidator();
            var qpdf = QpdfTestTool.Require();
            Directory.CreateDirectory(OutputDirectory);
            var profiles = new[] { PdfAConformance.None, PdfAConformance.PdfA2A, PdfAConformance.PdfA2B,
                PdfAConformance.PdfA2U, PdfAConformance.PdfA3A, PdfAConformance.PdfA3B, PdfAConformance.PdfA3U };
            foreach (var profile in profiles)
            foreach (var mode in profile == PdfAConformance.None
                         ? new[] { "default", "compressed", "linearized", "uncompressed" }
                         : new[] { "default", "optimized" })
            {
                var document = AccessibilityDocument.Create(profile).WithSettings(new DocumentSettings
                {
                    PdfUaConformance = PdfUaConformance.PdfUa1, PdfAConformance = profile,
                    EnableAdditionalCompression = mode is "compressed" or "optimized",
                    Linearize = mode is "linearized" or "optimized",
                    CompressDocument = mode != "uncompressed", QpdfExecutablePath = qpdf
                });
                var options = new PdfUaOptions
                {
                    Attachments = profile >= PdfAConformance.PdfA3A
                        ? new[] { new PdfAAttachment(Encoding.UTF8.GetBytes("<requests completed='12' awaiting='4'/>"),
                            new DocumentOperation.DocumentAttachment
                            {
                                AttachmentName = "requests.xml", MimeType = "application/xml",
                                Relationship = DocumentOperation.DocumentAttachmentRelationship.Data,
                                ModificationDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
                            }) }
                        : Array.Empty<PdfAAttachment>()
                };
                byte[] bytes;
                PdfUaValidationReport report;
                try { bytes = document.GeneratePdfUa(out report, options); }
                catch (PdfUaConformanceException exception)
                {
                    File.WriteAllText(Path.Combine(OutputDirectory, profile + "-" + mode + "-failed-ua1.json"), exception.Report.Json);
                    throw;
                }
                catch (PdfAConformanceException exception)
                {
                    File.WriteAllText(Path.Combine(OutputDirectory, profile + "-" + mode + "-failed-pdfa.json"), exception.Report.Json);
                    throw;
                }
                Assert.That(report.MachineChecksPassed, Is.True, report.Json);
                Assert.That(report.ArchivalReport?.IsCompliant, profile == PdfAConformance.None ? Is.Null : Is.True);
                var stem = Path.Combine(OutputDirectory, profile + "-" + mode);
                File.WriteAllBytes(stem + ".pdf", bytes);
                File.WriteAllText(stem + "-ua1.json", report.Json);
                if (report.ArchivalReport != null)
                    File.WriteAllText(stem + "-pdfa.json", report.ArchivalReport.Json);
            }
            // The reference uses the same content with both conformance settings disabled.
            File.WriteAllBytes(Path.Combine(OutputDirectory, "reference.pdf"), AccessibilityDocument.Create(PdfAConformance.None)
                .WithSettings(new DocumentSettings()).GeneratePdf());
        }

        [TestCase("title")]
        [TestCase("language")]
        public void MissingDocumentDeclarationsFailBeforeComposition(string missing)
        {
            var metadata = new DocumentMetadata { Title = "Title", Language = "en-US" };
            if (missing == "title") metadata.Title = null; else metadata.Language = null;
            var document = Document.Create(_ => Assert.Fail("Preflight must run before composition."))
                .WithMetadata(metadata).WithSettings(new DocumentSettings { PdfUaConformance = PdfUaConformance.PdfUa1 });
            using var sink = new MemoryStream(); sink.WriteByte(42);
            Assert.That(() => document.GeneratePdfUa(sink), Throws.ArgumentException.With.Message.Contains(missing == "title" ? "Title" : "Language"));
            Assert.That(sink.ToArray(), Is.EqualTo(new byte[] { 42 }));
        }

        [TestCase("figure")]
        [TestCase("formula")]
        [TestCase("link")]
        public void MissingAlternativesLeaveTheCallerStreamUntouched(string missing)
        {
            PdfATests.RequireValidator();
            var document = Document.Create(container => container.Page(page =>
            {
                if (missing == "figure") page.Content().SemanticFigure().Height(20).Background("#ff0000");
                else if (missing == "formula") page.Content().SemanticFormula().Text("x = 1");
                else page.Content().Hyperlink("https://example.org/").Text("Reference");
            })).WithMetadata(new DocumentMetadata { Title = "Title", Language = "en-US" })
                .WithSettings(new DocumentSettings { PdfUaConformance = PdfUaConformance.PdfUa1 });
            using var sink = new MemoryStream(); sink.WriteByte(42);
            Assert.That(() => document.GeneratePdfUa(sink), Throws.ArgumentException);
            Assert.That(sink.ToArray(), Is.EqualTo(new byte[] { 42 }));
        }

        [TestCase("en_US", false)]
        [TestCase("en-US\n", false)]
        [TestCase("x-review-fr", true)]
        [TestCase("i-klingon", true)]
        public void LanguagePreflightAcceptsBcp47FormsAndRejectsMalformedTags(string language, bool valid)
        {
            var document = Document.Create(_ => Assert.Fail("Preflight must run before composition."))
                .WithMetadata(new DocumentMetadata { Title = "Title", Language = language })
                .WithSettings(new DocumentSettings { PdfUaConformance = PdfUaConformance.PdfUa1 });
            var options = new PdfUaOptions { VeraPdfJarPath = "missing-" + Guid.NewGuid() };
            if (valid)
                Assert.Throws<NotSupportedException>(() => document.GeneratePdfUa(options));
            else
                Assert.That(() => document.GeneratePdfUa(options), Throws.ArgumentException.With.Message.Contains("BCP 47"));
        }

        [Test]
        public void OrdinaryGenerationRequiresTheExplicitPdfUaApiAndSelection()
        {
            var document = AccessibilityDocument.Create(PdfAConformance.None);
            Assert.That(() => document.GeneratePdf(), Throws.TypeOf<NotSupportedException>().With.Message.Contains("GeneratePdfUa"));
            document.WithSettings(new DocumentSettings());
            Assert.That(() => document.GeneratePdfUa(), Throws.ArgumentException);
            using var token = new CancellationTokenSource(); token.Cancel();
            document.WithSettings(new DocumentSettings { PdfUaConformance = PdfUaConformance.PdfUa1 });
            Assert.Throws<OperationCanceledException>(() => document.GeneratePdfUa(cancellationToken: token.Token));
        }

        [Test]
        public void ArchivalEntryPointAlsoValidatesCombinedAccessibility()
        {
            PdfATests.RequireValidator();
            var bytes = AccessibilityDocument.Create(PdfAConformance.PdfA2U).GeneratePdfA(out var archival);
            Assert.That(archival.IsCompliant, Is.True);
            var accessibility = PdfUaValidator.Validate(bytes);
            Assert.That(accessibility.MachineChecksPassed, Is.True, accessibility.Json);
            using var input = new MemoryStream(bytes, writable: false);
            using var pdf = PdfReader.Open(input, PdfDocumentOpenMode.Import);
            Assert.That(pdf.Internals.Catalog.Elements.GetDictionary("/StructTreeRoot"), Is.Not.Null);
        }

        [Test]
        public void InvalidCustomXmpWithholdsTheFileAndReturnsTheActualFailedProfile()
        {
            PdfATests.RequireValidator();
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, new byte[] { 42 });
                Assert.Throws<PdfAConformanceException>(() => AccessibilityDocument.Create(PdfAConformance.PdfA3A)
                    .GeneratePdfUa(path, new PdfUaOptions { MetadataExtensions = "<custom:Field xmlns:custom='urn:missing-schema'>value</custom:Field>" }));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 42 }));
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void CustomSchemasMergeWithoutReplacingPdfUaIdentification()
        {
            PdfATests.RequireValidator();
            const string extension = @"
<review:Code xmlns:review='urn:fosspdf:review'>checked</review:Code>
<pdfaExtension:schemas xmlns:pdfaExtension='http://www.aiim.org/pdfa/ns/extension/'
 xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'
 xmlns:pdfaSchema='http://www.aiim.org/pdfa/ns/schema#'
 xmlns:pdfaProperty='http://www.aiim.org/pdfa/ns/property#'>
 <rdf:Bag><rdf:li rdf:parseType='Resource'>
  <pdfaSchema:schema>Review metadata</pdfaSchema:schema>
  <pdfaSchema:namespaceURI>urn:fosspdf:review</pdfaSchema:namespaceURI>
  <pdfaSchema:prefix>review</pdfaSchema:prefix>
  <pdfaSchema:property><rdf:Seq><rdf:li rdf:parseType='Resource'>
   <pdfaProperty:name>Code</pdfaProperty:name><pdfaProperty:valueType>Text</pdfaProperty:valueType>
   <pdfaProperty:category>external</pdfaProperty:category>
   <pdfaProperty:description>Review code.</pdfaProperty:description>
  </rdf:li></rdf:Seq></pdfaSchema:property>
 </rdf:li></rdf:Bag>
</pdfaExtension:schemas>";
            var document = AccessibilityDocument.Create(PdfAConformance.PdfA3A);
            document.GeneratePdfUa(out var report, new PdfUaOptions { MetadataExtensions = extension });
            Assert.That(report.MachineChecksPassed, Is.True);
            Assert.That(report.ArchivalReport!.IsCompliant, Is.True);
            var collision = extension.Replace("urn:fosspdf:review", "http://www.aiim.org/pdfua/ns/id/");
            Assert.That(() => document.GeneratePdfUa(new PdfUaOptions { MetadataExtensions = collision }),
                Throws.ArgumentException.With.Message.Contains("duplicate namespace URI"));
        }
    }
}
#endif
