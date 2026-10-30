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
    }
}
