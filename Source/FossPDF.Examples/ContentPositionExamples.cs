using System.IO;
using System.Linq;
using System.Text.Json;
using FossPDF.Fluent;
using NUnit.Framework;

namespace FossPDF.Examples
{
    public class ContentPositionExamples
    {
        [Test]
        public void CaptureContentPositions()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(160, 160);
                page.Margin(10);
                page.PageColor("#ffffff");

                page.Content().Column(column =>
                {
                    for (var index = 0; index < 12; index++)
                    {
                        column.Item()
                            .CaptureContentPosition("rows")
                            .Height(20)
                            .Background(index % 2 == 0 ? "#eeeeee" : "#cccccc");
                    }
                });
            }));

            var pdf = document.GeneratePdf(out var pageCount);
            Assert.That(pageCount, Is.EqualTo(2));

            var positions = document.ContentPositions
                .Where(x => x.Id == "rows")
                .OrderBy(x => x.PageNumber)
                .ThenBy(x => x.Y)
                .ToArray();

            Assert.That(positions, Has.Length.EqualTo(12));

            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "content-position-example");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "document.pdf"), pdf);
            File.WriteAllText(
                Path.Combine(directory, "positions.json"),
                JsonSerializer.Serialize(positions, new JsonSerializerOptions { WriteIndented = true }));

            var images = document.GenerateImages().ToArray();

            for (var index = 0; index < images.Length; index++)
                File.WriteAllBytes(Path.Combine(directory, $"page-{index + 1}.png"), images[index]);
        }
    }
}
