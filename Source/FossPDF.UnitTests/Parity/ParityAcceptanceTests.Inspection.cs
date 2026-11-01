using System.Text;
using System.Text.Json;
using FossPDF.Fluent;
using NUnit.Framework;

namespace FossPDF.UnitTests.Parity
{
    public partial class ParityAcceptanceTests
    {
        [ParityFixtureRunner("DEV-501")]
        private static void RunInspection(ParityFixture fixture)
        {
            using var preview = InspectionTests.CreateDocument().GenerateInspectedPreview();
            preview.Inspection.Validate(preview.Pictures.Count);
            Assert.That(preview.Inspection.Nodes.Count, Is.GreaterThan(10));
            SaveArtifact(fixture.Id, "inspection-v1.json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(preview.Inspection)));
        }

#if NET9_0
        [ParityFixtureRunner("DEV-502")]
        private static void RunInteractiveInspection(ParityFixture fixture)
        {
            using var preview = InspectionTests.CreateDocument().GenerateInspectedPreview();
            using var model = new FossPDF.Previewer.InspectionViewModel(action => action());
            model.Refresh(preview.Inspection, preview.Pictures.Count);
            model.Query = "Report heading";
            model.SearchCompletion.GetAwaiter().GetResult();
            model.Selected = System.Linq.Enumerable.Single(model.Entries);
            Assert.That(model.Highlights.Count, Is.EqualTo(preview.Pictures.Count));
            Assert.That(model.SourceNode.SourceLine, Is.GreaterThan(0));
            model.Query = "Revenue";
            model.SearchCompletion.GetAwaiter().GetResult();
            Assert.That(model.Entries.Count, Is.GreaterThanOrEqualTo(10));
            model.Tool = FossPDF.Previewer.InspectionViewModel.Tools[2];
            model.Pick(new FossPDF.Previewer.PagePoint(1, 10, 10));
            model.Pick(new FossPDF.Previewer.PagePoint(1, 110, 10));
            Assert.That(model.Measurement.Value.Points, Is.EqualTo(100).Within(0.25));
            SaveArtifact(fixture.Id, "interactive-inspection.json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                Pages = preview.Pictures.Count, RepeatedHeadingOccurrences = model.Highlights.Count,
                Source = model.CopyLocation, SearchMatches = model.Entries.Count, MeasurementPoints = model.Measurement.Value.Points
            })));
        }
#endif
    }
}
