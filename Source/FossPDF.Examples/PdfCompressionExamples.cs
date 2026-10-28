using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Helpers;
using FossPDF.Infrastructure;
using FossPDF.PdfOperations;
using NUnit.Framework;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace FossPDF.Examples
{
    /// <summary>
    /// Shows the explicit PDF compression and linearization controls. Additional compression and
    /// linearization are performed by the external qpdf tool (version 11 or newer); the examples
    /// skip when qpdf cannot be found. Every example writes its artifacts under the test directory
    /// and prints the measured sizes.
    /// </summary>
    public class PdfCompressionExamples
    {
        [Test]
        public void GenerateWithAdditionalCompressionAndLinearization()
        {
            var directory = CreateOutputDirectory("pdf-optimization-generated");
            var qpdf = RequireQpdf();

            var normal = CreateReport().GeneratePdf();

            // The settings are applied after SkiaSharp has produced the document. Both flags can be
            // combined: object streams reduce the size, linearization reorders the file so the first
            // page can be shown before the rest is downloaded.
            var optimized = CreateReport()
                .WithSettings(new DocumentSettings
                {
                    EnableAdditionalCompression = true,
                    Linearize = true,
                    QpdfExecutablePath = qpdf
                })
                .GeneratePdf();

            var normalPath = Path.Combine(directory, "report.pdf");
            var optimizedPath = Path.Combine(directory, "report-optimized.pdf");

            File.WriteAllBytes(normalPath, normal);
            File.WriteAllBytes(optimizedPath, optimized);

            var optimizedText = Encoding.Latin1.GetString(optimized);

            Assert.That(optimizedText, Does.Contain("/Linearized"));
            Assert.That(optimizedText, Does.Contain("/ObjStm"));
            Assert.That(ReadPageCount(optimized), Is.EqualTo(ReadPageCount(normal)));

            var check = RunQpdf(qpdf, "--check-linearization", optimizedPath);
            Assert.That(check.ExitCode, Is.EqualTo(0), check.Output);

            ReportSizes("generated text-heavy document", normal.Length, optimized.Length);
        }

        [Test]
        public void OptimizeAnExistingPdfAndKeepItsAttachment()
        {
            var directory = CreateOutputDirectory("pdf-optimization-existing");
            var qpdf = RequireQpdf();

            var sourcePath = Path.Combine(directory, "invoice.pdf");
            var attachmentPath = Path.Combine(directory, "terms.txt");
            var outputPath = Path.Combine(directory, "invoice-optimized.pdf");

            File.WriteAllBytes(sourcePath, CreateInvoice().GeneratePdf());
            File.WriteAllText(attachmentPath, "Payment is due within 30 days.\n", Encoding.UTF8);

            // Existing documents use the same optimizer through the operation API. The attachment
            // and metadata survive because qpdf rewrites the object graph rather than rasterizing.
            DocumentOperation
                .LoadFile(sourcePath)
                .AddAttachment(new DocumentOperation.DocumentAttachment
                {
                    FilePath = attachmentPath,
                    MimeType = "text/plain",
                    Relationship = DocumentOperation.DocumentAttachmentRelationship.Supplement
                })
                .EnableAdditionalCompression()
                .Linearize()
                .UseQpdfExecutable(qpdf)
                .Save(outputPath);

            var source = File.ReadAllBytes(sourcePath);
            var output = File.ReadAllBytes(outputPath);

            Assert.That(Encoding.Latin1.GetString(output), Does.Contain("/Linearized"));
            Assert.That(ReadPageCount(output), Is.EqualTo(1));
            Assert.That(ReadTitle(output), Is.EqualTo("Invoice 2026-002"));
            Assert.That(ReadAttachmentKey(output), Is.EqualTo("terms.txt"));

            var check = RunQpdf(qpdf, "--check", outputPath);
            Assert.That(check.ExitCode, Is.AnyOf(0, 3), check.Output);

            ReportSizes("existing invoice with attachment", source.Length, output.Length);
        }

        [Test]
        public void MeasureAnImageHeavyDocument()
        {
            var directory = CreateOutputDirectory("pdf-optimization-images");
            var qpdf = RequireQpdf();

            var normal = CreateImageReport().GeneratePdf();
            var optimized = CreateImageReport()
                .WithSettings(new DocumentSettings
                {
                    EnableAdditionalCompression = true,
                    QpdfExecutablePath = qpdf
                })
                .GeneratePdf();

            File.WriteAllBytes(Path.Combine(directory, "image-report.pdf"), normal);
            File.WriteAllBytes(Path.Combine(directory, "image-report-optimized.pdf"), optimized);

            Assert.That(ReadPageCount(optimized), Is.EqualTo(ReadPageCount(normal)));
            Assert.That(Encoding.Latin1.GetString(optimized), Does.Contain("/ObjStm"));

            ReportSizes("image-heavy document (three 600x400 images)", normal.Length, optimized.Length);
        }

        [Test]
        public void DisableCompressionForAnInspectablePdf()
        {
            var directory = CreateOutputDirectory("pdf-optimization-uncompressed");
            var qpdf = RequireQpdf();

            var compressed = CreateInvoice().GeneratePdf();

            // Disabling compression makes streams readable in a text editor. It trades size for
            // inspection; it cannot be combined with additional compression.
            var uncompressed = CreateInvoice()
                .WithSettings(new DocumentSettings
                {
                    CompressDocument = false,
                    QpdfExecutablePath = qpdf
                })
                .GeneratePdf();

            File.WriteAllBytes(Path.Combine(directory, "invoice.pdf"), compressed);
            File.WriteAllBytes(Path.Combine(directory, "invoice-uncompressed.pdf"), uncompressed);

            Assert.That(uncompressed.Length, Is.GreaterThan(compressed.Length));
            Assert.That(Encoding.Latin1.GetString(uncompressed), Does.Not.Contain("/FlateDecode"));
            Assert.That(ReadPageCount(uncompressed), Is.EqualTo(1));

            ReportSizes("inspectable invoice", compressed.Length, uncompressed.Length);
        }

        private static Document CreateReport()
        {
            return Document.Create(container =>
            {
                for (var pageIndex = 0; pageIndex < 3; pageIndex++)
                {
                    var currentPage = pageIndex;

                    container.Page(page =>
                    {
                        page.Size(400, 300);
                        page.Margin(24);

                        page.Content().Column(column =>
                        {
                            column.Item().Text($"Quarterly report — page {currentPage + 1}").FontSize(18).Bold();

                            for (var paragraph = 0; paragraph < 8; paragraph++)
                                column.Item().PaddingTop(6).Text(Placeholders.LoremIpsum()).FontSize(9);
                        });
                    });
                }
            });
        }

        private static Document CreateImageReport()
        {
            // Three pages with one 600x400 placeholder image each. The image bytes are already
            // compressed, so this shows how little extra compression can do for image-heavy output.
            var image = Placeholders.Image(600, 400);

            return Document.Create(container =>
            {
                for (var pageIndex = 0; pageIndex < 3; pageIndex++)
                {
                    var currentPage = pageIndex;

                    container.Page(page =>
                    {
                        page.Size(400, 320);
                        page.Margin(20);

                        page.Content().Column(column =>
                        {
                            column.Item().Text($"Image page {currentPage + 1}").FontSize(16).Bold();
                            column.Item().PaddingTop(8).Image(image);
                        });
                    });
                }
            });
        }

        private static Document CreateInvoice()
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(300, 200);
                    page.Margin(20);

                    page.Content().Column(column =>
                    {
                        column.Item().Text("Invoice 2026-002").FontSize(20).Bold();
                        column.Item().PaddingTop(10).Text("Consulting services");
                        column.Item().PaddingTop(20).Text("Total: 1,200.00 EUR").Bold();
                    });
                });
            }).WithMetadata(new DocumentMetadata { Title = "Invoice 2026-002" });
        }

        private static int ReadPageCount(byte[] pdf)
        {
            using var stream = new MemoryStream(pdf, writable: false);
            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            return document.PageCount;
        }

        private static string ReadTitle(byte[] pdf)
        {
            using var stream = new MemoryStream(pdf, writable: false);
            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            return document.Info.Title;
        }

        private static string ReadAttachmentKey(byte[] pdf)
        {
            using var stream = new MemoryStream(pdf, writable: false);
            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

            var names = document.Internals.Catalog.Elements.GetDictionary("/Names");
            var embedded = names?.Elements.GetDictionary("/EmbeddedFiles");
            var entries = embedded?.Elements.GetArray("/Names");

            Assert.That(entries, Is.Not.Null, "the optimized document keeps its embedded-files tree.");

            return ((PdfString)entries.Elements[0]).Value;
        }

        private static void ReportSizes(string description, int before, int after)
        {
            TestContext.Out.WriteLine(
                $"{description}: {before} bytes -> {after} bytes (delta {after - before:+0;-0;0} bytes)");
        }

        private static (int ExitCode, string Output) RunQpdf(string executable, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return (process.ExitCode, output + error);
        }

        private static string RequireQpdf()
        {
            var path = FindQpdf();

            if (path == null)
                Assert.Ignore("qpdf 11.0 or newer is required for PDF optimization examples and was not found.");

            return path;
        }

        private static string FindQpdf()
        {
            var explicitPath = Environment.GetEnvironmentVariable("FOSSPDF_QPDF");

            if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
                return explicitPath;

            var fromPath = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator)
                .Where(directory => !string.IsNullOrWhiteSpace(directory))
                .SelectMany(directory => new[] { "qpdf", "qpdf.exe" }.Select(name => Path.Combine(directory.Trim(), name)))
                .FirstOrDefault(File.Exists);

            if (fromPath != null)
                return fromPath;

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new[] { "/usr/bin/qpdf", "/usr/local/bin/qpdf", "/opt/homebrew/bin/qpdf", "/opt/local/bin/qpdf" }
                    .FirstOrDefault(File.Exists);
            }

            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };

            return roots
                .Where(Directory.Exists)
                .SelectMany(root => Directory.GetDirectories(root, "qpdf*"))
                .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(directory => Path.Combine(directory, "bin", "qpdf.exe"))
                .FirstOrDefault(File.Exists);
        }

        private static string CreateOutputDirectory(string name)
        {
            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, name);
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
