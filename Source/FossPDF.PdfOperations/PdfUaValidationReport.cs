using System;
using System.Threading;

namespace FossPDF.PdfOperations
{
    /// <summary>Validator, XMP and associated-file settings shared with the archival writer.</summary>
    public sealed record PdfUaOptions : PdfAOptions;

    /// <summary>
    /// PDF/UA-1 machine-check evidence. Human checks of reading order, alternatives, language and
    /// usability are still required; a passing automated report cannot perform those checks.
    /// </summary>
    public sealed class PdfUaValidationReport
    {
        public bool MachineChecksPassed { get; }
        public int FailedRules { get; }
        public int FailedChecks { get; }
        public string Json { get; }
        /// <summary>The archival validation report when generation also selected PDF/A.</summary>
        public PdfAValidationReport? ArchivalReport { get; }

        internal PdfUaValidationReport(string json, PdfAValidationReport? archivalReport = null)
        {
            var data = new PdfValidationReportData(json, "PDF/UA-1 validation profile");
            MachineChecksPassed = data.IsCompliant;
            FailedRules = data.FailedRules;
            FailedChecks = data.FailedChecks;
            Json = json;
            ArchivalReport = archivalReport;
        }
    }

    /// <summary>The finished document failed PDF/UA-1 machine checks and was withheld.</summary>
    public sealed class PdfUaConformanceException : Exception
    {
        public PdfUaValidationReport Report { get; }
        internal PdfUaConformanceException(PdfUaValidationReport report)
            : base($"PDF/UA-1 machine checks failed ({report.FailedRules} rules, {report.FailedChecks} checks). Inspect Report.Json. No output was written.") => Report = report;
    }

    /// <summary>Runs the pinned independent PDF/UA-1 machine checks without modifying the PDF.</summary>
    public static class PdfUaValidator
    {
        public static PdfUaValidationReport Validate(byte[] document, PdfUaOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (document.Length == 0)
                throw new ArgumentException("The PDF must not be empty.", nameof(document));
            options ??= new PdfUaOptions();
            var jar = PdfAValidator.Prepare(options, cancellationToken);
            return new PdfUaValidationReport(PdfAValidator.ReadReport(document, "ua1", options, jar, cancellationToken));
        }
    }
}
