#if NET9_0
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FossPDF.Fluent;
using FossPDF.PdfOperations;
using NUnit.Framework;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace FossPDF.UnitTests.Parity
{
    /// <summary>
    /// FP-305 acceptance: attach a file with a declared media type and associated-file
    /// relationship, extend the XMP packet with a namespaced fragment, and verify that the exact
    /// bytes, metadata and page content can be read back from the output. The operations companion
    /// targets net9.0 only, so this runner is compiled for that framework; the fixture declares
    /// frameworks: ["net9.0"].
    /// </summary>
    public partial class ParityAcceptanceTests
    {
        [ParityFixtureRunner("PDF-305")]
        private static void RunPdfAttachments(ParityFixture fixture)
        {
            var catalog = FixtureCatalog.Load();
            var sourceId = fixture.Input.GetProperty("source").GetString();
            var sourceFixture = catalog.Fixtures.Single(x => x.Id == sourceId);
            var source = ExplicitPagination(sourceFixture).GeneratePdf();

            var attachment = fixture.Input.GetProperty("attachment");
            var attachmentName = attachment.GetProperty("name").GetString()!;
            var mediaType = attachment.GetProperty("media_type").GetString()!;
            var payload = Encoding.UTF8.GetBytes(attachment.GetProperty("utf8").GetString()!);
            var relationshipText = attachment.GetProperty("relationship").GetString()!;
            var relationship = ParseAttachmentRelationship(relationshipText);

            var xmp = fixture.Input.GetProperty("xmp");
            var namespaceName = xmp.GetProperty("namespace").GetString()!;
            var key = xmp.GetProperty("key").GetString()!;
            var value = xmp.GetProperty("value").GetString()!;
            var fragment = $"<parity:{key} xmlns:parity=\"{namespaceName}\">{value}</parity:{key}>";

            var sourceSnapshot = source.ToArray();
            SaveArtifact(fixture.Id, "source.pdf", source);
            SaveArtifact(fixture.Id, "attachment-payload.txt", payload);

            var directory = Path.Combine(Path.GetTempPath(), "fosspdf-parity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var attachmentPath = Path.Combine(directory, attachmentName);

            byte[] output;

            try
            {
                File.WriteAllBytes(attachmentPath, payload);

                output = DocumentOperation
                    .Load(source)
                    .AddAttachment(new DocumentOperation.DocumentAttachment
                    {
                        FilePath = attachmentPath,
                        AttachmentName = attachmentName,
                        MimeType = mediaType,
                        Relationship = relationship
                    })
                    .ExtendMetadata(fragment)
                    .SaveToBytes();
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }

            Assert.That(Encoding.ASCII.GetString(output, 0, 5), Is.EqualTo("%PDF-"),
                "PDF-305: the result is a PDF document.");
            Assert.That(source, Is.EqualTo(sourceSnapshot),
                "PDF-305: the input byte array is not modified.");
            SaveArtifact(fixture.Id, "attachments.pdf", output);

            var extracted = ReadParityAttachment(output);

            Assert.That(extracted.Key, Is.EqualTo(attachmentName),
                "PDF-305: the embedded-files name-tree key defaults to the attachment name.");
            Assert.That(extracted.Name, Is.EqualTo(attachmentName),
                "PDF-305: the attachment name is preserved.");
            Assert.That(extracted.MimeType, Is.EqualTo(mediaType),
                "PDF-305: the declared media type is written as the embedded file subtype.");
            Assert.That(extracted.Relationship, Is.EqualTo(relationshipText),
                "PDF-305: the associated-file relationship is written.");
            Assert.That(extracted.Data, Is.EqualTo(payload),
                "PDF-305: the attachment bytes are stored unchanged.");

            var raw = Encoding.Latin1.GetString(output);

            Assert.That(Regex.IsMatch(raw, @"/AF\s*\["), Is.True,
                "PDF-305: the attachment is listed in the catalog's associated-files array.");
            Assert.That(Regex.IsMatch(raw, @"/AFRelationship\s*/" + relationshipText), Is.True,
                "PDF-305: the file specification declares the associated-file relationship.");

            var metadata = ReadParityMetadata(output);

            Assert.That(metadata, Is.Not.Null, "PDF-305: the output has an XMP metadata stream.");

            var document = XDocument.Parse(metadata);
            var extension = document.Descendants(XName.Get(key, namespaceName)).SingleOrDefault();

            Assert.That(extension, Is.Not.Null,
                "PDF-305: the declared XMP namespace and key are present.");
            Assert.That(extension.Value, Is.EqualTo(value),
                "PDF-305: the declared XMP value is preserved.");

            Assert.That(document.Descendants(XName.Get("Description", "http://www.w3.org/1999/02/22-rdf-syntax-ns#")),
                Is.Not.Empty, "PDF-305: the extension lives inside an rdf:Description element.");

            var sourcePages = ReadPageFacts(source);
            var outputPages = ReadPageFacts(output);

            Assert.That(outputPages, Has.Length.EqualTo(sourcePages.Length),
                "PDF-305: attaching a file and extending metadata preserve the page count.");

            for (var pageIndex = 0; pageIndex < sourcePages.Length; pageIndex++)
            {
                Assert.That(outputPages[pageIndex].Content, Is.EqualTo(sourcePages[pageIndex].Content),
                    $"PDF-305: page {pageIndex + 1} content is preserved.");
            }
        }

        private static DocumentOperation.DocumentAttachmentRelationship ParseAttachmentRelationship(string value)
        {
            switch (value)
            {
                case "Data":
                    return DocumentOperation.DocumentAttachmentRelationship.Data;
                case "Source":
                    return DocumentOperation.DocumentAttachmentRelationship.Source;
                case "Alternative":
                    return DocumentOperation.DocumentAttachmentRelationship.Alternative;
                case "Supplement":
                    return DocumentOperation.DocumentAttachmentRelationship.Supplement;
                default:
                    return DocumentOperation.DocumentAttachmentRelationship.Unspecified;
            }
        }

        private static ParityAttachment ReadParityAttachment(byte[] pdf)
        {
            using var stream = new MemoryStream(pdf, writable: false);
            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

            var names = document.Internals.Catalog.Elements.GetDictionary("/Names");
            var embedded = names?.Elements.GetDictionary("/EmbeddedFiles");
            var entries = embedded?.Elements.GetArray("/Names");

            Assert.That(entries, Is.Not.Null, "PDF-305: the catalog contains an embedded-files name tree.");
            Assert.That(entries.Elements.Count, Is.GreaterThanOrEqualTo(2),
                "PDF-305: the name tree contains the attached file.");

            var key = ((PdfString)entries.Elements[0]).Value;
            var specification = ResolveParityDictionary(entries.Elements[1]);
            var files = specification?.Elements.GetDictionary("/EF");
            var fileStream = ResolveParityDictionary(files?.Elements.GetObject("/F"));
            var streamValue = fileStream?.Stream;

            Assert.That(streamValue, Is.Not.Null, "PDF-305: the name-tree entry references an embedded file stream.");

            if (streamValue.IsFiltered())
                Assert.That(streamValue.TryUncompress(), Is.True, "PDF-305: the embedded file stream can be decoded.");

            return new ParityAttachment(
                key,
                specification.Elements.GetString("/UF"),
                TrimParityName(fileStream.Elements.GetName("/Subtype")),
                TrimParityName(specification.Elements.GetName("/AFRelationship")),
                streamValue.UnfilteredValue);
        }

        private static string ReadParityMetadata(byte[] pdf)
        {
            using var stream = new MemoryStream(pdf, writable: false);
            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

            var metadata = ResolveParityDictionary(document.Internals.Catalog.Elements.GetObject("/Metadata"));
            var metadataStream = metadata?.Stream;

            if (metadataStream == null)
                return null;

            if (metadataStream.IsFiltered())
                Assert.That(metadataStream.TryUncompress(), Is.True,
                    "PDF-305: the XMP metadata stream can be decoded.");

            return Encoding.UTF8.GetString(metadataStream.UnfilteredValue);
        }

        private static PdfDictionary ResolveParityDictionary(PdfItem item)
        {
            return item switch
            {
                PdfReference reference => reference.Value as PdfDictionary,
                PdfDictionary dictionary => dictionary,
                _ => null
            };
        }

        private static string TrimParityName(string value)
        {
            return value.StartsWith("/", StringComparison.Ordinal) ? value.Substring(1) : value;
        }

        private readonly struct ParityAttachment
        {
            public ParityAttachment(string key, string name, string mimeType, string relationship, byte[] data)
            {
                Key = key;
                Name = name;
                MimeType = mimeType;
                Relationship = relationship;
                Data = data;
            }

            public string Key { get; }
            public string Name { get; }
            public string MimeType { get; }
            public string Relationship { get; }
            public byte[] Data { get; }
        }
    }
}
#endif
