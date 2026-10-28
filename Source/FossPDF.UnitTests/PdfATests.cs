#if NET9_0
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.PdfOperations;
using NUnit.Framework;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace FossPDF.UnitTests
{
    /// <summary>
    /// FP-402: selectable PDF/A-2 and PDF/A-3 conformance levels. Every supported level is written
    /// from a tagged semantic document; identification metadata, output intents, fonts, Unicode
    /// mappings, language and attachment rules are asserted from the produced bytes. Independent
    /// validation of the artifacts is performed by verify_pdfa.py with veraPDF.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class PdfATests
    {
        private const string Language = "en-US";
        private const string Title = "Accessible fixture";

        public static IEnumerable<TestCaseData> Profiles()
        {
            yield return new TestCaseData(PdfAConformance.PdfA2A).SetName("PdfA_2a");
            yield return new TestCaseData(PdfAConformance.PdfA2B).SetName("PdfA_2b");
            yield return new TestCaseData(PdfAConformance.PdfA2U).SetName("PdfA_2u");
            yield return new TestCaseData(PdfAConformance.PdfA3A).SetName("PdfA_3a");
            yield return new TestCaseData(PdfAConformance.PdfA3B).SetName("PdfA_3b");
            yield return new TestCaseData(PdfAConformance.PdfA3U).SetName("PdfA_3u");
        }

        [TestCaseSource(nameof(Profiles))]
        public void EveryProfileWritesItsIdentificationAndConformanceStructures(PdfAConformance profile)
        {
            var bytes = AccessibleDocument().GeneratePdfA(profile);

            Artifact(Name(profile), bytes);

            var (part, conformance) = Expected(profile);

            Encoding.ASCII.GetString(bytes, 0, 8).Should().Be("%PDF-1.7",
                $"{Name(profile)} is based on PDF 1.7");
            Xmp(bytes).Should().Contain($"<pdfaid:part>{part}</pdfaid:part>")
                .And.Contain($"<pdfaid:conformance>{conformance}</pdfaid:conformance>");

            using var pdf = Open(bytes);
            AssertOutputIntent(pdf);
            AssertTaggedStructure(pdf);
            pdf.Internals.Catalog.Elements.GetString("/Lang").Should().Be(Language);

            var text = Encoding.ASCII.GetString(bytes);
            text.Should().Contain("/ToUnicode", "all text must map glyphs to Unicode");
            text.Should().Contain("/FontFile2", "all fonts must be embedded");

            var info = pdf.Info;
            info.Title.Should().Be(Title);
        }

        [Test]
        public void SettingsSelectTheProfileForGeneratePdfA()
        {
            var document = AccessibleDocument(new DocumentSettings
            {
                PdfAConformance = PdfAConformance.PdfA3U
            });

            var bytes = document.GeneratePdfA();

            Xmp(bytes).Should().Contain("<pdfaid:part>3</pdfaid:part>")
                .And.Contain("<pdfaid:conformance>U</pdfaid:conformance>");
        }

        [Test]
        public void SettingsSelectTheProfileForGenerateTaggedPdf()
        {
            var bytes = AccessibleDocument(new DocumentSettings
            {
                PdfAConformance = PdfAConformance.PdfA2U
            }).GenerateTaggedPdf();

            Xmp(bytes).Should().Contain("<pdfaid:part>2</pdfaid:part>")
                .And.Contain("<pdfaid:conformance>U</pdfaid:conformance>");
        }

        [Test]
        public void NoSelectionDoesNotProduceAPdfAClaim()
        {
            var bytes = AccessibleDocument().GenerateTaggedPdf();

            Xmp(bytes).Should().NotContain("pdfaid:part");
            using var pdf = Open(bytes);
            pdf.Internals.Catalog.Elements.ContainsKey("/OutputIntents").Should().BeFalse();
        }

        [Test]
        public void LegacyMetadataFlagMapsToPdfA2B()
        {
            var bytes = Document.Create(Compose)
                .WithMetadata(new DocumentMetadata { Title = Title, Language = Language, PdfA = true })
                .GeneratePdfA();

            Xmp(bytes).Should().Contain("<pdfaid:part>2</pdfaid:part>")
                .And.Contain("<pdfaid:conformance>B</pdfaid:conformance>");
        }

        [Test]
        public void LegacySettingsValueMapsToPdfA2B()
        {
            var bytes = AccessibleDocument(new DocumentSettings
            {
                PdfAConformance = PdfAConformance.Legacy
            }).GeneratePdfA();

            Xmp(bytes).Should().Contain("<pdfaid:part>2</pdfaid:part>")
                .And.Contain("<pdfaid:conformance>B</pdfaid:conformance>");
        }

        [Test]
        public void ExplicitlyDisabledPdfAOverridesTheLegacyFlag()
        {
            var document = Document.Create(Compose)
                .WithMetadata(new DocumentMetadata { Title = Title, Language = Language, PdfA = true })
                .WithSettings(new DocumentSettings { PdfAConformance = PdfAConformance.None });

            Action generate = () => document.GeneratePdfA();
            generate.Should().Throw<InvalidOperationException>();

            var bytes = document.GenerateTaggedPdf();
            Xmp(bytes).Should().NotContain("pdfaid:part");
        }

        [Test]
        public void GeneratePdfAWithoutSelectionFails()
        {
            Action generate = () => AccessibleDocument().GeneratePdfA();
            generate.Should().Throw<InvalidOperationException>()
                .WithMessage("*No PDF/A profile was selected*");
        }

        [TestCase(PdfAConformance.None)]
        [TestCase((PdfAConformance)99)]
        public void InvalidExplicitProfileIsRejected(PdfAConformance profile)
        {
            Action generate = () => AccessibleDocument().GeneratePdfA(profile);
            generate.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Test]
        public void ConflictingSettingsAndExplicitProfileAreRejected()
        {
            var document = AccessibleDocument(new DocumentSettings
            {
                PdfAConformance = PdfAConformance.PdfA2B
            });

            Action generate = () => document.GeneratePdfA(PdfAConformance.PdfA3B);

            generate.Should().Throw<InvalidOperationException>()
                .WithMessage("*PDF/A-2b*PDF/A-3b*");
        }

        [Test]
        public void AccessibleProfileRequiresADocumentLanguage()
        {
            var document = Document.Create(Compose)
                .WithMetadata(new DocumentMetadata { Title = Title })
                .WithSettings(new DocumentSettings { PdfAConformance = PdfAConformance.PdfA2A });

            using var stream = new MemoryStream();
            stream.WriteByte(42);

            Action generate = () => document.GeneratePdfA(stream);

            generate.Should().Throw<PdfOperationException>()
                .WithMessage("*DocumentMetadata.Language*");
            stream.ToArray().Should().Equal(new byte[] { 42 },
                "nothing may be written when the level cannot be satisfied");
        }

        [Test]
        public void BinariesAreUnsetWhenTheDocumentDoesNotDeclareALanguage()
        {
            var bytes = AccessibleDocument(metadata: new DocumentMetadata { Title = Title })
                .GeneratePdfA(PdfAConformance.PdfA2B);

            using var pdf = Open(bytes);
            pdf.Internals.Catalog.Elements.ContainsKey("/Lang").Should().BeFalse(
                "a PDF/A document must not claim a language the caller did not declare");
        }

        [Test]
        public void PdfA3EmbedsAttachmentsAsAssociatedFiles()
        {
            var data = Encoding.UTF8.GetBytes("FossPDF PDF/A-3 fixture\n");

            using var file = new TemporaryAttachment("readme.txt", data);

            var bytes = AccessibleDocument().GeneratePdfA(
                PdfAConformance.PdfA3B,
                new PdfAOptions
                {
                    Attachments = new[]
                    {
                        new DocumentOperation.DocumentAttachment
                        {
                            FilePath = file.FilePath,
                            AttachmentName = "readme.txt",
                            MimeType = "text/plain",
                            Description = "Fixture readme",
                            Relationship = DocumentOperation.DocumentAttachmentRelationship.Data
                        }
                    }
                });

            Artifact("pdfa-3b-attachment", bytes);
            Xmp(bytes).Should().Contain("<pdfaid:part>3</pdfaid:part>");

            using var pdf = Open(bytes);
            var attachments = ReadAttachments(pdf);

            attachments.Should().ContainSingle();
            var attachment = attachments[0];

            attachment.Key.Should().Be("readme.txt");
            attachment.Name.Should().Be("readme.txt");
            attachment.Relationship.Should().Be("Data");
            attachment.MimeType.Should().Be("text/plain");
            attachment.Data.Should().Equal(data);
            attachment.ByteSize.Should().Be(data.Length);
            attachment.ModificationDate.Should().NotBeNull(
                "PDF/A-3 requires a modification date for every embedded file");

            var associated = pdf.Internals.Catalog.Elements.GetArray("/AF");
            associated.Should().NotBeNull();
            associated.Elements.Count.Should().Be(1);
        }

        [Test]
        public void PdfA2RejectsAttachments()
        {
            using var file = new TemporaryAttachment("readme.txt", Encoding.UTF8.GetBytes("fixture"));

            var options = new PdfAOptions
            {
                Attachments = new[]
                {
                    new DocumentOperation.DocumentAttachment
                    {
                        FilePath = file.FilePath,
                        MimeType = "text/plain",
                        Relationship = DocumentOperation.DocumentAttachmentRelationship.Data
                    }
                }
            };

            Action generate = () => AccessibleDocument().GeneratePdfA(PdfAConformance.PdfA2B, options);

            generate.Should().Throw<ArgumentException>()
                .WithMessage("*PDF/A-2b*embedded files*");
        }

        [Test]
        public void AttachmentsWithoutAPdfAProfileAreRejected()
        {
            using var file = new TemporaryAttachment("readme.txt", Encoding.UTF8.GetBytes("fixture"));

            Action generate = () => AccessibleDocument().GenerateTaggedPdf();

            var options = new PdfAOptions
            {
                Attachments = new[]
                {
                    new DocumentOperation.DocumentAttachment
                    {
                        FilePath = file.FilePath,
                        MimeType = "text/plain"
                    }
                }
            };

            Action assemble = () => AccessibleDocument()
                .GenerateTaggedContent()
                .ToPdfA(PdfAConformance.PdfA2B, options);

            generate.Should().NotThrow();
            assemble.Should().Throw<ArgumentException>();
        }

        [Test]
        public void DuplicateAttachmentKeysAreRejected()
        {
            using var first = new TemporaryAttachment("first.txt", Encoding.UTF8.GetBytes("one"));
            using var second = new TemporaryAttachment("second.txt", Encoding.UTF8.GetBytes("two"));

            DocumentOperation.DocumentAttachment Create(TemporaryAttachment file, string name) =>
                new DocumentOperation.DocumentAttachment
                {
                    FilePath = file.FilePath,
                    AttachmentName = name,
                    Key = "shared",
                    MimeType = "text/plain"
                };

            var options = new PdfAOptions
            {
                Attachments = new[] { Create(first, "first.txt"), Create(second, "second.txt") }
            };

            Action generate = () => AccessibleDocument().GeneratePdfA(PdfAConformance.PdfA3B, options);

            generate.Should().Throw<PdfOperationException>().WithMessage("*shared*");
        }

        [Test]
        public void OptimizationCannotBeCombinedWithPdfA()
        {
            var document = AccessibleDocument(new DocumentSettings
            {
                PdfAConformance = PdfAConformance.PdfA2B,
                EnableAdditionalCompression = true,
                QpdfExecutablePath = Path.Combine(Path.GetTempPath(), "missing-qpdf.exe")
            });

            Action generate = () => document.GeneratePdfA();

            generate.Should().Throw<NotSupportedException>().WithMessage("*qpdf*");
        }

        [Test]
        public void StreamAndFileOverloadsWriteTheSameDocument()
        {
            var document = AccessibleDocument(new DocumentSettings
            {
                PdfAConformance = PdfAConformance.PdfA3U
            });

            using var stream = new MemoryStream();
            document.GeneratePdfA(stream, PdfAConformance.PdfA3U);

            stream.CanWrite.Should().BeTrue("the caller owns the stream");
            Xmp(stream.ToArray()).Should().Contain("<pdfaid:part>3</pdfaid:part>");

            var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "pdfa-overload.pdf");

            try
            {
                document.GeneratePdfA(path, PdfAConformance.PdfA3U);
                File.Exists(path).Should().BeTrue();
                Xmp(File.ReadAllBytes(path)).Should().Contain("<pdfaid:part>3</pdfaid:part>");
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Test]
        public void CapturedContentCanBeAssembledAsPdfA()
        {
            var content = AccessibleDocument().GenerateTaggedContent();

            var bytes = content.ToPdfA(PdfAConformance.PdfA2U);

            Xmp(bytes).Should().Contain("<pdfaid:part>2</pdfaid:part>")
                .And.Contain("<pdfaid:conformance>U</pdfaid:conformance>");
        }

        [Test]
        public void ConcurrentProfilesDoNotShareState()
        {
            var results = new[]
            {
                System.Threading.Tasks.Task.Run(() => AccessibleDocument().GeneratePdfA(PdfAConformance.PdfA2A)),
                System.Threading.Tasks.Task.Run(() => AccessibleDocument().GeneratePdfA(PdfAConformance.PdfA3U))
            };

            System.Threading.Tasks.Task.WaitAll(results);

            Xmp(results[0].Result).Should().Contain("<pdfaid:part>2</pdfaid:part>")
                .And.Contain("<pdfaid:conformance>A</pdfaid:conformance>");
            Xmp(results[1].Result).Should().Contain("<pdfaid:part>3</pdfaid:part>")
                .And.Contain("<pdfaid:conformance>U</pdfaid:conformance>");
        }

        private static Document AccessibleDocument(
            DocumentSettings settings = null,
            DocumentMetadata metadata = null)
        {
            var document = Document.Create(Compose)
                .WithMetadata(metadata ?? new DocumentMetadata { Title = Title, Language = Language });

            return settings == null ? document : document.WithSettings(settings);
        }

        private static void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(320, 240);
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(12));

                page.Header().SemanticIgnore().Text(Title);

                page.Content().Column(column =>
                {
                    column.Item().SemanticHeading1().Text("Quarterly totals");
                    column.Item().SemanticParagraph().Text("Revenue is twelve units.");
                    column.Item().SemanticFigure("Four colored quadrants.").Height(40).Background("#ff0000");
                    column.Item().SemanticParagraph().Text("Café naïve résumé");
                    column.Item().SemanticIgnore().Text("Decorative rule");
                });
            });
        }

        private static void AssertOutputIntent(PdfDocument pdf)
        {
            var intents = pdf.Internals.Catalog.Elements.GetArray("/OutputIntents");

            intents.Should().NotBeNull("PDF/A requires an output intent");
            intents.Elements.Count.Should().BeGreaterThan(0);

            var intent = ResolveDictionary(intents.Elements[0]);
            intent.Elements.GetName("/S").Should().Be("/GTS_PDFA1");

            var profile = ResolveDictionary(intent.Elements.GetObject("/DestOutputProfile"));
            profile.Should().NotBeNull("the output intent must carry an ICC profile");
            profile.Stream.Should().NotBeNull();
            profile.Stream.UnfilteredValue.Length.Should().BeGreaterThan(0);
        }

        private static void AssertTaggedStructure(PdfDocument pdf)
        {
            var catalog = pdf.Internals.Catalog;

            catalog.Elements.GetDictionary("/MarkInfo")?.Elements.GetBoolean("/Marked").Should().BeTrue();
            catalog.Elements.GetDictionary("/StructTreeRoot").Should().NotBeNull();
            catalog.Elements.GetDictionary("/ViewerPreferences")?.Elements.GetBoolean("/DisplayDocTitle")
                .Should().BeTrue();
        }

        private static void Artifact(string name, byte[] bytes)
        {
            var directory = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "parity-artifacts",
                "ACC-402");

            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, name + ".pdf"), bytes);
        }

        private static string Name(PdfAConformance profile)
        {
            var value = profile.ToString();
            return "pdfa-" + value.Substring("PdfA".Length).ToLowerInvariant();
        }

        private static (int Part, char Conformance) Expected(PdfAConformance profile)
        {
            var value = profile.ToString();
            var part = value[4] - '0';
            return (part, value[5]);
        }

        private static string Xmp(byte[] pdf)
        {
            using var document = Open(pdf);
            var metadata = ResolveDictionary(document.Internals.Catalog.Elements.GetObject("/Metadata"));

            metadata.Should().NotBeNull("PDF/A requires XMP metadata");
            metadata.Stream.Should().NotBeNull();

            if (metadata.Stream.IsFiltered())
                metadata.Stream.TryUncompress().Should().BeTrue();

            return Encoding.UTF8.GetString(metadata.Stream.UnfilteredValue);
        }

        private static List<AttachmentInfo> ReadAttachments(PdfDocument document)
        {
            var attachments = new List<AttachmentInfo>();
            var names = document.Internals.Catalog.Elements.GetDictionary("/Names");
            var embedded = names?.Elements.GetDictionary("/EmbeddedFiles");
            var entries = embedded?.Elements.GetArray("/Names");

            if (entries == null)
                return attachments;

            for (var index = 0; index + 1 < entries.Elements.Count; index += 2)
            {
                var key = ((PdfString)entries.Elements[index]).Value;
                var specification = ResolveDictionary(entries.Elements[index + 1]);
                var files = specification.Elements.GetDictionary("/EF");
                var streamDictionary = ResolveDictionary(files.Elements.GetObject("/F"));
                var stream = streamDictionary.Stream;
                var parameters = streamDictionary.Elements.GetDictionary("/Params");

                if (stream.IsFiltered())
                    stream.TryUncompress().Should().BeTrue();

                attachments.Add(new AttachmentInfo
                {
                    Key = key,
                    Name = specification.Elements.GetString("/UF"),
                    Relationship = TrimName(specification.Elements.GetName("/AFRelationship")),
                    MimeType = TrimName(streamDictionary.Elements.GetName("/Subtype")),
                    Data = stream.UnfilteredValue,
                    ByteSize = parameters.Elements.GetInteger("/Size"),
                    ModificationDate = parameters.Elements.ContainsKey("/ModDate")
                        ? parameters.Elements.GetDateTime("/ModDate", DateTime.MinValue)
                        : (DateTime?)null
                });
            }

            return attachments;
        }

        private static PdfDocument Open(byte[] pdf)
        {
            return PdfReader.Open(new MemoryStream(pdf, writable: false), PdfDocumentOpenMode.Import);
        }

        private static PdfDictionary ResolveDictionary(PdfItem item)
        {
            return item switch
            {
                PdfReference reference => reference.Value as PdfDictionary,
                PdfDictionary dictionary => dictionary,
                _ => null
            };
        }

        private static string TrimName(string value)
        {
            return value.StartsWith("/", StringComparison.Ordinal) ? value.Substring(1) : value;
        }

        private sealed class AttachmentInfo
        {
            public string Key { get; set; }
            public string Name { get; set; }
            public string Relationship { get; set; }
            public string MimeType { get; set; }
            public byte[] Data { get; set; }
            public int ByteSize { get; set; }
            public DateTime? ModificationDate { get; set; }
        }

        private sealed class TemporaryAttachment : IDisposable
        {
            private readonly string _directory;

            public TemporaryAttachment(string name, byte[] content)
            {
                _directory = Path.Combine(
                    Path.GetTempPath(),
                    "foss-pdf-pdfa",
                    Guid.NewGuid().ToString("N"));

                Directory.CreateDirectory(_directory);
                FilePath = Path.Combine(_directory, name);
                File.WriteAllBytes(FilePath, content);
            }

            public string FilePath { get; }

            public void Dispose()
            {
                if (Directory.Exists(_directory))
                    Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
#endif
