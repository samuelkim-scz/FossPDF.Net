using System.IO;
using System.Linq;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;

namespace FossPDF.Examples
{
    public class DocumentMergeExamples
    {
        [Test]
        public void MergeWithOriginalAndContinuousPageNumbers()
        {
            var first = CreateSourceDocument("First document", "#d32f2f");
            var second = CreateSourceDocument("Second document", "#1976d2");

            var original = Document
                .Merge(first, second)
                .UseOriginalPageNumbers()
                .WithMetadata(new DocumentMetadata { Title = "Merged document (original numbering)" });

            var continuous = Document
                .Merge(first, second)
                .UseContinuousPageNumbers()
                .WithMetadata(new DocumentMetadata { Title = "Merged document (continuous numbering)" });

            WriteOutputs(original, "document-merge-original");
            WriteOutputs(continuous, "document-merge-continuous");
        }

        private static Document CreateSourceDocument(string title, string color)
        {
            return Document.Create(container => container.Page(page =>
            {
                page.Size(200, 150);
                page.Margin(15);
                page.PageColor("#ffffff");

                page.Content().Column(column =>
                {
                    column.Item().Text(title).FontSize(18).Bold();
                    column.Item().Height(40).Background(color);
                    column.Item().PageBreak();
                    column.Item().Text(title + " (continued)").FontSize(14);
                    column.Item().Height(40).Background(color);
                });

                page.Footer().Height(20).AlignRight().Text(text =>
                {
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            }));
        }

        private static void WriteOutputs(IDocument document, string directoryName)
        {
            const int expectedPages = 4;

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
