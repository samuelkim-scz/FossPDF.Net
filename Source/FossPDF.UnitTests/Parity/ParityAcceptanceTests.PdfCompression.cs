#if NET9_0
using System;
using System.IO;
using System.Linq;
using System.Text;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;

namespace FossPDF.UnitTests.Parity
{
    /// <summary>
    /// FP-306 acceptance: apply explicit additional compression and linearization both while
    /// generating a document and while saving an existing one, measure the effect instead of
    /// assuming it, and prove with an independent tool that the result is linearized and
    /// structurally complete. qpdf is the external optimizer; the test skips when it is absent.
    /// The operations companion targets net9.0 only, so this runner is compiled for that
    /// framework; the fixture declares frameworks: ["net9.0"].
    /// </summary>
    public partial class ParityAcceptanceTests
    {
        [ParityFixtureRunner("PDF-306")]
        private static void RunPdfCompression(ParityFixture fixture)
        {
            var catalog = FixtureCatalog.Load();
            var sourceId = fixture.Input.GetProperty("source").GetString();
            var sourceFixture = catalog.Fixtures.Single(x => x.Id == sourceId);

            var options = fixture.Input.GetProperty("options");
            var additionalCompression = options.GetProperty("additional_compression").GetBoolean();
            var linearize = options.GetProperty("linearize").GetBoolean();

            var qpdf = QpdfTestTool.Require();

            var source = ExplicitPagination(sourceFixture).GeneratePdf();

            var settings = new DocumentSettings
            {
                EnableAdditionalCompression = additionalCompression,
                Linearize = linearize,
                QpdfExecutablePath = qpdf
            };

            var generated = ExplicitPagination(sourceFixture)
                .WithSettings(settings)
                .GeneratePdf();

            var optimized = DocumentOperation
                .Load(source)
                .EnableAdditionalCompression()
                .Linearize()
                .UseQpdfExecutable(qpdf)
                .SaveToBytes();

            SaveArtifact(fixture.Id, "source.pdf", source);
            SaveArtifact(fixture.Id, "generated.pdf", generated);
            SaveArtifact(fixture.Id, "optimized.pdf", optimized);

            Assert.That(Encoding.ASCII.GetString(generated, 0, 5), Is.EqualTo("%PDF-"));
            Assert.That(Encoding.ASCII.GetString(optimized, 0, 5), Is.EqualTo("%PDF-"));

            Assert.That(Encoding.ASCII.GetString(generated), Does.Contain("/Linearized"),
                "PDF-306: the generated document carries the linearization parameter dictionary.");
            Assert.That(Encoding.ASCII.GetString(optimized), Does.Contain("/Linearized"),
                "PDF-306: the optimized existing document carries the linearization parameter dictionary.");

            if (additionalCompression)
            {
                Assert.That(Encoding.ASCII.GetString(generated), Does.Contain("/ObjStm"),
                    "PDF-306: additional compression generates object streams in the generated document.");
                Assert.That(Encoding.ASCII.GetString(optimized), Does.Contain("/ObjStm"),
                    "PDF-306: additional compression generates object streams in the optimized document.");
            }

            var sourcePages = ReadPageFacts(source);
            var generatedPages = ReadPageFacts(generated);
            var optimizedPages = ReadPageFacts(optimized);

            Assert.That(generatedPages.Length, Is.EqualTo(sourcePages.Length),
                "PDF-306: the generated document keeps every page.");
            Assert.That(optimizedPages.Length, Is.EqualTo(sourcePages.Length),
                "PDF-306: the optimized document keeps every page.");

            for (var index = 0; index < sourcePages.Length; index++)
            {
                Assert.That(generatedPages[index].Content, Is.EqualTo(sourcePages[index].Content),
                    $"PDF-306: generated page {index + 1} renders the same content.");
                Assert.That(optimizedPages[index].Content, Is.EqualTo(sourcePages[index].Content),
                    $"PDF-306: optimized page {index + 1} renders the same content.");
                Assert.That(generatedPages[index].Width, Is.EqualTo(sourcePages[index].Width).Within(0.01));
                Assert.That(generatedPages[index].Height, Is.EqualTo(sourcePages[index].Height).Within(0.01));
                Assert.That(optimizedPages[index].Width, Is.EqualTo(sourcePages[index].Width).Within(0.01));
                Assert.That(optimizedPages[index].Height, Is.EqualTo(sourcePages[index].Height).Within(0.01));
            }

            Assert.That(ReadTitle(generated), Is.EqualTo(ReadTitle(source)),
                "PDF-306: the generated document keeps its metadata.");
            Assert.That(ReadTitle(optimized), Is.EqualTo(ReadTitle(source)),
                "PDF-306: the optimized document keeps its metadata.");

            using (var sourcePath = new TemporaryFile(source))
            using (var generatedPath = new TemporaryFile(generated))
            using (var optimizedPath = new TemporaryFile(optimized))
            {
                foreach (var (label, path) in new[]
                         {
                             ("source", sourcePath.Path),
                             ("generated", generatedPath.Path),
                             ("optimized", optimizedPath.Path)
                         })
                {
                    var structure = QpdfTestTool.Run("--check", path);

                    Assert.That(structure.ExitCode, Is.AnyOf(0, 3),
                        $"PDF-306: qpdf reads the {label} document completely. " + structure.Output);
                }

                foreach (var (label, path) in new[]
                         {
                             ("generated", generatedPath.Path),
                             ("optimized", optimizedPath.Path)
                         })
                {
                    var linearization = QpdfTestTool.Run("--check-linearization", path);

                    Assert.That(linearization.ExitCode, Is.EqualTo(0),
                        $"PDF-306: qpdf accepts the {label} linearization data. " + linearization.Output);
                }
            }

            // Size is measured and reported, never asserted as a universal improvement: object
            // streams and hint tables can grow a very small document.
            var measurements = string.Join(Environment.NewLine, new[]
            {
                $"source_bytes={source.Length}",
                $"generated_bytes={generated.Length}",
                $"optimized_bytes={optimized.Length}",
                $"generated_delta_bytes={generated.Length - source.Length}",
                $"optimized_delta_bytes={optimized.Length - source.Length}"
            }) + Environment.NewLine;

            SaveArtifact(fixture.Id, "measurements.txt", Encoding.UTF8.GetBytes(measurements));
            TestContext.Out.WriteLine(measurements);

            AssertUnsupportedCombinationsFail(source);
        }

        private static void AssertUnsupportedCombinationsFail(byte[] source)
        {
            Assert.Throws<InvalidOperationException>(
                () => DocumentOperation.Load(source).EnableAdditionalCompression().DisableCompression(),
                "PDF-306: enabling and disabling compression at once fails clearly.");

            Assert.Throws<InvalidOperationException>(
                () => DocumentOperation.Load(source)
                    .Linearize()
                    .Encrypt(new DocumentOperation.Encryption256Bit { OwnerPassword = "owner" })
                    .SaveToBytes(),
                "PDF-306: optimization cannot silently drop encryption, so the combination fails clearly.");
        }

        private sealed class TemporaryFile : IDisposable
        {
            public TemporaryFile(byte[] bytes)
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "fosspdf-parity-" + Guid.NewGuid().ToString("N") + ".pdf");
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
#endif
