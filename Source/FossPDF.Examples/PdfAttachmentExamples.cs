using System.IO;
using System.Text;
using FossPDF.Fluent;
using NUnit.Framework;

namespace FossPDF.Examples
{
    public class PdfAttachmentExamples
    {
        [Test]
        public void AttachSupportingDataAndExtendMetadata()
        {
            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "pdf-operations-attachments");
            Directory.CreateDirectory(directory);
            var source = Document.Create(container => container.Page(page =>
                page.Content().Text("Report with supporting data"))).GeneratePdf();
            using var data = new MemoryStream(Encoding.UTF8.GetBytes("Item,Amount\nConsulting,1200\n"));
            DocumentOperation.Load(source)
                .AddAttachment(data, new DocumentOperation.DocumentAttachment
                {
                    Key = "supporting-data",
                    AttachmentName = "supporting-data.csv",
                    MimeType = "text/csv",
                    Description = "Source figures used in this report",
                    Relationship = DocumentOperation.DocumentAttachmentRelationship.Data
                })
                .ExtendMetadata("<report:Identifier xmlns:report='urn:example:reports'>REPORT-001</report:Identifier>")
                .Save(Path.Combine(directory, "report-with-data.pdf"));
            Assert.That(data.CanRead, Is.True);
        }
    }
}
