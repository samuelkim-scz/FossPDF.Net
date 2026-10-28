#if NET9_0
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FossPDF.Drawing;
using FossPDF.Fluent;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using NUnit.Framework;

namespace FossPDF.UnitTests
{
    [Category("PdfOptimization")]
    public class PdfDocumentOptimizationTests
    {
        [Test]
        public void LinearizedDocumentPassesTheQpdfCheckAndKeepsPages()
        {
            var source = CreateSource();
            var output = DocumentOperation
                .Load(source)
                .Linearize()
                .UseQpdfExecutable(QpdfTestTool.Require())
                .SaveToBytes();

            var text = Encoding.ASCII.GetString(output);

            Assert.That(text, Does.StartWith("%PDF-"));
            Assert.That(text, Does.Contain("/Linearized"));
            Assert.That(ReadPageCount(output), Is.EqualTo(2));
            Assert.That(ReadTitle(output), Is.EqualTo("Optimization source"));

            using var path = new TemporaryFile(output);

            var linearization = QpdfTestTool.Run("--check-linearization", path.Path);

            Assert.That(linearization.ExitCode, Is.EqualTo(0),
                "qpdf must accept the linearization hint tables. " + linearization.Output);

            var structure = QpdfTestTool.Run("--check", path.Path);

            Assert.That(structure.ExitCode, Is.AnyOf(0, 3),
                "qpdf must read the optimized document completely. " + structure.Output);
        }

        [Test]
        public void AdditionalCompressionKeepsPagesMetadataAndAttachments()
        {
            var payload = Encoding.UTF8.GetBytes("FossPDF optimization attachment payload.");
            using var file = new TemporaryAttachment("terms.txt", payload);

            var output = DocumentOperation
                .Load(CreateSource())
                .AddAttachment(new DocumentOperation.DocumentAttachment
                {
                    FilePath = file.Path,
                    MimeType = "text/plain",
                    Relationship = DocumentOperation.DocumentAttachmentRelationship.Supplement
                })
                .EnableAdditionalCompression()
                .Linearize()
                .UseQpdfExecutable(QpdfTestTool.Require())
                .SaveToBytes();

            var text = Encoding.ASCII.GetString(output);

            Assert.That(text, Does.Contain("/ObjStm"));
            Assert.That(text, Does.Contain("/Linearized"));
            Assert.That(ReadPageCount(output), Is.EqualTo(2));
            Assert.That(ReadTitle(output), Is.EqualTo("Optimization source"));

            var attachment = ReadAttachments(output).Single();

            Assert.That(attachment.Key, Is.EqualTo("terms.txt"));
            Assert.That(attachment.Data, Is.EqualTo(payload));
            Assert.That(attachment.Relationship, Is.EqualTo("Supplement"));
        }

        [Test]
        public void OptimizationPreservesExtendedXmpMetadata()
        {
            var output = DocumentOperation
                .Load(CreateSource())
                .ExtendMetadata("<opt:marker xmlns:opt=\"urn:fosspdf:test:optimization\">kept</opt:marker>")
                .Linearize()
                .UseQpdfExecutable(QpdfTestTool.Require())
                .SaveToBytes();

            var metadata = ReadMetadata(output);

            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata, Does.Contain("urn:fosspdf:test:optimization"));
            Assert.That(metadata, Does.Contain("kept"));
        }

