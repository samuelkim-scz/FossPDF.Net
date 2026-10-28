using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using FossPDF.Drawing;
using FossPDF.Drawing.Exceptions;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;

namespace FossPDF.UnitTests
{
    [Category("PdfOptimization")]
    public class PdfOptimizationTests
    {
        [Test]
        public void LinearizedDocumentPassesTheQpdfLinearizationCheck()
        {
            var bytes = CreateDocument()
                .WithSettings(new DocumentSettings
                {
                    Linearize = true,
                    QpdfExecutablePath = QpdfTestTool.Require()
                })
                .GeneratePdf(out var pageCount);

            Assert.That(Encoding.ASCII.GetString(bytes, 0, 5), Is.EqualTo("%PDF-"));
            Assert.That(pageCount, Is.EqualTo(2));
            Assert.That(Encoding.ASCII.GetString(bytes), Does.Contain("/Linearized"));

            using var path = new TemporaryFile(bytes);
            var linearization = QpdfTestTool.Run("--check-linearization", path.Path);

            Assert.That(linearization.ExitCode, Is.EqualTo(0),
                "qpdf must accept the linearization hint tables. " + linearization.Output);

            var structure = QpdfTestTool.Run("--check", path.Path);

            Assert.That(structure.ExitCode, Is.AnyOf(0, 3),
                "qpdf must read the optimized document completely. " + structure.Output);
        }

        [Test]
        public void AdditionalCompressionGeneratesObjectStreamsAndKeepsThePages()
        {
            var bytes = CreateDocument()
                .WithSettings(new DocumentSettings
                {
                    EnableAdditionalCompression = true,
                    QpdfExecutablePath = QpdfTestTool.Require()
                })
                .GeneratePdf(out var pageCount);

            var text = Encoding.ASCII.GetString(bytes);

            Assert.That(pageCount, Is.EqualTo(2));
            Assert.That(text, Does.Contain("/ObjStm"));
            Assert.That(text, Does.Not.Contain("/Linearized"));

            using var path = new TemporaryFile(bytes);
            var structure = QpdfTestTool.Run("--check", path.Path);

            Assert.That(structure.ExitCode, Is.AnyOf(0, 3),
                "qpdf must read the compressed document completely. " + structure.Output);
        }

        [Test]
        public void LinearizationCanBeCombinedWithAdditionalCompression()
        {
            var bytes = CreateDocument()
                .WithSettings(new DocumentSettings
                {
                    EnableAdditionalCompression = true,
                    Linearize = true,
                    QpdfExecutablePath = QpdfTestTool.Require()
                })
                .GeneratePdf();

            var text = Encoding.ASCII.GetString(bytes);

            Assert.That(text, Does.Contain("/Linearized"));
            Assert.That(text, Does.Contain("/ObjStm"));

            using var path = new TemporaryFile(bytes);

            Assert.That(QpdfTestTool.Run("--check-linearization", path.Path).ExitCode, Is.EqualTo(0));
        }

        [Test]
        public void DisablingCompressionProducesALargerUncompressedDocument()
        {
            var normal = CreateDocument().GeneratePdf();
            var uncompressed = CreateDocument()
                .WithSettings(new DocumentSettings
                {
                    CompressDocument = false,
                    QpdfExecutablePath = QpdfTestTool.Require()
                })
                .GeneratePdf();

            Assert.That(uncompressed.Length, Is.GreaterThan(normal.Length));
            Assert.That(Encoding.ASCII.GetString(uncompressed), Does.Not.Contain("/FlateDecode"));

            using var path = new TemporaryFile(uncompressed);

            Assert.That(QpdfTestTool.Run("--check", path.Path).ExitCode, Is.AnyOf(0, 3));
        }

        [Test]
        public void MissingQpdfIsRejectedBeforeCompositionAndLeavesTheCallerStreamUntouched()
        {
            var composed = false;
            var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "qpdf-missing.exe");

            var document = Document.Create(container =>
            {
                composed = true;
                container.Page(page => page.Content().Text("must not be composed"));
            }).WithSettings(new DocumentSettings { Linearize = true, QpdfExecutablePath = missing });

            using var stream = new MemoryStream();
            stream.WriteByte(42);

            Assert.Throws<NotSupportedException>(() => document.GeneratePdf(stream));
            Assert.That(composed, Is.False);
            Assert.That(stream.ToArray(), Is.EqualTo(new byte[] { 42 }));
        }

        [Test]
        public void ContradictoryCompressionOptionsAreRejectedBeforeComposition()
        {
            var composed = false;

            var document = Document.Create(container =>
            {
                composed = true;
                container.Page(page => page.Content().Text("must not be composed"));
            }).WithSettings(new DocumentSettings
            {
                CompressDocument = false,
                EnableAdditionalCompression = true
            });

            using var stream = new MemoryStream();

            Assert.Throws<ArgumentException>(() => document.GeneratePdf(stream));
            Assert.That(composed, Is.False);
            Assert.That(stream.Length, Is.EqualTo(0));
        }

        [Test]
        public void OptimizeValidatesItsArguments()
        {
            var bytes = CreateDocument().GeneratePdf();

            Assert.Throws<ArgumentNullException>(() => PdfOptimizer.Optimize(null, new PdfOptimizationSettings()));
            Assert.Throws<ArgumentNullException>(() => PdfOptimizer.Optimize(bytes, null));
            Assert.Throws<ArgumentException>(() => PdfOptimizer.Optimize(Array.Empty<byte>(), new PdfOptimizationSettings()));
            Assert.Throws<ArgumentException>(() => PdfOptimizer.Optimize(bytes, new PdfOptimizationSettings
            {
                EnableAdditionalCompression = true,
                DisableCompression = true
            }));
            Assert.Throws<ArgumentOutOfRangeException>(() => PdfOptimizer.Optimize(bytes, new PdfOptimizationSettings
            {
                Linearize = true,
                ProcessTimeout = TimeSpan.Zero
            }));
            Assert.Throws<ArgumentException>(() => PdfOptimizer.Optimize(bytes, new PdfOptimizationSettings
            {
                Linearize = true,
                QpdfExecutablePath = "   "
            }));
        }

