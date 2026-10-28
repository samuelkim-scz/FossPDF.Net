#if NET9_0
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.PdfOperations;
using NUnit.Framework;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace FossPDF.UnitTests
{
    /// <summary>
    /// FP-305: embedded attachments, associated-file relationships and XMP metadata extensions for
    /// existing-PDF operations. The operations companion targets net9.0, so the whole fixture is
    /// compiled only for that framework.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class PdfAttachmentTests
    {
        private const string RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        private const string TestNamespace = "urn:fosspdf:tests:attachments";

        [Test]
        public void AddAttachment_EmbedsExactBytesAndDeclaresNameTypeAndRelationship()
        {
            var data = Encoding.UTF8.GetBytes("FossPDF parity fixture\n");

            using var file = new TemporaryAttachment("readme.txt", data);

            var result = DocumentOperation
                .Load(CreateSource())
                .AddAttachment(new DocumentOperation.DocumentAttachment
                {
                    FilePath = file.Path,
                    AttachmentName = "readme.txt",
                    MimeType = "text/plain",
                    Description = "Fixture readme",
                    Relationship = DocumentOperation.DocumentAttachmentRelationship.Data,
                    CreationDate = new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Unspecified),
                    ModificationDate = new DateTime(2026, 1, 5, 12, 0, 1, DateTimeKind.Unspecified)
                })
                .SaveToBytes();

            var attachment = ReadAttachments(result).Should().ContainSingle().Subject;

            attachment.Key.Should().Be("readme.txt");
            attachment.Name.Should().Be("readme.txt");
            attachment.MimeType.Should().Be("text/plain");
            attachment.Relationship.Should().Be("Data");
            attachment.Description.Should().Be("Fixture readme");
            attachment.Data.Should().Equal(data);
            attachment.ByteSize.Should().Be(data.Length);
            attachment.CreationDate.Should().NotBeNull("the configured creation date is written");
            attachment.ModificationDate.Should().NotBeNull("the configured modification date is written");
        }

        [Test]
        public void AddAttachment_DefaultsNameKeyAndMediaTypeFromTheFile()
        {
            using var file = new TemporaryAttachment("notes.txt", Encoding.ASCII.GetBytes("notes"));

            var result = DocumentOperation
                .Load(CreateSource())
                .AddAttachment(new DocumentOperation.DocumentAttachment { FilePath = file.Path })
                .SaveToBytes();

            var attachment = ReadAttachments(result).Should().ContainSingle().Subject;

            attachment.Key.Should().Be("notes.txt");
            attachment.Name.Should().Be("notes.txt");
            attachment.MimeType.Should().Be("application/octet-stream");
            attachment.Relationship.Should().Be("Unspecified");
            attachment.Description.Should().BeNull();
        }

        [Test]
        public void AddAttachment_PreservesUnicodeNames()
        {
            const string name = "résumé-附件.txt";
            using var file = new TemporaryAttachment(name, new byte[] { 1, 2, 3 });

            var result = DocumentOperation
                .Load(CreateSource())
                .AddAttachment(new DocumentOperation.DocumentAttachment { FilePath = file.Path })
                .SaveToBytes();

            var attachment = ReadAttachments(result).Should().ContainSingle().Subject;

            attachment.Key.Should().Be(name);
            attachment.Name.Should().Be(name, "the Unicode name round-trips through /UF");
            attachment.LegacyName.Should().Be("r?sum?-??.txt",
                "the PDFDocEncoding name replaces characters it cannot represent");
            Encoding.Latin1.GetString(result).Should().Contain("/UF");
        }

        [Test]
        public void AddAttachment_CopiesAttachmentsFromTheLoadedDocument()
        {
            using var firstFile = new TemporaryAttachment("first.txt", Encoding.ASCII.GetBytes("first"));
            using var secondFile = new TemporaryAttachment("second.bin", new byte[] { 9, 8, 7 });

            var source = DocumentOperation
                .Load(CreateSource())
                .AddAttachment(new DocumentOperation.DocumentAttachment
                {
                    FilePath = firstFile.Path,
                    MimeType = "text/plain",
                    Relationship = DocumentOperation.DocumentAttachmentRelationship.Supplement
                })
                .SaveToBytes();

            var result = DocumentOperation
                .Load(source)
                .AddAttachment(new DocumentOperation.DocumentAttachment { FilePath = secondFile.Path })
                .SaveToBytes();

            var attachments = ReadAttachments(result);

            // Name-tree entries are written in sorted key order.
            attachments.Select(x => x.Key).Should().Equal("first.txt", "second.bin");

            attachments[0].Data.Should().Equal(Encoding.ASCII.GetBytes("first"));
            attachments[0].MimeType.Should().Be("text/plain");
            attachments[0].Relationship.Should().Be("Supplement");
            attachments[1].Data.Should().Equal(new byte[] { 9, 8, 7 });
        }

        [Test]
        public void AddAttachment_SavingWithoutChangesKeepsExistingAttachments()
        {
            using var file = new TemporaryAttachment("keep.txt", Encoding.ASCII.GetBytes("keep"));

            var source = DocumentOperation
                .Load(CreateSource())
                .AddAttachment(new DocumentOperation.DocumentAttachment
                {
                    FilePath = file.Path,
                    Relationship = DocumentOperation.DocumentAttachmentRelationship.Source
                })
                .SaveToBytes();

            var result = DocumentOperation.Load(source).SaveToBytes();
            var attachment = ReadAttachments(result).Should().ContainSingle().Subject;

            attachment.Name.Should().Be("keep.txt");
            attachment.Data.Should().Equal(Encoding.ASCII.GetBytes("keep"));
            attachment.Relationship.Should().Be("Source");
        }

        [Test]
        public void AddAttachment_RejectsDuplicateKeysUnlessReplaceIsRequested()
        {
            using var firstFile = new TemporaryAttachment("one.txt", Encoding.ASCII.GetBytes("one"));
            using var secondFile = new TemporaryAttachment("two.txt", Encoding.ASCII.GetBytes("two"));

            var source = CreateSource();

            Action duplicate = () => DocumentOperation
                .Load(source)
                .AddAttachment(new DocumentOperation.DocumentAttachment
                {
                    FilePath = firstFile.Path,
                    Key = "shared.txt"
                })
                .AddAttachment(new DocumentOperation.DocumentAttachment
                {
                    FilePath = secondFile.Path,
                    Key = "shared.txt"
                })
                .SaveToBytes();

            duplicate.Should().Throw<PdfOperationException>()
                .WithMessage("*already contains an attachment*");

            var replaced = DocumentOperation
                .Load(source)
                .AddAttachment(new DocumentOperation.DocumentAttachment
                {
                    FilePath = firstFile.Path,
                    Key = "shared.txt"
                })
                .AddAttachment(new DocumentOperation.DocumentAttachment
                {
                    FilePath = secondFile.Path,
                    Key = "shared.txt",
                    Replace = true
                })
                .SaveToBytes();

            var attachment = ReadAttachments(replaced).Should().ContainSingle().Subject;

            attachment.Key.Should().Be("shared.txt");
            attachment.Data.Should().Equal(Encoding.ASCII.GetBytes("two"));
        }

        [Test]
        public void AddAttachment_RejectsInvalidConfiguration()
        {
            var source = CreateSource();

            Action missingConfiguration = () => DocumentOperation.Load(source).AddAttachment(null);
            missingConfiguration.Should().Throw<ArgumentNullException>();

            Action missingPath = () => DocumentOperation.Load(source)
                .AddAttachment(new DocumentOperation.DocumentAttachment());
            missingPath.Should().Throw<ArgumentException>();

            Action missingFile = () => DocumentOperation.Load(source)
                .AddAttachment(new DocumentOperation.DocumentAttachment
                {
                    FilePath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.txt")
                });
            missingFile.Should().Throw<PdfOperationException>();

            using var file = new TemporaryAttachment("bad-type.txt", new byte[] { 1 });

            foreach (var mimeType in new[] { "plain", "/", "text/plain; charset=utf-8", "text/plain\n" })
            {
                var value = mimeType;

                Action invalidMimeType = () => DocumentOperation.Load(source)
                    .AddAttachment(new DocumentOperation.DocumentAttachment
                    {
                        FilePath = file.Path,
                        MimeType = value
                    });

                invalidMimeType.Should().Throw<ArgumentException>($"'{value}' is not a supported media type");
            }
        }

        [Test]
        public void AddAttachment_ReadsTheFileImmediatelyAndDoesNotModifyTheInput()
        {
            var source = CreateSource();
            var sourceSnapshot = source.ToArray();
            var data = Encoding.ASCII.GetBytes("released");
            var file = new TemporaryAttachment("released.txt", data);

            var operation = DocumentOperation
                .Load(source)
                .AddAttachment(new DocumentOperation.DocumentAttachment { FilePath = file.Path });

            // The file handle is closed when AddAttachment returns; deleting the file must not
            // affect the recorded operation.
            file.Dispose();

            var result = operation.SaveToBytes();

            source.Should().Equal(sourceSnapshot, "the input byte array is not modified");
            ReadAttachments(result).Single().Data.Should().Equal(data);
        }

        [Test]
        public void AddAttachment_SurvivesEncryption()
        {
            using var file = new TemporaryAttachment("secret.txt", Encoding.ASCII.GetBytes("secret"));

            var result = DocumentOperation
                .Load(CreateSource())
                .AddAttachment(new DocumentOperation.DocumentAttachment
                {
                    FilePath = file.Path,
                    MimeType = "text/plain"
                })
                .Encrypt(new DocumentOperation.Encryption256Bit
                {
                    UserPassword = "user",
                    OwnerPassword = "owner"
                })
                .SaveToBytes();

            var attachment = ReadAttachments(result, "user").Should().ContainSingle().Subject;
            attachment.Data.Should().Equal(Encoding.ASCII.GetBytes("secret"));
            attachment.MimeType.Should().Be("text/plain");
        }

        [Test]
        public void ExtendMetadata_AddsTheFragmentAndKeepsTheGeneratedPacket()
        {
            var result = DocumentOperation
                .Load(CreateSource())
                .ExtendMetadata($"<foss:marker xmlns:foss=\"{TestNamespace}\">PDF-305</foss:marker>")
                .SaveToBytes();

            var metadata = ReadMetadata(result);

            metadata.Should().NotBeNull();
            metadata.Should().Contain(TestNamespace);
            metadata.Should().Contain("PDF-305");
            metadata.Should().Contain("Source document", "the generated title is retained");

            var document = XDocument.Parse(metadata);
            var marker = document.Descendants(XName.Get("marker", TestNamespace)).Single();

            marker.Value.Should().Be("PDF-305");
            document.Descendants(XName.Get("Description", RdfNamespace)).Should().NotBeEmpty();

            ReadPageCount(result).Should().Be(1);
        }

        [Test]
        public void ExtendMetadata_PreservesAnExistingXmpPacket()
        {
            var source = DocumentOperation
                .Load(CreateSource())
                .ExtendMetadata($"<foss:first xmlns:foss=\"{TestNamespace}\">one</foss:first>")
                .SaveToBytes();

            var result = DocumentOperation
                .Load(source)
                .ExtendMetadata($"<foss:second xmlns:foss=\"{TestNamespace}\">two</foss:second>")
                .SaveToBytes();

            var metadata = ReadMetadata(result);
            var document = XDocument.Parse(metadata);

            document.Descendants(XName.Get("first", TestNamespace)).Single().Value.Should().Be("one");
            document.Descendants(XName.Get("second", TestNamespace)).Single().Value.Should().Be("two");
            document.Descendants(XName.Get("Description", RdfNamespace)).Should().NotBeEmpty();
            document.Descendants(XName.Get("title", "http://purl.org/dc/elements/1.1/"))
                .Should().NotBeEmpty("the input XMP packet is kept as the base of the extension");
        }

        [Test]
        public void ExtendMetadata_WritesAnIncrementalUpdateThatReadersCanFollow()
        {
            var result = DocumentOperation
                .Load(CreateSource())
                .ExtendMetadata($"<foss:update xmlns:foss=\"{TestNamespace}\">yes</foss:update>")
                .SaveToBytes();

            var text = Encoding.Latin1.GetString(result);
            var eofCount = text.Split(new[] { "%%EOF" }, StringSplitOptions.None).Length - 1;

            eofCount.Should().Be(2, "the original file is followed by one incremental update");
            text.Should().Contain("/Prev");
            text.Should().Contain("/Subtype /XML");

            using var document = Open(result);
            document.PageCount.Should().Be(1);
            document.Info.Title.Should().Be("Source document");
        }

        [Test]
        public void ExtendMetadata_RejectsMalformedFragments()
        {
            var source = CreateSource();

            Action missing = () => DocumentOperation.Load(source).ExtendMetadata(null);
            missing.Should().Throw<ArgumentNullException>();

            foreach (var fragment in new[] { string.Empty, "   ", "not xml", "<undeclared:tag />", "<!-- no element -->" })
            {
                var value = fragment;

                Action invalid = () => DocumentOperation.Load(source).ExtendMetadata(value);
                invalid.Should().Throw<ArgumentException>($"'{value}' is not a usable metadata fragment");
            }
        }

        [Test]
        public void ExtendMetadata_CannotBeCombinedWithEncryption()
        {
            var fragment = $"<foss:marker xmlns:foss=\"{TestNamespace}\">value</foss:marker>";

            Action extendThenEncrypt = () => DocumentOperation
                .Load(CreateSource())
                .ExtendMetadata(fragment)
                .Encrypt(new DocumentOperation.Encryption256Bit
                {
                    UserPassword = "user",
                    OwnerPassword = "owner"
                })
                .SaveToBytes();

            Action encryptThenExtend = () => DocumentOperation
                .Load(CreateSource())
                .Encrypt(new DocumentOperation.Encryption256Bit
                {
                    UserPassword = "user",
                    OwnerPassword = "owner"
                })
                .ExtendMetadata(fragment)
                .SaveToBytes();

            extendThenEncrypt.Should().Throw<InvalidOperationException>()
                .WithMessage("*cannot be combined with encryption*");
            encryptThenExtend.Should().Throw<InvalidOperationException>()
                .WithMessage("*cannot be combined with encryption*");
        }

        private static byte[] CreateSource()
        {
            return Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(200, 100);
                        page.Margin(0);
                        page.PageColor("#d32f2f");
                    });
                })
                .WithMetadata(new DocumentMetadata
                {
                    Title = "Source document",
                    Author = "FossPDF tests"
                })
                .GeneratePdf();
        }

        private static PdfDocument Open(byte[] pdf, string password = null)
        {
            var stream = new MemoryStream(pdf, writable: false);

            return password == null
                ? PdfReader.Open(stream, PdfDocumentOpenMode.Import)
                : PdfReader.Open(stream, password, PdfDocumentOpenMode.Import, new PdfReaderOptions());
        }

        private static int ReadPageCount(byte[] pdf)
        {
            using var document = Open(pdf);
            return document.PageCount;
        }

        private static List<AttachmentInfo> ReadAttachments(byte[] pdf, string password = null)
        {
            using var document = Open(pdf, password);

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
                var stream = streamDictionary?.Stream;
                var parameters = streamDictionary?.Elements.GetDictionary("/Params");

                if (stream == null)
                    throw new InvalidOperationException($"The attachment '{key}' has no embedded file stream.");

                if (stream.IsFiltered() && !stream.TryUncompress())
                    throw new InvalidOperationException("The embedded file stream uses an unsupported filter.");

                attachments.Add(new AttachmentInfo
                {
                    Key = key,
                    Name = specification.Elements.GetString("/UF"),
                    LegacyName = specification.Elements.GetString("/F"),
                    Description = string.IsNullOrEmpty(specification.Elements.GetString("/Desc"))
                        ? null
                        : specification.Elements.GetString("/Desc"),
                    Relationship = TrimName(specification.Elements.GetName("/AFRelationship")),
                    MimeType = TrimName(streamDictionary.Elements.GetName("/Subtype")),
                    Data = stream.UnfilteredValue,
                    ByteSize = parameters == null ? 0 : parameters.Elements.GetInteger("/Size"),
                    CreationDate = ReadDate(parameters, "/CreationDate"),
                    ModificationDate = ReadDate(parameters, "/ModDate")
                });
            }

            return attachments;
        }

        private static string ReadMetadata(byte[] pdf)
        {
            using var document = Open(pdf);
            var metadata = ResolveDictionary(document.Internals.Catalog.Elements.GetObject("/Metadata"));
            var stream = metadata?.Stream;

            if (stream == null)
                return null;

            if (stream.IsFiltered() && !stream.TryUncompress())
                throw new InvalidOperationException("The metadata stream uses an unsupported filter.");

            return Encoding.UTF8.GetString(stream.UnfilteredValue);
        }

        private static DateTime? ReadDate(PdfDictionary parameters, string key)
        {
            if (parameters == null || !parameters.Elements.ContainsKey(key))
                return null;

            var value = parameters.Elements.GetDateTime(key, DateTime.MinValue);
            return value.Year > 1 ? value : (DateTime?)null;
        }

        private static PdfDictionary ResolveDictionary(PdfItem item)
        {
            if (item is PdfReference reference)
                return reference.Value as PdfDictionary;

            return item as PdfDictionary;
        }

        private static string TrimName(string value)
        {
            return value.StartsWith("/", StringComparison.Ordinal) ? value.Substring(1) : value;
        }

        private sealed class AttachmentInfo
        {
            public string Key { get; set; }
            public string Name { get; set; }
            public string LegacyName { get; set; }
            public string Description { get; set; }
            public string Relationship { get; set; }
            public string MimeType { get; set; }
            public byte[] Data { get; set; }
            public int ByteSize { get; set; }
            public DateTime? CreationDate { get; set; }
            public DateTime? ModificationDate { get; set; }
        }

        private sealed class TemporaryAttachment : IDisposable
        {
            private readonly string _directory;

            public TemporaryAttachment(string name, byte[] content)
            {
                _directory = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "foss-pdf-attachments",
                    Guid.NewGuid().ToString("N"));

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
