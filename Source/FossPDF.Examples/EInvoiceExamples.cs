using System;
using System.IO;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.PdfOperations;
using NUnit.Framework;

namespace FossPDF.Examples
{
    public class EInvoiceExamples
    {
        [Test]
        public void PackageExternallySuppliedInvoiceXml()
        {
            if (!File.Exists(Environment.GetEnvironmentVariable("FOSSPDF_MUSTANG_JAR")) ||
                !File.Exists(Environment.GetEnvironmentVariable("FOSSPDF_VERAPDF_JAR")))
                Assert.Ignore("Deploy Mustang 2.26.0, veraPDF 1.30.3 and Java 17+; see the e-invoice guide.");
            var xml = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "invoice.xml"));
            // In an application these bytes come from its invoice/accounting system. This fixture
            // is hand-authored; the visible invoice must contain the same parties, dates and amounts.
            var document = Document.Create(container => container.Page(page =>
            {
                page.Margin(30);
                page.Content().Column(column =>
                {
                    column.Item().Text("Invoice FP-2026-001, issued and delivered 2026-10-29");
                    column.Item().Text("FossPDF Example Seller GmbH, Example Street 1, 10115 Berlin, DE");
                    column.Item().Text("VAT ID DE123456789");
                    column.Item().Text("Example Buyer GmbH, Example Square 2, 20095 Hamburg, DE");
                    column.Item().Text("Report preparation: 2 x EUR 6.00 = EUR 12.00 net");
                    column.Item().Text("VAT 19%: EUR 2.28. Total EUR 14.28");
                    column.Item().Text("Transfer to DE12500105170648489890 by 2026-11-29");
                });
            })).WithMetadata(new DocumentMetadata { Title = "Invoice FP-2026-001", Language = "en-US" })
                .WithSettings(new DocumentSettings { PdfAConformance = PdfAConformance.PdfA3B });
            var bytes = document.GenerateEInvoice(xml, EInvoiceProfile.FacturX1092En16931, out var report);
            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "einvoice-example");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "invoice.pdf"), bytes);
            File.WriteAllText(Path.Combine(directory, "verapdf.json"), report.ArchivalReport.Json);
            File.WriteAllText(Path.Combine(directory, "invoice-xml-report.xml"), report.InvoiceXmlReport.Xml);
            File.WriteAllText(Path.Combine(directory, "package-report.xml"), report.PackageReport.Xml);
        }
    }
}
