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
    public class EInvoiceTests
    {
        internal static byte[] InvoiceXml() => File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "Resources", "EInvoice", "en16931.xml"));

        internal static void RequireValidators()
        {
            PdfATests.RequireValidator();
            if (!File.Exists(Environment.GetEnvironmentVariable("FOSSPDF_MUSTANG_JAR")))
                Assert.Ignore("Install Mustang 2.26.0 and set FOSSPDF_MUSTANG_JAR; CI requires this coverage.");
        }

        internal static Document CreateDocument() => Document.Create(container => container.Page(page =>
        {
            page.Size(420, 420); page.Margin(24);
            page.Content().Column(column =>
            {
                column.Item().SemanticHeading1().Text("Invoice FP-2026-001");
                column.Item().Text("Seller: FossPDF Example Seller GmbH, Example Street 1, 10115 Berlin, DE");
                column.Item().Text("VAT ID: DE123456789");
                column.Item().Text("Buyer: Example Buyer GmbH, Example Square 2, 20095 Hamburg, DE");
                column.Item().Text("Invoice and delivery date: 2026-10-29");
                column.Item().Text("Report preparation: 2 x EUR 6.00 = EUR 12.00 net");
                column.Item().Text("VAT 19%: EUR 2.28. Total due: EUR 14.28");
                column.Item().Text("Bank transfer to DE12500105170648489890 by 2026-11-29");
            });
        })).WithMetadata(new DocumentMetadata
        {
            Title = "Invoice FP-2026-001", Language = "en-US", Author = "FossPDF Example Seller GmbH",
            CreationDate = new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc),
            ModifiedDate = new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc)
        }).WithSettings(new DocumentSettings { PdfAConformance = PdfAConformance.PdfA3B });

        [Test]
        public void UnsupportedProfilesAndUnsafeXmlFailBeforeCompositionAndLeaveSinkUntouched()
        {
            var composed = false;
            var document = Document.Create(_ => composed = true)
                .WithSettings(new DocumentSettings { PdfAConformance = PdfAConformance.PdfA3B });
            using var sink = new MemoryStream(); sink.WriteByte(42);
            Assert.Throws<ArgumentOutOfRangeException>(() => document.GenerateEInvoice(InvoiceXml(), (EInvoiceProfile)999, sink));
            foreach (var xml in new[] { "<invalid>", "<!DOCTYPE a [<!ENTITY x SYSTEM 'file:///secret'>]><a>&x;</a>",
                "<a/>", Encoding.UTF8.GetString(InvoiceXml()).Replace(EInvoicePackaging.Guideline, "urn:unsupported:basic") })
                Assert.Throws<ArgumentException>(() => document.GenerateEInvoice(Encoding.UTF8.GetBytes(xml), EInvoiceProfile.FacturX1092En16931, sink));
            Assert.Throws<ArgumentException>(() => document.GenerateEInvoice(new byte[] { 0xff }, EInvoiceProfile.FacturX1092En16931, sink));
            document.WithSettings(new DocumentSettings { PdfAConformance = PdfAConformance.PdfA2B });
            Assert.Throws<ArgumentException>(() => document.GenerateEInvoice(InvoiceXml(), EInvoiceProfile.FacturX1092En16931, sink));
            Assert.That(composed, Is.False);
            Assert.That(sink.ToArray(), Is.EqualTo(new byte[] { 42 }));
        }

        [Test]
        public void ReservedNamesMetadataAndMissingToolsFailBeforeComposition()
        {
            var document = Document.Create(_ => Assert.Fail("Must not compose."))
                .WithSettings(new DocumentSettings { PdfAConformance = PdfAConformance.PdfA3B });
            var attachment = new PdfAAttachment(new byte[] { 42 }, new DocumentOperation.DocumentAttachment
                { AttachmentName = "FACTUR-X.XML", ModificationDate = DateTime.UtcNow });
            Assert.Throws<ArgumentException>(() => document.GenerateEInvoice(InvoiceXml(), EInvoiceProfile.FacturX1092En16931,
                new EInvoiceOptions { Attachments = new[] { attachment } }));
            Assert.Throws<ArgumentException>(() => document.GenerateEInvoice(InvoiceXml(), EInvoiceProfile.FacturX1092En16931,
                new EInvoiceOptions { MetadataExtensions = "<fx:Version xmlns:fx='" + EInvoicePackaging.InvoiceNamespace + "'>fake</fx:Version>" }));
            Assert.Throws<NotSupportedException>(() => document.GenerateEInvoice(InvoiceXml(), EInvoiceProfile.FacturX1092En16931,
                new EInvoiceOptions { VeraPdfJarPath = "missing-verapdf" }));
            Assert.Throws<NotSupportedException>(() => MustangValidator.Prepare(new EInvoiceOptions { MustangJarPath = "missing-mustang" }, default));
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "unqualified JAR");
                Assert.Throws<NotSupportedException>(() => MustangValidator.Prepare(new EInvoiceOptions { MustangJarPath = path }, default));
            }
            finally { File.Delete(path); }
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() => MustangValidator.Prepare(new EInvoiceOptions(), cancelled.Token));
        }

        [Test]
        public void SchemaAndBusinessRuleFailuresWithholdOutputAndRetainDiagnostics()
        {
            RequireValidators();
            foreach (var invalid in new[]
            {
                Encoding.UTF8.GetString(InvoiceXml()).Replace("<ram:InvoiceCurrencyCode>EUR", "<ram:InvoiceCurrencyCode>INVALID"),
                Encoding.UTF8.GetString(InvoiceXml()).Replace("<ram:GrandTotalAmount>14.28", "<ram:GrandTotalAmount>99.99")
            })
            {
                var document = Document.Create(_ => Assert.Fail("Invalid XML must fail before composition."))
                    .WithSettings(new DocumentSettings { PdfAConformance = PdfAConformance.PdfA3B });
                using var sink = new MemoryStream(); sink.WriteByte(42);
                var exception = Assert.Throws<EInvoiceConformanceException>(() => document.GenerateEInvoice(
                    Encoding.UTF8.GetBytes(invalid), EInvoiceProfile.FacturX1092En16931, sink));
                Assert.That(exception!.Report.IsValid, Is.False);
                Assert.That(exception.Report.Xml, Does.Contain("error"));
                Assert.That(exception.Report.IsPackagedPdf, Is.False);
                Assert.That(sink.ToArray(), Is.EqualTo(new byte[] { 42 }));
            }
        }

        [Test]
        public void PayloadIsCapturedBeforeCompositionAndCallerSettingsArePreserved()
        {
            RequireValidators();
            var original = InvoiceXml(); var callerBytes = (byte[])original.Clone();
            var document = Document.Create(container =>
            {
                Array.Fill(callerBytes, (byte)42);
                container.Page(page => page.Content().Text("Invoice FP-2026-001"));
            }).WithSettings(new DocumentSettings { PdfAConformance = PdfAConformance.PdfA3U });
            var settings = ((IDocumentSettingsProvider)document).GetSettings(); var metadata = document.GetMetadata();
            var bytes = document.GenerateEInvoice(callerBytes, EInvoiceProfile.Zugferd252En16931, out var report);
            Assert.That(report.PackageReport.IsValid, Is.True, report.PackageReport.Xml);
            Assert.That(report.InvoiceXmlReport.Xml, Does.Contain("\u00fcbermittelt"), "Preserve UTF-8 validator diagnostics on Windows.");
            using var stream = new MemoryStream(bytes, false); using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
            var file = pdf.Internals.Catalog.Elements.GetArray("/AF")!.Elements.GetDictionary(0)!;
            Assert.That(PdfXmpMetadata.ReadStream(file.Elements.GetDictionary("/EF")!.Elements.GetDictionary("/F")!), Is.EqualTo(original));
            Assert.That(((IDocumentSettingsProvider)document).GetSettings(), Is.SameAs(settings));
            Assert.That(document.GetMetadata(), Is.SameAs(metadata));
            file.Elements.SetName("/AFRelationship", "/Unspecified");
            using var changed = new MemoryStream(); pdf.Save(changed, false);
            var invalid = PdfXmpMetadata.PreserveAndExtend(changed.ToArray(), pdf, pdf,
                Array.Empty<System.Xml.Linq.XElement>(), false, false);
            Assert.That(() => EInvoicePackaging.ValidateOutput(invalid, original),
                Throws.TypeOf<PdfOperationException>().With.Message.Contains("relationship"));
            using var restoredInput = new MemoryStream(bytes, false);
            using var restoredPdf = PdfReader.Open(restoredInput, PdfDocumentOpenMode.Modify);
            using var restored = new MemoryStream(); restoredPdf.Save(restored, false);
            var extensions = PdfXmpMetadata.ParseExtension(EInvoicePackaging.Metadata(null).Replace("EN 16931", "BASIC"));
            invalid = PdfXmpMetadata.ApplyArchivalIdentification(restored.ToArray(), restoredPdf, "3u", extensions);
            Assert.That(() => EInvoicePackaging.ValidateOutput(invalid, original),
                Throws.TypeOf<PdfOperationException>().With.Message.Contains("ConformanceLevel"));
        }

        [Test]
        public void ReportsRejectMissingChecksAndCannotHidePdfFailures()
        {
            Assert.Throws<PdfOperationException>(() => new EInvoiceValidationReport("<validation><summary status='valid'/></validation>", false));
            var info = "<info><version>2</version><profile>urn:cen.eu:en16931:2017</profile><validator version='2.26.0'/><rules><fired>15</fired></rules></info>";
            var report = new EInvoiceValidationReport("<validation><pdf><summary status='invalid'/></pdf><xml>" + info +
                "<summary status='valid'/></xml><summary status='valid'/></validation>", true);
            Assert.That(report.IsValid, Is.False);
        }

        [Test]
        public void BorrowedNonseekingStreamStaysOpenAndFailedFileIsNotReplaced()
        {
            RequireValidators();
            using var sink = new NonseekingOutput(); sink.WriteByte(42);
            var javaName = OperatingSystem.IsWindows() ? "java.exe" : "java";
            var java = Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator)
                .Select(directory => Path.Combine(directory, javaName)).First(File.Exists);
            CreateDocument().GenerateEInvoice(InvoiceXml(), EInvoiceProfile.FacturX1092En16931, sink,
                new EInvoiceOptions { JavaExecutablePath = Path.GetRelativePath(Environment.CurrentDirectory, java) });
            Assert.That(sink.CanWrite, Is.True);
            Assert.That(sink.ToArray().Take(6), Is.EqualTo(new byte[] { 42, 37, 80, 68, 70, 45 }));
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, new byte[] { 42 });
                var invalid = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(InvoiceXml())
                    .Replace("<ram:GrandTotalAmount>14.28", "<ram:GrandTotalAmount>99.99"));
                Assert.Throws<EInvoiceConformanceException>(() => CreateDocument().GenerateEInvoice(invalid, EInvoiceProfile.FacturX1092En16931, path));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 42 }));
            }
            finally { File.Delete(path); }
        }

        private sealed class NonseekingOutput : MemoryStream
        {
            public override bool CanSeek => false;
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        }
    }
}
#endif
