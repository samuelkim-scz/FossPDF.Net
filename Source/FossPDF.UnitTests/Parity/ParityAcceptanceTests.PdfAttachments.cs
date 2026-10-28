#if NET9_0
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FossPDF.Fluent;
using NUnit.Framework;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace FossPDF.UnitTests.Parity
{
    public partial class ParityAcceptanceTests
    {
        [ParityFixtureRunner("PDF-305")]
        private static void RunPdfAttachments(ParityFixture fixture)
        {
            var sourceFixture = FixtureCatalog.Load().Fixtures.Single(x => x.Id == fixture.Input.GetProperty("source").GetString());
            var source = ExplicitPagination(sourceFixture).GeneratePdf();
            var attachment = fixture.Input.GetProperty("attachment");
            var data = Encoding.UTF8.GetBytes(attachment.GetProperty("utf8").GetString());
            var extension = fixture.Input.GetProperty("xmp");
            var property = new XElement(XName.Get(extension.GetProperty("key").GetString(), extension.GetProperty("namespace").GetString()),
                extension.GetProperty("value").GetString());
            SaveArtifact(fixture.Id, "source.pdf", source);
            SaveArtifact(fixture.Id, "expected-attachment.bin", data);
            foreach (var relationship in Enum.GetValues<DocumentOperation.DocumentAttachmentRelationship>())
            {
                var result = DocumentOperation.Load(source).AddAttachment(data, new DocumentOperation.DocumentAttachment
                {
                    AttachmentName = attachment.GetProperty("name").GetString(),
                    MimeType = attachment.GetProperty("media_type").GetString(),
                    Description = "Independent parity attachment",
                    CreationDate = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    ModificationDate = new DateTime(2000, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                    Relationship = relationship
                }).ExtendMetadata(property.ToString(SaveOptions.DisableFormatting)).SaveToBytes();
                SaveArtifact(fixture.Id, $"attachment-{relationship}.pdf", result);
                using var input = new MemoryStream(result);
                using var document = PdfReader.Open(input, PdfDocumentOpenMode.Import);
                Assert.That(document.PageCount, Is.EqualTo(2));
                var association = document.Internals.Catalog.Elements.GetArray("/AF");
                Assert.That(association.Elements.Count, Is.EqualTo(1));
                var specification = (PdfDictionary)((PdfReference)association.Elements[0]).Value;
                Assert.That(specification.Elements.GetName("/AFRelationship"), Is.EqualTo("/" + relationship));
                Assert.That(specification.Elements.GetString("/UF"), Is.EqualTo(attachment.GetProperty("name").GetString()));
            }
        }
    }
}
#endif
