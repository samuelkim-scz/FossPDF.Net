#if NET9_0
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.PdfOperations;
using NUnit.Framework;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace FossPDF.UnitTests
{
    [TestFixture]
    [NonParallelizable]
    public class PdfAttachmentTests
    {
        private static readonly byte[] Payload = Encoding.UTF8.GetBytes("invoice data: \u03b1\u03b2\u03b3");
        private static DocumentOperation.DocumentAttachment Options(string name = "invoice.xml") => new()
        {
            AttachmentName = name,
            Key = "invoice-data",
            MimeType = "application/xml",
            Description = "Supporting invoice \u03b1",
            CreationDate = new DateTime(2000, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            ModificationDate = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc),
            Relationship = DocumentOperation.DocumentAttachmentRelationship.Data
        };

        [TestCase(DocumentOperation.DocumentAttachmentRelationship.Data)]
        [TestCase(DocumentOperation.DocumentAttachmentRelationship.Source)]
        [TestCase(DocumentOperation.DocumentAttachmentRelationship.Alternative)]
        [TestCase(DocumentOperation.DocumentAttachmentRelationship.Supplement)]
        [TestCase(DocumentOperation.DocumentAttachmentRelationship.Unspecified)]
        public void Attachment_PreservesBytesUnicodeMetadataAndAssociation(DocumentOperation.DocumentAttachmentRelationship relationship)
        {
            var options = Options("\u8acb\u6c42\u66f8-\u03b1.xml");
            options.Relationship = relationship;
            var result = DocumentOperation.Load(Source()).AddAttachment(Payload, options).SaveToBytes();
            using var input = new MemoryStream(result);
            using var document = PdfReader.Open(input, PdfDocumentOpenMode.Import);
            var specification = Specifications(document).Single();
            var stream = specification.Elements.GetDictionary("/EF").Elements.GetDictionary("/UF");
            Assert.That(PdfXmpMetadata.ReadStream(stream), Is.EqualTo(Payload));
            Assert.That(specification.Elements.GetString("/UF"), Is.EqualTo(options.AttachmentName));
            Assert.That(specification.Elements.GetString("/Desc"), Is.EqualTo(options.Description));
            Assert.That(specification.Elements.GetName("/AFRelationship"), Is.EqualTo("/" + relationship));
            Assert.That(stream.Elements.GetName("/Subtype"), Is.EqualTo("/application/xml"));
            var parameters = stream.Elements.GetDictionary("/Params");
            Assert.That(parameters.Elements.GetInteger("/Size"), Is.EqualTo(Payload.Length));
            Assert.That(parameters.Elements.GetDateTime("/CreationDate", DateTime.MinValue).ToUniversalTime(), Is.EqualTo(options.CreationDate));
            var associated = document.Internals.Catalog.Elements.GetArray("/AF");
            Assert.That(associated.Elements.Count, Is.EqualTo(1));
            Assert.That(((PdfReference)associated.Elements[0]).Value, Is.SameAs(specification));
        }

        [Test]
        public void Attachment_SnapshotsBytesAndConfiguration()
        {
            var bytes = Payload.ToArray();
            var options = Options();
            var operation = DocumentOperation.Load(Source()).AddAttachment(bytes, options);
            bytes[0] = 0;
            options.AttachmentName = "changed.txt";
            options.Description = "changed";
            var result = operation.SaveToBytes();
            Assert.That(Extract(result), Is.EqualTo(Payload));
            Assert.That(Encoding.Latin1.GetString(result), Does.Not.Contain("changed.txt"));
            Assert.That(operation.SaveToBytes(), Is.Not.Empty, "An operation may be saved more than once.");
        }

        [Test]
        public void Attachment_ReadsBorrowedStreamFromCurrentPositionAndLeavesItOpen()
        {
            using var stream = new MemoryStream(new byte[] { 0, 0 }.Concat(Payload).ToArray());
            stream.Position = 2;
            var operation = DocumentOperation.Load(Source()).AddAttachment(stream, Options());
            Assert.That(stream.CanRead, Is.True);
            Assert.That(stream.Position, Is.EqualTo(stream.Length));
            stream.Dispose();
            Assert.That(Extract(operation.SaveToBytes()), Is.EqualTo(Payload));
        }

        [Test]
        public void Attachment_FileIsReadImmediatelyAndDefaultsToItsFileName()
        {
            var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid() + ".bin");
            try
            {
                File.WriteAllBytes(path, Payload);
                var operation = DocumentOperation.Load(Source()).AddAttachment(new DocumentOperation.DocumentAttachment { FilePath = path });
                var renamed = DocumentOperation.Load(Source()).AddAttachment(new DocumentOperation.DocumentAttachment
                    { FilePath = path, AttachmentName = "renamed.bin" });
                File.Delete(path);
                using var input = new MemoryStream(operation.SaveToBytes());
                using var document = PdfReader.Open(input, PdfDocumentOpenMode.Import);
                Assert.That(Specifications(document).Single().Elements.GetString("/UF"), Is.EqualTo(Path.GetFileName(path)));
                using var renamedInput = new MemoryStream(renamed.SaveToBytes());
                using var renamedDocument = PdfReader.Open(renamedInput, PdfDocumentOpenMode.Import);
                var names = renamedDocument.Internals.Catalog.Elements.GetDictionary("/Names")
                    .Elements.GetDictionary("/EmbeddedFiles").Elements.GetArray("/Names");
                Assert.That(((PdfString)names.Elements[0]).Value, Is.EqualTo(Path.GetFileName(path)),
                    "Renaming the display name does not change the default file-based replacement key.");
                Assert.That(Specifications(renamedDocument).Single().Elements.GetString("/UF"), Is.EqualTo("renamed.bin"));
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void Attachment_EmptyFilesAreValidAndInvalidOptionsFailBeforeSaving()
        {
            Assert.That(Extract(DocumentOperation.Load(Source()).AddAttachment(Array.Empty<byte>(), Options()).SaveToBytes()), Is.Empty);
            var options = Options();
            options.MimeType = "invalid MIME";
            Assert.Throws<ArgumentException>(() => DocumentOperation.Load(Source()).AddAttachment(Payload, options));
            Assert.Throws<ArgumentException>(() => DocumentOperation.Load(Source()).AddAttachment(Payload, new DocumentOperation.DocumentAttachment()));
            options = Options();
            options.Relationship = (DocumentOperation.DocumentAttachmentRelationship)999;
            Assert.Throws<ArgumentOutOfRangeException>(() => DocumentOperation.Load(Source()).AddAttachment(Payload, options));
        }

        [Test]
        public void Attachment_ExistingKeysRequireReplaceAndPreserveOtherFiles()
        {
            var first = DocumentOperation.Load(Source()).AddAttachment(Payload, Options()).SaveToBytes();
            var duplicate = DocumentOperation.Load(first).AddAttachment(new byte[] { 1, 2, 3 }, Options());
            using var output = new MemoryStream(new byte[] { 7, 8, 9 });
            Assert.Throws<PdfOperationException>(() => duplicate.Save(output));
            Assert.That(output.ToArray(), Is.EqualTo(new byte[] { 7, 8, 9 }));
            var replacement = Options();
            replacement.Replace = true;
            var result = DocumentOperation.Load(first).AddAttachment(new byte[] { 1, 2, 3 }, replacement)
                .AddAttachment(Payload, new DocumentOperation.DocumentAttachment { AttachmentName = "second.bin", Key = "second" }).SaveToBytes();
            Assert.That(Extract(result), Is.EqualTo(new byte[] { 1, 2, 3 }));
            using var input = new MemoryStream(result);
            using var document = PdfReader.Open(input, PdfDocumentOpenMode.Import);
            Assert.That(Specifications(document).Length, Is.EqualTo(2));
            Assert.That(document.Internals.Catalog.Elements.GetArray("/AF").Elements.Count, Is.EqualTo(2));
        }

        [Test]
        public void Metadata_ExtensionsPreserveExistingPropertiesNamespacesAndInfo()
        {
            var first = DocumentOperation.Load(Source()).ExtendMetadata("<old:Value xmlns:old='urn:old'>original &amp; safe</old:Value>").SaveToBytes();
            var result = DocumentOperation.Load(first).ExtendMetadata("<new:Value xmlns:new='urn:new'>added</new:Value>")
                .AddAttachment(Payload, Options()).SaveToBytes();
            var xml = Metadata(result);
            Assert.That(xml.Descendants(XName.Get("Value", "urn:old")).Single().Value, Is.EqualTo("original & safe"));
            Assert.That(xml.Descendants(XName.Get("Value", "urn:new")).Single().Value, Is.EqualTo("added"));
            using var input = new MemoryStream(result);
            using var document = PdfReader.Open(input, PdfDocumentOpenMode.Import);
            Assert.That(document.Info.Title, Is.EqualTo("Attachment source"));
            Assert.That(Extract(result), Is.EqualTo(Payload));
            Assert.Throws<PdfOperationException>(() => DocumentOperation.Load(first)
                .ExtendMetadata("<other:Value xmlns:other='urn:old'>duplicate</other:Value>").SaveToBytes());
        }

        [Test]
        public void Attachment_DistinctKeysCanShareTheSameDisplayName()
        {
            var first = Options();
            var second = Options();
            second.Key = "another-key";
            var result = DocumentOperation.Load(Source()).AddAttachment(Payload, first).AddAttachment(Payload, second).SaveToBytes();
            using var input = new MemoryStream(result);
            using var document = PdfReader.Open(input, PdfDocumentOpenMode.Import);
            Assert.That(Specifications(document).Select(x => x.Elements.GetString("/UF")), Is.EqualTo(new[] { "invoice.xml", "invoice.xml" }));
        }

        [Test]
        public void MalformedEmbeddedFileTree_FailsWithoutWritingOutput()
        {
            using var source = new PdfDocument();
            source.AddPage();
            var tree = new PdfDictionary(source);
            var entries = new PdfArray(source);
            entries.Elements.Add(new PdfString("incomplete-key"));
            tree.Elements.SetObject("/Names", entries);
            var names = new PdfDictionary(source);
            names.Elements.SetObject("/EmbeddedFiles", tree);
            source.Internals.Catalog.Elements.SetObject("/Names", names);
            using var bytes = new MemoryStream();
            source.Save(bytes, false);
            using var output = new MemoryStream(new byte[] { 1, 2, 3 });
            Assert.Throws<PdfOperationException>(() => DocumentOperation.Load(bytes.ToArray()).AddAttachment(Payload, Options()).Save(output));
            Assert.That(output.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void MalformedExistingXmp_FailsClearly()
        {
            using var source = new PdfDocument();
            source.AddPage();
            using var bytes = new MemoryStream();
            source.Save(bytes, false);
            var text = Encoding.Latin1.GetString(bytes.ToArray());
            Assert.That(text, Does.Contain("<x:xmpmeta"));
            var invalid = new Regex("<x:xmpmeta").Replace(text, match => "!" + match.Value.Substring(1), 1);
            Assert.Throws<PdfOperationException>(() => DocumentOperation.Load(Encoding.Latin1.GetBytes(invalid))
                .ExtendMetadata("<a:Value xmlns:a='urn:valid'>value</a:Value>").SaveToBytes());
        }

        [Test]
        public void Metadata_RejectsMalformedUnqualifiedOrDuplicateProperties()
        {
            foreach (var xml in new[] { "", "<unqualified/>", "<bad:unbound/>", "<!DOCTYPE foo><foo/>", "<x:broken xmlns:x='urn:test'>", "text" })
                Assert.Throws<ArgumentException>(() => DocumentOperation.Load(Source()).ExtendMetadata(xml));
            var operation = DocumentOperation.Load(Source())
                .ExtendMetadata("<a:Value xmlns:a='urn:test'>one</a:Value>")
                .ExtendMetadata("<b:Value xmlns:b='urn:test'>two</b:Value>");
            Assert.Throws<PdfOperationException>(() => operation.SaveToBytes());
        }

        [Test]
        public void Metadata_CreatesEscapedXmpWhenTheSourceHasNone()
        {
            var bytes = Source();
            var withoutMetadata = Encoding.Latin1.GetBytes(Regex.Replace(Encoding.Latin1.GetString(bytes),
                @"/Metadata\s+\d+\s+\d+\s+R", match => new string(' ', match.Length)));
            var result = DocumentOperation.Load(withoutMetadata).ExtendMetadata("<a:Value xmlns:a='urn:test'>safe &amp; sound</a:Value>").SaveToBytes();
            Assert.That(Metadata(result).Descendants(XName.Get("Value", "urn:test")).Single().Value, Is.EqualTo("safe & sound"));
        }

        [TestCase(40, true)]
        [TestCase(128, true)]
        [TestCase(128, false)]
        [TestCase(256, true)]
        [TestCase(256, false)]
        public void Packaging_CombinesWithEncryptionWithoutLeakingMetadata(int strength, bool encryptMetadata)
        {
            var operation = DocumentOperation.Load(Source()).AddAttachment(Payload, Options())
                .ExtendMetadata("<a:Secret xmlns:a='urn:packing'>private-extension-value</a:Secret>");
            const string user = "fixture-reader";
            const string owner = "fixture-owner";
            if (strength == 40)
                operation.Encrypt(new DocumentOperation.Encryption40Bit { UserPassword = user, OwnerPassword = owner });
            else if (strength == 128)
                operation.Encrypt(new DocumentOperation.Encryption128Bit { UserPassword = user, OwnerPassword = owner, EncryptMetadata = encryptMetadata });
            else
                operation.Encrypt(new DocumentOperation.Encryption256Bit { UserPassword = user, OwnerPassword = owner, EncryptMetadata = encryptMetadata });
            var result = operation.SaveToBytes();
            Assert.That(Encoding.Latin1.GetString(result).Contains("private-extension-value"), Is.EqualTo(!encryptMetadata));
            Assert.That(Metadata(result, user).Descendants(XName.Get("Secret", "urn:packing")).Single().Value, Is.EqualTo("private-extension-value"));
            var decrypted = DocumentOperation.Load(result, owner).Decrypt().SaveToBytes();
            Assert.That(Extract(decrypted), Is.EqualTo(Payload));
            Assert.That(Metadata(decrypted).Descendants(XName.Get("Secret", "urn:packing")).Single().Value, Is.EqualTo("private-extension-value"));
            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "parity-artifacts", "PDF-305");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, $"encrypted-{strength}-{encryptMetadata}.pdf"), result);
        }

        private static byte[] Source() => Document.Create(container => container.Page(page =>
            page.Content().Text("Attachment source"))).WithMetadata(new DocumentMetadata { Title = "Attachment source" }).GeneratePdf();

        private static PdfDictionary[] Specifications(PdfDocument document)
        {
            var tree = document.Internals.Catalog.Elements.GetDictionary("/Names").Elements.GetDictionary("/EmbeddedFiles");
            var names = tree.Elements.GetArray("/Names");
            return Enumerable.Range(0, names.Elements.Count / 2)
                .Select(index => (PdfDictionary)((PdfReference)names.Elements[index * 2 + 1]).Value).ToArray();
        }

        private static byte[] Extract(byte[] pdf)
        {
            using var input = new MemoryStream(pdf);
            using var document = PdfReader.Open(input, PdfDocumentOpenMode.Import);
            return PdfXmpMetadata.ReadStream(Specifications(document).First().Elements.GetDictionary("/EF").Elements.GetDictionary("/UF"));
        }

        private static XDocument Metadata(byte[] pdf, string password = null)
        {
            using var input = new MemoryStream(pdf);
            using var document = password == null ? PdfReader.Open(input, PdfDocumentOpenMode.Import)
                : PdfReader.Open(input, password, PdfDocumentOpenMode.Import);
            var bytes = PdfXmpMetadata.ReadStream(document.Internals.Catalog.Elements.GetDictionary("/Metadata"));
            using var xml = new MemoryStream(bytes);
            return XDocument.Load(xml);
        }
    }
}
#endif
