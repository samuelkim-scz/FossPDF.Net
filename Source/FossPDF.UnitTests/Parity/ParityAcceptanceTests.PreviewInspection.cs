using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using FossPDF.Fluent;
using FossPDF.Previewer;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.UnitTests.Parity
{
    public partial class ParityAcceptanceTests
    {
        [ParityFixtureRunner("DEV-501")]
        private static void RunPreviewInspection(ParityFixture fixture)
        {
            using var snapshot = PreviewInspectionTests.Sample().GeneratePreviewerSnapshot(PreviewInspectionTests.Enabled());
            var json = JsonSerializer.SerializeToUtf8Bytes(snapshot.Inspection, new JsonSerializerOptions { WriteIndented = true });
            Assert.That(json.Length, Is.LessThan(PreviewerProtocol.MaxCommandBytes));
            PreviewerProtocol.Validate(snapshot.Inspection!, snapshot.Pictures.Count);
            SaveArtifact(fixture.Id, "inspection.json", json);
            SaveArtifact(fixture.Id, "summary.txt", Encoding.UTF8.GetBytes(
                $"Version: 1\nPages: {snapshot.Pictures.Count}\nElements: {snapshot.Inspection!.Elements.Count}\n" +
                $"Occurrences: {snapshot.Inspection.Occurrences.Count}\nPayload bytes: {json.Length}\n"));
            var index = 0;
            foreach (var picture in snapshot.Pictures)
            {
                using var bitmap = PreviewInspectionTests.Raster(picture);
                using var image = SKImage.FromBitmap(bitmap);
                using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                SaveArtifact(fixture.Id, $"page-{++index}.png", png.ToArray());
            }
        }
    }
}
