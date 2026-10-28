#if NET9_0
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.PdfOperations;
using NUnit.Framework;

namespace FossPDF.UnitTests
{
    [NonParallelizable]
    public class PdfATests
    {
        internal static void RequireValidator()
        {
            if (!File.Exists(Environment.GetEnvironmentVariable("FOSSPDF_VERAPDF_JAR")))
                Assert.Ignore("Install veraPDF 1.30.3 and set FOSSPDF_VERAPDF_JAR; CI requires this coverage.");
        }

        internal static DocumentMetadata Metadata(bool legacy = false) => new()
        {
            PdfA = legacy, Title = "Archival quarterly report", Author = "FossPDF", Language = "en-US",
            CreationDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        internal static Document CreateDocument() => Document.Create(container => container.Page(page =>
        {
            page.Size(320, 240);
            page.Margin(20);
            page.Header().Text("Quarterly report");
            page.Content().Column(column =>
            {
                column.Item().SemanticHeading1().Text("Archival quarterly report");
                column.Item().Text("Unicode: caf\u00e9, r\u00e9sum\u00e9, office.");
                column.Item().Height(20).SemanticFigure("A red band.").Background("#ff0000");
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns => { columns.RelativeColumn(); columns.RelativeColumn(); });
                    table.Header(header => { header.Cell().Text("Item"); header.Cell().Text("Amount"); });
                    table.Cell().Text("Revenue"); table.Cell().Text("12");
                });
                column.Item().Hyperlink("https://example.org/archive").Text("External reference");
            });
        })).WithMetadata(Metadata());

        [Test]
        public void InvalidProfilesAndMissingToolsLeaveTheSinkUntouched()
        {
            var composed = false;
            var document = Document.Create(_ => composed = true);
            using var sink = new MemoryStream();
            sink.WriteByte(42);
            Assert.Throws<ArgumentOutOfRangeException>(() => document.GeneratePdfA(sink));
            document.WithSettings(new DocumentSettings { PdfAConformance = PdfAConformance.PdfA2B });
            Assert.Throws<NotSupportedException>(() => document.GeneratePdfA(sink,
                new PdfAOptions { VeraPdfJarPath = "missing-verapdf-" + Guid.NewGuid() }));
            Assert.That(composed, Is.False);
            Assert.That(sink.ToArray(), Is.EqualTo(new byte[] { 42 }));
        }

        [Test]
        public void IncompatibleSettingsAndAssociatedFilesFailBeforeComposition()
        {
            var document = Document.Create(_ => Assert.Fail("Must fail before composition."))
                .WithSettings(new DocumentSettings { PdfAConformance = PdfAConformance.PdfA2B });
            var metadata = new DocumentOperation.DocumentAttachment { AttachmentName = "data.xml", ModificationDate = DateTime.UtcNow };
            var options = new PdfAOptions { Attachments = new[] { new PdfAAttachment(new byte[] { 42 }, metadata) } };
            Assert.Throws<NotSupportedException>(() => document.GeneratePdfA(options));
            document.WithSettings(new DocumentSettings { PdfAConformance = PdfAConformance.PdfA3A, PdfUaConformance = PdfUaConformance.PdfUa1 });
            Assert.Throws<NotSupportedException>(() => document.GeneratePdfA());
            metadata.ModificationDate = null;
            Assert.Throws<ArgumentException>(() => new PdfAAttachment(new byte[] { 42 }, metadata));
        }

        [Test]
        public void MissingGlyphsCannotBePublishedAsArchivalOutput()
        {
            RequireValidator();
            var document = Document.Create(container => container.Page(page => page.Content().Text("\U0010FFFF")))
                .WithSettings(new DocumentSettings { PdfAConformance = PdfAConformance.PdfA2U, CheckIfAllTextGlyphsAreAvailable = false });
            using var sink = new MemoryStream();
            sink.WriteByte(42);
            Assert.That(() => document.GeneratePdfA(sink), Throws.Exception);
            Assert.That(sink.ToArray(), Is.EqualTo(new byte[] { 42 }));
        }

        [Test]
        public void ArchivalGenerationPreservesCallerMetadataSettingsAndContentPositions()
        {
            RequireValidator();
            var metadata = Metadata();
            var settings = new DocumentSettings { PdfAConformance = PdfAConformance.PdfA2B };
            var document = Document.Create(container => container.Page(page =>
                page.Content().CaptureContentPosition("paragraph").Text("Positioned text")))
                .WithMetadata(metadata).WithSettings(settings);
            document.GeneratePdfA();
            Assert.That(document.GetMetadata(), Is.SameAs(metadata));
            Assert.That(metadata.PdfA, Is.False);
            Assert.That(((IDocumentSettingsProvider)document).GetSettings(), Is.SameAs(settings));
            Assert.That(document.ContentPositions, Has.Count.EqualTo(1));
        }

        [Test]
        public void InvalidCustomMetadataReturnsRulesAndDoesNotReplaceAFile()
        {
            RequireValidator();
            var path = Path.Combine(Path.GetTempPath(), "fosspdf-pdfa-sink-" + Guid.NewGuid() + ".pdf");
            var original = new byte[] { 42 };
            File.WriteAllBytes(path, original);
            try
            {
                var document = CreateDocument().WithSettings(new DocumentSettings { PdfAConformance = PdfAConformance.PdfA2B });
                var exception = Assert.Throws<PdfAConformanceException>(() => document.GeneratePdfA(path,
                    new PdfAOptions { MetadataExtensions = "<custom:Value xmlns:custom='urn:undeclared-schema'>x</custom:Value>" }));
                Assert.That(exception!.Report.IsCompliant, Is.False);
                Assert.That(exception.Report.FailedRules, Is.GreaterThan(0));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
            }
            finally { File.Delete(path); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ValidatorStopsAWaitingProcess(bool cancel)
        {
            var start = OperatingSystem.IsWindows()
                ? new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
                : new ProcessStartInfo("/bin/sh");
            var arguments = OperatingSystem.IsWindows()
                ? new[] { "-NoProfile", "-NonInteractive", "-Command", "[System.Threading.Thread]::Sleep(30000)" }
                : new[] { "-c", "exec sleep 30" };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var cancellation = new CancellationTokenSource();
            if (cancel) cancellation.CancelAfter(250);
            var elapsed = Stopwatch.StartNew();
            if (cancel)
                Assert.Throws<OperationCanceledException>(() => PdfAValidator.RunProcess(start, TimeSpan.FromSeconds(10), cancellation.Token));
            else
                Assert.Throws<PdfOperationException>(() => PdfAValidator.RunProcess(start, TimeSpan.FromMilliseconds(250), cancellation.Token));
            Assert.That(elapsed.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        }

        [Test]
        public void AttachmentSnapshotsAndJarPinCannotBeChangedByCallerMutation()
        {
            var metadata = new DocumentOperation.DocumentAttachment { AttachmentName = "original", ModificationDate = DateTime.UtcNow };
            var bytes = new byte[] { 42 };
            var attachment = new PdfAAttachment(bytes, metadata);
            bytes[0] = 7;
            metadata.AttachmentName = "changed";
            Assert.That(attachment.Input.Data, Is.EqualTo(new byte[] { 42 }));
            Assert.That(attachment.Input.Name, Is.EqualTo("original"));
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "unqualified jar");
                Assert.Throws<NotSupportedException>(() => PdfAValidator.Validate(new byte[] { 42 }, PdfAConformance.PdfA2B,
                    new PdfAOptions { VeraPdfJarPath = path }));
            }
            finally { File.Delete(path); }
        }
    }
}
#endif