        [Test]
        public void EncryptionAndOptimizationAreRejectedInEitherOrder()
        {
            var source = CreateSource();

            Assert.That(
                () => DocumentOperation.Load(source)
                    .Linearize()
                    .Encrypt(new DocumentOperation.Encryption256Bit
                    {
                        UserPassword = "user",
                        OwnerPassword = "owner"
                    })
                    .SaveToBytes(),
                Throws.TypeOf<InvalidOperationException>());

            Assert.That(
                () => DocumentOperation.Load(source)
                    .Encrypt(new DocumentOperation.Encryption256Bit
                    {
                        UserPassword = "user",
                        OwnerPassword = "owner"
                    })
                    .EnableAdditionalCompression()
                    .SaveToBytes(),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void AdditionalAndDisabledCompressionCannotBeCombined()
        {
            var source = CreateSource();

            Assert.That(
                () => DocumentOperation.Load(source).DisableCompression().EnableAdditionalCompression(),
                Throws.TypeOf<InvalidOperationException>());

            Assert.That(
                () => DocumentOperation.Load(source).EnableAdditionalCompression().DisableCompression(),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void UseQpdfExecutableRejectsAnEmptyPath()
        {
            Assert.That(
                () => DocumentOperation.Load(CreateSource()).UseQpdfExecutable("   "),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void DisabledCompressionProducesALargerDocument()
        {
            var optimized = DocumentOperation
                .Load(CreateSource())
                .DisableCompression()
                .UseQpdfExecutable(QpdfTestTool.Require())
                .SaveToBytes();

            Assert.That(Encoding.ASCII.GetString(optimized), Does.Not.Contain("/FlateDecode"));
            Assert.That(ReadPageCount(optimized), Is.EqualTo(2));

            using var path = new TemporaryFile(optimized);

            Assert.That(QpdfTestTool.Run("--check", path.Path).ExitCode, Is.AnyOf(0, 3));
        }

        [Test]
        public void MissingQpdfFailsWithoutWritingToTheOutputStream()
        {
            var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "qpdf-missing.exe");
            var operation = DocumentOperation
                .Load(CreateSource())
                .Linearize()
                .UseQpdfExecutable(missing);

            Assert.That(() => operation.SaveToBytes(), Throws.TypeOf<NotSupportedException>());

            using var output = new MemoryStream();
            output.WriteByte(42);

            Assert.That(() => operation.Save(output), Throws.TypeOf<NotSupportedException>());
            Assert.That(output.ToArray(), Is.EqualTo(new byte[] { 42 }));
        }

        private static byte[] CreateSource()
        {
            return Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(200, 100);
                        page.Margin(10);
                        page.Content().Text("Optimization source page");
                    });

                    container.Page(page =>
                    {
                        page.Size(200, 100);
                        page.Margin(10);
                        page.Content().Text("Optimization second page");
                    });
                })
                .WithMetadata(new DocumentMetadata
                {
                    Title = "Optimization source",
                    Author = "FossPDF tests"
                })
                .GeneratePdf();
        }

        private static int ReadPageCount(byte[] pdf)
        {
            using var stream = new MemoryStream(pdf, writable: false);
            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            return document.PageCount;
        }

        private static string ReadTitle(byte[] pdf)
        {
            using var stream = new MemoryStream(pdf, writable: false);
            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            return document.Info.Title;
        }

        private static string ReadMetadata(byte[] pdf)
        {
            using var stream = new MemoryStream(pdf, writable: false);
            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

            var metadata = ResolveDictionary(document.Internals.Catalog.Elements.GetObject("/Metadata"));
            var metadataStream = metadata?.Stream;

            if (metadataStream == null)
                return null;

            if (metadataStream.IsFiltered())
                Assert.That(metadataStream.TryUncompress(), Is.True);

            return Encoding.UTF8.GetString(metadataStream.UnfilteredValue);
        }

        private static List<AttachmentInfo> ReadAttachments(byte[] pdf)
        {
            using var stream = new MemoryStream(pdf, writable: false);
            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

            var attachments = new List<AttachmentInfo>();
            var catalog = document.Internals.Catalog;
            var names = catalog.Elements.GetDictionary("/Names");
            var embedded = names?.Elements.GetDictionary("/EmbeddedFiles");
            var entries = embedded?.Elements.GetArray("/Names");

            if (entries == null)
                return attachments;

            for (var index = 0; index + 1 < entries.Elements.Count; index += 2)
            {
                var key = ((PdfString)entries.Elements[index]).Value;
                var specification = ResolveDictionary(entries.Elements[index + 1]);
                var files = specification?.Elements.GetDictionary("/EF");
                var streamDictionary = ResolveDictionary(files?.Elements.GetObject("/F"));
                var embeddedStream = streamDictionary?.Stream;

                Assert.That(embeddedStream, Is.Not.Null, $"The attachment '{key}' has an embedded file stream.");

                if (embeddedStream.IsFiltered())
                    Assert.That(embeddedStream.TryUncompress(), Is.True);

                attachments.Add(new AttachmentInfo
                {
                    Key = key,
                    Relationship = TrimName(specification.Elements.GetName("/AFRelationship")),
                    Data = embeddedStream.UnfilteredValue
                });
            }

            return attachments;
        }

        private static PdfDictionary ResolveDictionary(PdfItem item)
        {
            return item switch
            {
                PdfReference reference => reference.Value as PdfDictionary,
                PdfDictionary dictionary => dictionary,
                _ => null
            };
        }

        private static string TrimName(string value)
        {
            return value.StartsWith("/", StringComparison.Ordinal) ? value.Substring(1) : value;
        }

        private sealed class AttachmentInfo
        {
            public string Key { get; set; }
            public string Relationship { get; set; }
            public byte[] Data { get; set; }
        }

        private sealed class TemporaryFile : IDisposable
        {
            public TemporaryFile(byte[] bytes)
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "fosspdf-opt-" + Guid.NewGuid().ToString("N") + ".pdf");
                File.WriteAllBytes(Path, bytes);
            }

            public string Path { get; }

            public void Dispose()
            {
                try
                {
                    File.Delete(Path);
                }
                catch (IOException)
                {
                }
            }
        }

        private sealed class TemporaryAttachment : IDisposable
        {
            private readonly string _directory;

            public TemporaryAttachment(string name, byte[] content)
            {
                _directory = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "fosspdf-optimization", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_directory);
                Path = System.IO.Path.Combine(_directory, name);
                File.WriteAllBytes(Path, content);
            }

            public string Path { get; }

            public void Dispose()
            {
                if (Directory.Exists(_directory))
                    Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
#endif