        [Test]
        public void EmptyOptionsReturnTheInputWithoutStartingQpdf()
        {
            var bytes = CreateDocument().GeneratePdf();

            Assert.That(PdfOptimizer.Optimize(bytes, new PdfOptimizationSettings()), Is.SameAs(bytes));
        }

        [TestCase("qpdf version 10.6.3", false)]
        [TestCase("qpdf version 9.1.0", false)]
        [TestCase("qpdf version 11.0.0", true)]
        [TestCase("qpdf version 12.4.2", true)]
        [TestCase("not a qpdf version", false)]
        public void SupportedVersionsRequireQpdfElevenOrNewer(string version, bool supported)
        {
            if (supported)
                Assert.DoesNotThrow(() => PdfOptimizer.EnsureSupportedVersion(version));
            else
                Assert.Throws<NotSupportedException>(() => PdfOptimizer.EnsureSupportedVersion(version));
        }

        [Test]
        public void CancellationIsPropagatedToTheBackend()
        {
            var bytes = CreateDocument().GeneratePdf();
            var backend = new FakeQpdfBackend();
            using var cancellation = new CancellationTokenSource();

            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() => PdfOptimizer.Optimize(
                bytes,
                new PdfOptimizationSettings { Linearize = true, QpdfExecutablePath = ExistingFile() },
                backend,
                cancellation.Token));

            Assert.That(backend.VersionCalls, Is.EqualTo(0));
            Assert.That(backend.RunCalls, Is.EqualTo(0));
        }

        [Test]
        public void CancellationIsPassedToARunningBackend()
        {
            var bytes = CreateDocument().GeneratePdf();
            using var cancellation = new CancellationTokenSource();
            var backend = new FakeQpdfBackend
            {
                OnRun = token =>
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                }
            };

            Assert.Throws<OperationCanceledException>(() => PdfOptimizer.Optimize(
                bytes,
                new PdfOptimizationSettings { Linearize = true, QpdfExecutablePath = ExistingFile() },
                backend,
                cancellation.Token));

            Assert.That(backend.RunCalls, Is.EqualTo(1));
        }

        [Test]
        public void BackendFailureIsReportedAsAnOptimizationException()
        {
            var bytes = CreateDocument().GeneratePdf();
            var backend = new FakeQpdfBackend
            {
                OnRun = _ => throw new PdfOptimizationException("qpdf failed with exit code 2. simulated.")
            };

            var exception = Assert.Throws<PdfOptimizationException>(() => PdfOptimizer.Optimize(
                bytes,
                new PdfOptimizationSettings { Linearize = true, QpdfExecutablePath = ExistingFile() },
                backend,
                CancellationToken.None));

            Assert.That(exception.Message, Does.Contain("simulated"));
        }

        [Test]
        public void ProcessBackendReportsAControllableFailure()
        {
            // "dotnet" exists wherever these tests run and rejects the qpdf-style arguments with a
            // non-zero exit code, which exercises the real process launch and failure reporting.
            var backend = new QpdfProcessBackend();

            var exception = Assert.Throws<PdfOptimizationException>(() => backend.Run(
                "dotnet",
                new[] { "--fosspdf-not-a-real-option" },
                CreateDocument().GeneratePdf(),
                TimeSpan.FromMinutes(2),
                CancellationToken.None));

            Assert.That(exception.Message, Does.Contain("exit code"));
        }

        [Test]
        public void ProcessBackendRejectsAMissingExecutable()
        {
            var backend = new QpdfProcessBackend();
            var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "qpdf-missing.exe");

            Assert.Throws<PdfOptimizationException>(() => backend.GetVersion(
                missing, TimeSpan.FromMinutes(1), CancellationToken.None));
        }

        private static string ExistingFile() => typeof(PdfOptimizationTests).Assembly.Location;

        private static IDocument CreateDocument() => Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(160, 120);
                page.Margin(12);
                page.Content().Column(column =>
                {
                    column.Item().Text("FossPDF PDF optimization fixture");
                    column.Item().Text("Compact content that fits on one page.");
                });
            });

            container.Page(page =>
            {
                page.Size(160, 120);
                page.Margin(12);
                page.Content().Text("Second page");
            });
        });

        private sealed class FakeQpdfBackend : IQpdfBackend
        {
            public Action<CancellationToken> OnRun { get; set; } = _ => { };
            public int VersionCalls { get; private set; }
            public int RunCalls { get; private set; }

            public string GetVersion(string executablePath, TimeSpan timeout, CancellationToken cancellationToken)
            {
                VersionCalls++;
                return "qpdf version 12.4.2";
            }

            public byte[] Run(
                string executablePath,
                System.Collections.Generic.IReadOnlyList<string> arguments,
                byte[] input,
                TimeSpan timeout,
                CancellationToken cancellationToken)
            {
                RunCalls++;
                OnRun(cancellationToken);
                return input;
            }
        }

        private sealed class TemporaryFile : IDisposable
        {
            public TemporaryFile(byte[] bytes)
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "fosspdf-opt-" + Guid.NewGuid().ToString("N") + ".pdf");
                File.WriteAllBytes(Path, bytes);
            }

            public string Path { get; }

            public void Dispose()
            {
                try
                {
                    File.Delete(Path);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
