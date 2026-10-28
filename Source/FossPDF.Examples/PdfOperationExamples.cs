using System;
using System.IO;
using System.Text;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.PdfOperations;
using NUnit.Framework;

namespace FossPDF.Examples
{
    public class PdfOperationExamples
    {
        [Test]
        public void SelectReorderAndMergeExistingDocuments()
        {
            var directory = CreateOutputDirectory("pdf-operations-selection");

            var firstPath = Path.Combine(directory, "first.pdf");
            var appendixPath = Path.Combine(directory, "appendix.pdf");
            var outputPath = Path.Combine(directory, "selected-and-merged.pdf");

            File.WriteAllBytes(firstPath, CreateSource(
                ("First page", "#d32f2f"),
                ("Second page", "#1976d2"),
                ("Third page", "#388e3c")));
            File.WriteAllBytes(appendixPath, CreateSource(("Appendix", "#8e24aa")));

            // Keep pages 2 and 1 in that order, then append the appendix.
            DocumentOperation
                .LoadFile(firstPath)
                .TakePages("2,1")
                .MergeFile(appendixPath, "1")
                .Save(outputPath);

            var result = File.ReadAllBytes(outputPath);

            Assert.That(Encoding.ASCII.GetString(result, 0, 5), Is.EqualTo("%PDF-"));

            // Loading the result again proves it is a usable PDF and reports three pages.
            Assert.That(
                DocumentOperation.LoadFile(outputPath).TakePages("3").SaveToBytes(),
                Is.Not.Empty);

            Assert.Throws<PdfOperationException>(
                () => DocumentOperation.LoadFile(outputPath).TakePages("4").SaveToBytes());
        }

        [Test]
        public void WorkWithByteArraysAndBorrowedStreams()
        {
            var directory = CreateOutputDirectory("pdf-operations-streams");
            var source = CreateSource(
                ("First page", "#d32f2f"),
                ("Second page", "#1976d2"),
                ("Third page", "#388e3c"));

            var reversed = DocumentOperation.Load(source).TakePages("3-1").SaveToBytes();
            File.WriteAllBytes(Path.Combine(directory, "reversed.pdf"), reversed);
            Assert.That(Encoding.ASCII.GetString(reversed, 0, 5), Is.EqualTo("%PDF-"));

            using var input = new MemoryStream(source);

            using var output = new MemoryStream();
            DocumentOperation.Load(input).TakePages("z").Save(output);

            Assert.That(input.CanRead, Is.True);
            Assert.That(input.Position, Is.EqualTo(source.Length));
            Assert.That(output.CanWrite, Is.True);
            Assert.That(Encoding.ASCII.GetString(output.ToArray(), 0, 5), Is.EqualTo("%PDF-"));

            File.WriteAllBytes(Path.Combine(directory, "last-page.pdf"), output.ToArray());
        }

        private static byte[] CreateSource(params (string Title, string Color)[] pages)
        {
            return Document.Create(container =>
            {
                foreach (var page in pages)
                {
                    container.Page(descriptor =>
                    {
                        descriptor.Size(200, 150);
                        descriptor.Margin(15);
                        descriptor.PageColor("#ffffff");

                        descriptor.Content().Column(column =>
                        {
                            column.Item().Text(page.Title).FontSize(18).Bold();
                            column.Item().Height(40).Background(page.Color);
                        });
                    });
                }
            })
                .WithMetadata(new DocumentMetadata
                {
                    Title = "FossPDF PDF operations example",
                    Author = "FossPDF examples"
                })
                .GeneratePdf();
        }

        private static string CreateOutputDirectory(string name)
        {
            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, name);
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
