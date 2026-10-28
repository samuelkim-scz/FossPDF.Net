using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FossPDF.Infrastructure;

namespace FossPDF.PdfOperations
{
    /// <summary>The complete report from the pinned veraPDF validator, including failed rules.</summary>
    public sealed class PdfAValidationReport
    {
        public PdfAConformance Profile { get; }
        public bool IsCompliant { get; }
        public int FailedRules { get; }
        public int FailedChecks { get; }
        public string Json { get; }

        internal PdfAValidationReport(string json, PdfAConformance profile)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var report = document.RootElement.GetProperty("report");
                var releases = report.GetProperty("buildInformation").GetProperty("releaseDetails").EnumerateArray().ToArray();
                if (!new[] { "core", "validation-model", "apps" }.All(id => releases.Any(x => x.GetProperty("id").GetString() == id)) ||
                    releases.Any(x => x.GetProperty("version").GetString() != PdfAValidator.Version))
                    throw new JsonException("Unexpected validator build.");
                var jobs = report.GetProperty("jobs");
                if (jobs.GetArrayLength() != 1)
                    throw new JsonException("Expected one validation job.");
                var summary = report.GetProperty("batchSummary");
                if (summary.GetProperty("totalJobs").GetInt32() != 1 ||
                    new[] { "outOfMemory", "veraExceptions", "failedEncryptedJobs", "failedParsingJobs" }
                        .Any(key => summary.GetProperty(key).GetInt32() != 0))
                    throw new JsonException("The validation batch did not complete normally.");
                var results = jobs[0].GetProperty("validationResult");
                if (results.GetArrayLength() != 1)
                    throw new JsonException("Expected one validation result.");
                var result = results[0];
                var expected = "PDF/A-" + PdfAValidator.Flavour(profile) + " validation profile";
                if (!string.Equals(result.GetProperty("profileName").GetString(), expected, StringComparison.OrdinalIgnoreCase) ||
                    result.GetProperty("jobEndStatus").GetString() != "normal")
                    throw new JsonException("Unexpected validation profile or job status.");
                var details = result.GetProperty("details");
                FailedRules = details.GetProperty("failedRules").GetInt32();
                FailedChecks = details.GetProperty("failedChecks").GetInt32();
                if (FailedRules < 0 || FailedChecks < 0 || details.GetProperty("passedChecks").GetInt32() <= 0)
                    throw new JsonException("The validation checks are incomplete.");
                IsCompliant = result.GetProperty("compliant").GetBoolean() && FailedRules == 0 && FailedChecks == 0;
                Profile = profile;
                Json = json;
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
            {
                throw new PdfOperationException("veraPDF returned an incomplete or unexpected validation report.", exception);
            }
        }
    }

    /// <summary>Output was withheld because the finished PDF did not satisfy the selected profile.</summary>
    public sealed class PdfAConformanceException : Exception
    {
        public PdfAValidationReport Report { get; }
        internal PdfAConformanceException(PdfAValidationReport report)
            : base($"The finished PDF failed {report.Profile} validation ({report.FailedRules} rules, {report.FailedChecks} checks). Inspect Report.Json for rule details. No output was written.")
        {
            Report = report;
        }
    }
}
