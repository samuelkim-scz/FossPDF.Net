#if NET9_0
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FossPDF.Infrastructure;
using FossPDF.PdfOperations;
using NUnit.Framework;

namespace FossPDF.UnitTests.Parity
{
    public partial class ParityAcceptanceTests
    {
        private static string AccessibilityReviewPath(string file) => Path.Combine(AppContext.BaseDirectory, "Resources", "AccessibilityReview", file);

        [Test]
        public void ValidateTheLockedAccessibilityReviewFixture()
        {
            PdfATests.RequireValidator();
            using var record = JsonDocument.Parse(File.ReadAllText(AccessibilityReviewPath("human-review.json")));
            var pdf = File.ReadAllBytes(AccessibilityReviewPath(record.RootElement.GetProperty("pdf").GetString()!));
            Assert.That(Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant(), Is.EqualTo(record.RootElement.GetProperty("pdf_sha256").GetString()));
            var ua = PdfUaValidator.Validate(pdf);
            var archival = PdfAValidator.Validate(pdf, PdfAConformance.PdfA3A);
            Assert.That(ua.MachineChecksPassed, Is.True, ua.Json);
            Assert.That(archival.IsCompliant, Is.True, archival.Json);
            SaveArtifact("ACC-403", "review-fixture-r1.pdf", pdf);
            SaveArtifact("ACC-403", "review-fixture-r1-ua1.json", Encoding.UTF8.GetBytes(ua.Json));
            SaveArtifact("ACC-403", "review-fixture-r1-pdfa3a.json", Encoding.UTF8.GetBytes(archival.Json));
            SaveArtifact("ACC-403", "human-review.json", File.ReadAllBytes(AccessibilityReviewPath("human-review.json")));
        }

        // Human evidence is tied to exact bytes; machine qualification runs separately.
        [ParityFixtureRunner("ACC-403")]
        private static void RunPdfUaAcceptance(ParityFixture fixture)
        {
            using var record = JsonDocument.Parse(File.ReadAllText(AccessibilityReviewPath("human-review.json")));
            var evidence = record.RootElement;
            Assert.That(evidence.GetProperty("status").GetString(), Is.EqualTo("passed"), "Human accessibility review is required to accept FP-403.");
            foreach (var field in new[] { "tool", "reviewer", "reported_at_utc" })
                Assert.That(evidence.GetProperty(field).GetString(), Is.Not.Null.And.Not.Empty, "Missing human evidence: " + field);
            Assert.That(DateTimeOffset.TryParse(evidence.GetProperty("reported_at_utc").GetString(), out _), Is.True);
            if (evidence.GetProperty("tool_version").ValueKind == JsonValueKind.Null)
                Assert.That(evidence.GetProperty("tool_version_status").GetString(), Is.EqualTo("not_reported"),
                    "An unavailable tool version must be disclosed rather than invented.");
            foreach (var check in new[] { "reading_order", "headings", "list", "table_headers", "figure_alternative", "links", "language", "decorative_content" })
                Assert.That(evidence.GetProperty("checks").GetProperty(check).GetBoolean(), Is.True, "Human check did not pass: " + check);
            var pdf = File.ReadAllBytes(AccessibilityReviewPath(evidence.GetProperty("pdf").GetString()!));
            Assert.That(Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant(), Is.EqualTo(evidence.GetProperty("pdf_sha256").GetString()), "Human results must identify these exact PDF bytes.");
        }
    }
}
#endif
