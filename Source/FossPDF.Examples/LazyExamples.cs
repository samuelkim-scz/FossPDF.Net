using System.IO;
using System.Linq;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;

namespace FossPDF.Examples
{
    public class LazyExamples
    {
        [Test]
        public void GenerateDeferredSections()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(320, 480);
                page.Margin(20);
                page.PageColor("#ffffff");
                page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(12));

                page.Content().Column(column =>
                {
                    for (var section = 0; section < 20; section++)
                    {
                        var start = section * 50;
                        var end = start + 49;

                        column.Item().Lazy(content => content.Column(inner =>
                        {
                            inner.Item().Text($"Section {section}").FontSize(16).Bold();

                            for (var number = start; number <= end; number++)
                                inner.Item().Text($"Number {number}");
                        }));
                    }
                });
            }));

            var pdf = document.GeneratePdf(out var pageCount);
            Assert.That(pageCount, Is.GreaterThan(1));
            Assert.That(document.GenerateImages().Count(), Is.EqualTo(pageCount));

            Save("lazy-sections.pdf", pdf);
        }

        [Test]
        public void GenerateCachedSections()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(320, 320);
                page.Margin(20);
                page.PageColor("#ffffff");
                page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(12));

                page.Content().Column(column =>
                {
                    for (var section = 0; section < 10; section++)
                    {
                        var index = section;
                        column.Item().LazyWithCache(content => content.Column(inner =>
                        {
                            inner.Item().Text($"Cached section {index}").Bold();

                            for (var row = 0; row < 20; row++)
                                inner.Item().Text($"Row {index}.{row}");
                        }));
                    }
                });
            }));

            var pdf = document.GeneratePdf();
            Assert.That(pdf.Length, Is.GreaterThan(5));

            Save("lazy-with-cache.pdf", pdf);
        }

        private static void Save(string fileName, byte[] pdf)
        {
            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "lazy-example");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, fileName), pdf);
        }
    }
}
