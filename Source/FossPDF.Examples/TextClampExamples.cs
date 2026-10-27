using System.IO;
using System.Linq;
using FossPDF.Fluent;
using NUnit.Framework;

namespace FossPDF.Examples
{
    public class TextClampExamples
    {
        [Test]
        public void ClampParagraphWithCustomEllipsis()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(160, 200);
                page.Margin(10);
                page.PageColor("#ffffff");

                page.Content().Column(column =>
                {
                    column.Item().Text(text =>
                    {
                        text.ClampLines(2, "[more]");
                        text.Span("One two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen.")
                            .FontSize(12);
                    });

                    column.Item().Text(text =>
                    {
                        text.ClampLines(5, "[more]");
                        text.Span("A short paragraph is not truncated.").FontSize(12);
                    });
                });
            }));

            var pdf = document.GeneratePdf(out var pageCount);
            Assert.That(pageCount, Is.EqualTo(1));

            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "text-clamp-example");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "document.pdf"), pdf);

            var image = document.GenerateImages().Single();
            File.WriteAllBytes(Path.Combine(directory, "page-1.png"), image);
        }
    }
}
