using System.IO;
using System.Linq;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;

namespace FossPDF.Examples
{
    public class PageControlExamples
    {
        [Test]
        public void PreventPageBreakAndPageAwareShowIf()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(200, 300);
                page.Margin(20);
                page.PageColor("#ffffff");

                page.Content().Column(column =>
                {
                    // This prefix leaves too little room for the group below.
                    column.Item().Height(220).Background("#eeeeee");

                    column.Item().PreventPageBreak().Column(group =>
                    {
                        group.Item().Height(40).Background("#ff0000");
                        group.Item().Height(40).Background("#0000ff");
                    });

                    // Rendered only on even pages, when the final page count is known.
                    column.Item()
                        .ShowIf(context => context.PageNumber % 2 == 0 && context.TotalPages == 2)
                        .Height(40)
                        .Background("#00ff00");
                });
            }));

            WriteOutputs(document, "page-control-example", expectedPages: 2);
        }

        [Test]
        public void RepeatMarkerWhileAdjacentContentContinues()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(200, 300);
                page.Margin(20);
                page.PageColor("#ffffff");

                page.Content().Row(row =>
                {
                    // The marker is repeated on every page the row spans.
                    row.ConstantItem(40)
                        .Repeat()
                        .Height(40)
                        .Background("#ff0000");

                    row.RelativeItem().Column(column =>
                    {
                        for (var index = 0; index < 20; index++)
                            column.Item().Height(20).Background("#0000ff");
                    });
                });
            }));

            WriteOutputs(document, "page-control-repeat-example", expectedPages: 2);
        }

        private static void WriteOutputs(Document document, string directoryName, int expectedPages)
        {
            var pdf = document.GeneratePdf(out var pageCount);
            Assert.That(pageCount, Is.EqualTo(expectedPages));

            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, directoryName);
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "document.pdf"), pdf);

            var images = document.GenerateImages().ToArray();
            Assert.That(images.Length, Is.EqualTo(expectedPages));

            for (var index = 0; index < images.Length; index++)
                File.WriteAllBytes(Path.Combine(directory, $"page-{index + 1}.png"), images[index]);
        }
    }
}
