using System.IO;
using System.Linq;
using System.Text.Json;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.Previewer;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.Examples
{
    public class PreviewInspectionExamples
    {
        [Test]
        public void SaveLocalInspectionSnapshot()
        {
            var document = Document.Create(d => d.Page(page =>
            {
                page.Size(240, 240);
                page.Margin(20);
                page.Header().Inspect("heading", "Invoice heading").Text("Revenue");
                page.Content().Column(column =>
                {
                    for (var index = 0; index < 12; index++)
                        RevenueRow(column.Item(), "revenue-" + index);
                });
            }));
            using var snapshot = document.GeneratePreviewerSnapshot(new PreviewerOptions { EnableInspection = true });
            var output = Path.Combine(TestContext.CurrentContext.TestDirectory, "preview-inspection-example");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "inspection.json"),
                JsonSerializer.Serialize(snapshot.Inspection, new JsonSerializerOptions { WriteIndented = true }));
            using var data = snapshot.Pictures.First().Picture.Serialize();
            File.WriteAllBytes(Path.Combine(output, "page-1.skp"), data.ToArray());
            Assert.That(snapshot.Inspection!.Elements.Any(x => x.Id == "id:revenue-0"), Is.True);
            // For an interactive session with the updated previewer:
            // document.ShowInPreviewer(new PreviewerOptions { EnableInspection = true });
        }

        private static void RevenueRow(IContainer container, string instanceId) =>
            container.Inspect(instanceId, "Reusable revenue row").Height(30).Padding(4).Text("Revenue: $125.00");
    }
}
