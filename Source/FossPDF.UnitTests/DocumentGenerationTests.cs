using System.Text;
using FossPDF.Fluent;
using NUnit.Framework;

namespace FossPDF.UnitTests
{
    public class DocumentGenerationTests
    {
        [Test]
        public void GeneratePdf_RendersTextAndReportsPageCount()
        {
            var document = Document.Create(container => container.Page(page =>
                page.Content().Text("FossPDF document generation")));

            var bytes = document.GeneratePdf(out var totalPages);

            Assert.That(totalPages, Is.EqualTo(1));
            Assert.That(Encoding.ASCII.GetString(bytes, 0, 5), Is.EqualTo("%PDF-"));
        }
    }
}
