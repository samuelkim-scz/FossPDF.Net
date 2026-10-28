using System;
using System.IO;
using System.Text;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.PdfOperations;
using NUnit.Framework;

namespace FossPDF.Examples
{
    /// <summary>
    /// Shows how overlay and underlay files are applied to an existing PDF. The layer pages are
    /// imported as form XObjects, so their text and vector content stay selectable instead of being
    /// rasterized into the result.
    /// </summary>
    public class PdfLayerExamples
    {
        [Test]
        public void ApplyOverlayAndUnderlayFiles()
        {
            var directory = CreateOutputDirectory("pdf-operations-layers");

            var sourcePath = Path.Combine(directory, "invoice.pdf");
            var underlayPath = Path.Combine(directory, "draft-underlay.pdf");
            var overlayPath = Path.Combine(directory, "approved-overlay.pdf");
            var outputPath = Path.Combine(directory, "stamped-invoice.pdf");

            File.WriteAllBytes(sourcePath, CreateInvoice());
            File.WriteAllBytes(underlayPath, CreateUnderlay());
            File.WriteAllBytes(overlayPath, CreateOverlay());

            // The underlay is drawn beneath page 1, the overlay above it. Both layers are ordinary
            // PDF files; only the selected pages are touched.
            DocumentOperation
                .LoadFile(sourcePath)
                .UnderlayFile(new DocumentOperation.LayerConfiguration
                {
                    FilePath = underlayPath,
                    TargetPages = "1"
                })
                .OverlayFile(new DocumentOperation.LayerConfiguration
                {
                    FilePath = overlayPath,
                    TargetPages = "1"
                })
                .Save(outputPath);

            var result = File.ReadAllBytes(outputPath);

            Assert.That(Encoding.ASCII.GetString(result, 0, 5), Is.EqualTo("%PDF-"));
            Assert.That(new FileInfo(outputPath).Length, Is.GreaterThan(0));
            Assert.That(File.ReadAllBytes(sourcePath), Is.Not.EqualTo(result),
                "the stamped document differs from the input");
        }

        [Test]
        public void RepeatOneLayerPageAcrossTheRemainingPages()
        {
            var directory = CreateOutputDirectory("pdf-operations-layer-repeat");

            var sourcePath = Path.Combine(directory, "report.pdf");
            var overlayPath = Path.Combine(directory, "page-number-overlay.pdf");
            var outputPath = Path.Combine(directory, "numbered-report.pdf");

            File.WriteAllBytes(sourcePath, CreateReport());
            File.WriteAllBytes(overlayPath, CreateOverlay());

            // Page 2 onwards receive the single overlay page; repetition cycles the source pages.
            DocumentOperation
                .LoadFile(sourcePath)
                .OverlayFile(new DocumentOperation.LayerConfiguration
                {
                    FilePath = overlayPath,
                    TargetPages = "2-z",
                    RepeatSourcePages = "1"
                })
                .Save(outputPath);

            var result = File.ReadAllBytes(outputPath);

            Assert.That(Encoding.ASCII.GetString(result, 0, 5), Is.EqualTo("%PDF-"));

            // Reloading proves the result is usable and still has all three pages.
            Assert.That(
                DocumentOperation.Load(result).TakePages("3").SaveToBytes(),
                Is.Not.Empty);
        }

        private static byte[] CreateInvoice()
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(300, 200);
                    page.Margin(20);

                    page.Content().Column(column =>
                    {
                        column.Item().Text("Invoice 2026-001").FontSize(20).Bold();
                        column.Item().PaddingTop(10).Text("Consulting services");

                        column.Item().PaddingTop(20).Row(row =>
                        {
                            row.RelativeItem().Text("Quantity");
                            row.ConstantItem(80).AlignRight().Text("Amount");
                        });

                        column.Item().PaddingTop(8).Text("Total: 1,200.00 EUR").Bold();
                    });
                });
            }).GeneratePdf();
        }

        private static byte[] CreateReport()
        {
            return Document.Create(container =>
            {
                for (var index = 0; index < 3; index++)
                {
                    var pageNumber = index + 1;

                    container.Page(page =>
                    {
                        page.Size(300, 200);
                        page.Margin(20);

                        page.Content().Column(column =>
                        {
                            column.Item().Text($"Report section {pageNumber}").FontSize(18).Bold();
                            column.Item().PaddingTop(10).Text(
                                "This page is generated by FossPDF and receives an overlay afterwards.");
                        });
                    });
                }
            }).GeneratePdf();
        }

        private static byte[] CreateUnderlay()
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(300, 200);
                    page.Margin(0);

                    // A transparent wash and large watermark text show that the underlay sits
                    // behind the original page content.
                    page.Content().Background("#fff59d").AlignCenter().AlignMiddle()
                        .Text("DRAFT")
                        .FontSize(72)
                        .Bold()
                        .FontColor("#f9a825");
                });
            }).GeneratePdf();
        }

        private static byte[] CreateOverlay()
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(300, 200);
                    page.Margin(10);

                    page.Content().AlignBottom().AlignRight()
                        .Background("#2e7d32")
                        .Padding(6)
                        .Text("APPROVED")
                        .FontSize(14)
                        .Bold()
                        .FontColor("#ffffff");
                });
            }).GeneratePdf();
        }

        private static string CreateOutputDirectory(string name)
        {
            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, name);
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
