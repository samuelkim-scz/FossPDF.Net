using System;
using System.Linq;

namespace FossPDF.PdfOperations
{
    /// <summary>Explicitly qualified releases and profiles. Other versions/profiles are unsupported.</summary>
    public enum EInvoiceProfile
    {
        FacturX1092En16931,
        Zugferd252En16931
    }

    /// <summary>Application-deployed validators, additional associated files and custom XMP.</summary>
    public sealed record EInvoiceOptions : PdfAOptions
    {
        /// <summary>Unmodified Mustang-CLI-2.26.0.jar. Defaults to FOSSPDF_MUSTANG_JAR. Java 17+ is required.</summary>
        public string? MustangJarPath { get; init; }
    }

    /// <summary>Original Mustang report. Warnings and notices remain available in Xml.</summary>
    public sealed class EInvoiceValidationReport
    {
        public bool IsValid { get; }
        public bool IsPackagedPdf { get; }
        public string Xml { get; }

        internal EInvoiceValidationReport(string xml, bool packaged)
        {
            Xml = xml;
            IsPackagedPdf = packaged;
            var root = EInvoicePackaging.ParseXml(System.Text.Encoding.UTF8.GetBytes(xml)).Root;
            var invoice = root?.Element("xml");
            var info = invoice?.Element("info");
            if (root?.Name != "validation" || root.Elements("summary").Count() != 1 ||
                invoice?.Elements("summary").Count() != 1 ||
                (string?)info?.Element("validator")?.Attribute("version") != MustangValidator.Version ||
                (string?)info?.Element("profile") != EInvoicePackaging.Guideline ||
                (string?)info?.Element("version") != "2" ||
                !int.TryParse((string?)info?.Element("rules")?.Element("fired"), out var fired) || fired <= 0 ||
                packaged && root.Element("pdf")?.Elements("summary").Count() != 1)
                throw new PdfOperationException("Mustang returned an incomplete or unexpected validation report.");
            bool Valid(System.Xml.Linq.XElement element) => (string?)element.Element("summary")?.Attribute("status") switch
            {
                "valid" => true,
                "invalid" => false,
                _ => throw new PdfOperationException("Mustang returned an unknown validation status.")
            };
            IsValid = Valid(root) && Valid(invoice!) && (!packaged || Valid(root.Element("pdf")!)) &&
                !root.Descendants().Any(x => x.Name.LocalName is "error" or "fatal" or "exception");
        }
    }

    /// <summary>Evidence for the supplied XML and the final, optionally optimized PDF.</summary>
    public sealed record EInvoiceGenerationReport(EInvoiceProfile Profile,
        EInvoiceValidationReport InvoiceXmlReport, EInvoiceValidationReport PackageReport,
        PdfAValidationReport ArchivalReport, PdfUaValidationReport? AccessibilityReport);

    /// <summary>The XML or finished package failed Mustang checks; no caller output was written.</summary>
    public sealed class EInvoiceConformanceException : Exception
    {
        public EInvoiceValidationReport Report { get; }
        internal EInvoiceConformanceException(EInvoiceValidationReport report)
            : base($"Mustang rejected the {(report.IsPackagedPdf ? "packaged invoice" : "invoice XML")}. Inspect Report.Xml. No output was written.") => Report = report;
    }
}
