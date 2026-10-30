#if NET9_0
using System.Text;
using System.Text.Json;
using FossPDF.Drawing.Exceptions;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.Previewer;
using NUnit.Framework;

namespace FossPDF.UnitTests.Parity
{
    public partial class ParityAcceptanceTests
    {
        [ParityFixtureRunner("DEV-502")]
        private static void RunInteractiveInspection(ParityFixture fixture)
        {
            using var preview = InspectionTests.CreateDocument().GenerateInspectedPreview();
            using var model = new InspectionViewModel();
            model.Load(preview.Inspection, preview.Pictures.Count);
            model.Search = "Revenue";
            Assert.That(model.Matches.Count, Is.EqualTo(10));
            model.SelectedEntry = model.Matches[9];
            Assert.That(model.Details, Does.Contain("InspectionTests.cs"));
            var selectedPage = model.Highlight!.Page;
            model.InteractionMode = 2; model.Pick(1, 10, 20); model.Pick(1, 46, 100);
            Assert.That(model.Measurement, Is.EqualTo(new InspectionMeasurement(1, 10, 20, 46, 20)));
            SaveArtifact(fixture.Id, "inspection-actions.json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                search = "Revenue", matches = model.Matches.Count, selectedPage, selectedDetails = model.Details,
                measurement = model.Measurement, units = "points", model.Status
            })));
            var invalid = Document.Create(container => container.Page(page =>
            {
                page.Size(100, 100); page.Content().Height(1000).Text("This cannot fit.");
            })).WithSettings(new DocumentSettings { EnableDebugging = true, DocumentLayoutExceptionThreshold = 2 });
            var failure = Assert.Throws<DocumentLayoutException>(() => { using var ignored = invalid.GenerateInspectedPreview(); });
            var text = failure!.ToString();
            model.Load(new InspectionSnapshot { Diagnostic = text.Length > 32768 ? text.Substring(0, 32768) : text }, 0);
            Assert.That(model.HasDiagnostic, Is.True);
            SaveArtifact(fixture.Id, "layout-diagnostic.txt", Encoding.UTF8.GetBytes(model.Diagnostic));
        }
    }
}
#endif
