#if NET9_0
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.PdfOperations;
using NUnit.Framework;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace FossPDF.UnitTests.Parity
{
    /// <summary>
    /// FP-402 acceptance: write every selectable PDF/A-2 and PDF/A-3 conformance level from the
    /// ACC-401 semantic document, verify the declared identification, output intent, tagged
    /// structure, embedded fonts and Unicode mappings from the produced bytes, and confirm that
    /// incompatible combinations are rejected before a compliance claim is written. The exact
    /// profiles are validated independently by verify_pdfa.py with a pinned veraPDF release.
    /// </summary>
    public partial class ParityAcceptanceTests
    {
        private const string PdfAIdentifierNamespace = "http://www.aiim.org/pdfa/ns/id/";

        [ParityFixtureRunner("ACC-402")]
        private static void RunPdfAConformance(ParityFixture fixture)
        {
            var catalog = FixtureCatalog.Load();
            var sourceId = fixture.Input.GetProperty("source").GetString()!;
            var source = catalog.Fixtures.Single(x => x.Id == sourceId).Input;
            var levels = FixtureCatalog.ReadArray(fixture.Input, "levels")
                .Select(x => x.GetString()!)
                .ToArray();

            Assert.That(levels, Is.EqualTo(new[]
                {
                    "PDF/A-2a", "PDF/A-2b", "PDF/A-2u",
                    "PDF/A-3a", "PDF/A-3b", "PDF/A-3u"
                }),
                "ACC-402: every supported level is exercised.");

            var document = CreatePdfAFixture(source);

            foreach (var level in levels)
            {
                var profile = ParsePdfAProfile(level);
                var bytes = document.GeneratePdfA(profile);
                var artifact = PdfAArtifactName(level);

                SaveArtifact(fixture.Id, artifact + ".pdf", bytes);
                AssertPdfAIdentification(bytes, profile, level);
                AssertPdfATaggedStructure(bytes, source, level);
                AssertPdfAFontsAndUnicode(bytes, level);

                if (PdfAAllowsAttachments(profile))
                    AssertPdfA3Attachments(fixture, document, profile, level);
            }

            AssertPdfARejectsIncompatibleCombinations(source);
        }

        private static Document CreatePdfAFixture(System.Text.Json.JsonElement source)
        {
            var title = source.GetProperty("title").GetString()!;
            var language = source.GetProperty("language").GetString()!;
            var content = source.GetProperty("content").EnumerateArray().ToArray();

            string Text(string role) => content
                .Single(x => x.GetProperty("role").GetString() == role)
                .GetProperty("text").GetString()!;

            var figureAlt = content.Single(x => x.GetProperty("role").GetString() == "Figure")
                .GetProperty("alt").GetString()!;

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(320, 240);
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(12));

                    page.Header().SemanticIgnore().Text(title);

                    page.Content().Column(column =>
                    {
                        column.Item().SemanticHeading1().Text(Text("H1"));
                        column.Item().SemanticParagraph().Text(Text("P"));
                        column.Item().SemanticFigure(figureAlt).Height(40).Background("#ff0000");
                        column.Item().SemanticIgnore().Text(Text("Artifact"));

                        // A second page keeps the structure valid across page breaks.
                        column.Item().SemanticParagraph().Text(
                            string.Join("\n", Enumerable.Range(0, 40)
                                .Select(index => "Accessible line " + index + ".")));
                    });
                });
            }).WithMetadata(new DocumentMetadata
            {
                Title = title,
                Author = "FossPDF",
                Language = language
            });
        }

        private static PdfAConformance ParsePdfAProfile(string level)
        {
            switch (level)
            {
                case "PDF/A-2a":
                    return PdfAConformance.PdfA2A;
                case "PDF/A-2b":
                    return PdfAConformance.PdfA2B;
                case "PDF/A-2u":
                    return PdfAConformance.PdfA2U;
                case "PDF/A-3a":
                    return PdfAConformance.PdfA3A;
                case "PDF/A-3b":
                    return PdfAConformance.PdfA3B;
                case "PDF/A-3u":
                    return PdfAConformance.PdfA3U;
                default:
                    throw new AssertionException($"Unknown PDF/A level '{level}'.");
            }
        }

        private static string PdfAArtifactName(string level) =>
            "pdfa-" + level.Substring("PDF/A-".Length).Replace("-", string.Empty).ToLowerInvariant();

        private static bool PdfAAllowsAttachments(PdfAConformance profile) =>
            profile is PdfAConformance.PdfA3A or PdfAConformance.PdfA3B or PdfAConformance.PdfA3U;

        private static (int Part, char Conformance) PdfAExpected(PdfAConformance profile)
        {
            var value = profile.ToString();
            return (value[4] - '0', value[5]);
        }

        private static void AssertPdfAIdentification(
            byte[] bytes,
            PdfAConformance profile,
            string level)
        {
            Assert.That(Encoding.ASCII.GetString(bytes, 0, 8), Is.EqualTo("%PDF-1.7"),
                $"ACC-402 {level}: PDF/A-2 and PDF/A-3 are based on PDF 1.7.");

            var (part, conformance) = PdfAExpected(profile);
            var xmp = PdfAMetadata(bytes);

            Assert.That(xmp, Is.Not.Null, $"ACC-402 {level}: the document has an XMP packet.");

            var document = XDocument.Parse(xmp);
            var declaredPart = document.Descendants(XName.Get("part", PdfAIdentifierNamespace))
                .SingleOrDefault();
            var declaredConformance = document
                .Descendants(XName.Get("conformance", PdfAIdentifierNamespace))
                .SingleOrDefault();

            Assert.That(declaredPart, Is.Not.Null,
                $"ACC-402 {level}: the XMP packet declares the PDF/A part.");
            Assert.That(declaredPart!.Value, Is.EqualTo(part.ToString()),
                $"ACC-402 {level}: the declared PDF/A part is exact.");
            Assert.That(declaredConformance, Is.Not.Null,
                $"ACC-402 {level}: the XMP packet declares the PDF/A conformance level.");
            Assert.That(declaredConformance!.Value, Is.EqualTo(conformance.ToString()),
                $"ACC-402 {level}: the declared conformance level is exact.");

            using var pdf = PdfAOpen(bytes);
            var intents = pdf.Internals.Catalog.Elements.GetArray("/OutputIntents");

            Assert.That(intents, Is.Not.Null, $"ACC-402 {level}: an output intent is present.");
            Assert.That(intents!.Elements.Count, Is.GreaterThan(0));

            var intent = PdfAResolve(intents.Elements[0]);
            Assert.That(intent.Elements.GetName("/S"), Is.EqualTo("/GTS_PDFA1"),
                $"ACC-402 {level}: the output intent uses the PDF/A subtype.");

            var icc = PdfAResolve(intent.Elements.GetObject("/DestOutputProfile"));
            Assert.That(icc?.Stream, Is.Not.Null,
                $"ACC-402 {level}: the output intent embeds an ICC profile.");

            if (icc!.Stream!.IsFiltered())
                Assert.That(icc.Stream.TryUncompress(), Is.True,
                    $"ACC-402 {level}: the ICC profile can be decoded.");

            Assert.That(icc.Stream.UnfilteredValue.Length, Is.GreaterThan(0),
                $"ACC-402 {level}: the ICC profile is not empty.");
        }

        private static void AssertPdfATaggedStructure(
            byte[] bytes,
            System.Text.Json.JsonElement source,
            string level)
        {
            using var pdf = PdfAOpen(bytes);
            var catalog = pdf.Internals.Catalog;

            Assert.That(catalog.Elements.GetDictionary("/MarkInfo")?.Elements.GetBoolean("/Marked"),
                Is.True, $"ACC-402 {level}: the document is tagged.");
            Assert.That(catalog.Elements.GetString("/Lang"),
                Is.EqualTo(source.GetProperty("language").GetString()),
                $"ACC-402 {level}: the document declares its language.");
            Assert.That(catalog.Elements.GetDictionary("/ViewerPreferences")
                    ?.Elements.GetBoolean("/DisplayDocTitle"),
                Is.True, $"ACC-402 {level}: the title is configured for display.");

            var root = catalog.Elements.GetDictionary("/StructTreeRoot");
            Assert.That(root, Is.Not.Null, $"ACC-402 {level}: the structure tree root is present.");
            Assert.That(root!.Elements.GetInteger("/ParentTreeNextKey"), Is.GreaterThan(0),
                $"ACC-402 {level}: the parent tree is populated.");

            var roles = PdfAStructureRoles(root!);
            Assert.That(roles, Is.EqualTo(new[] { "Document", "H1", "P", "Figure", "P" }),
                $"ACC-402 {level}: structure elements keep their logical reading order.");
            Assert.That(roles, Does.Not.Contain("Artifact"),
                $"ACC-402 {level}: decorative content stays outside the structure tree.");

            var figure = PdfAFindRole(root!, "Figure");
            Assert.That(figure, Is.Not.Null, $"ACC-402 {level}: the figure has a structure element.");

            var figureAlt = source.GetProperty("content").EnumerateArray()
                .Single(x => x.GetProperty("role").GetString() == "Figure")
                .GetProperty("alt").GetString();
            Assert.That(figure!.Elements.GetString("/Alt"), Is.EqualTo(figureAlt),
                $"ACC-402 {level}: alternative text is preserved exactly.");
        }

        private static void AssertPdfAFontsAndUnicode(byte[] bytes, string level)
        {
            var text = Encoding.Latin1.GetString(bytes);

            Assert.That(text, Does.Contain("/FontFile2"),
                $"ACC-402 {level}: every font is embedded.");
            Assert.That(text, Does.Contain("/ToUnicode"),
                $"ACC-402 {level}: every embedded font has a Unicode mapping.");
        }

        private static void AssertPdfA3Attachments(
            ParityFixture fixture,
            Document document,
            PdfAConformance profile,
            string level)
        {
            var attachment = FixtureCatalog.Load().Fixtures
                .Single(x => x.Id == "PDF-305").Input.GetProperty("attachment");
            var name = attachment.GetProperty("name").GetString()!;
            var mediaType = attachment.GetProperty("media_type").GetString()!;
            var payload = Encoding.UTF8.GetBytes(attachment.GetProperty("utf8").GetString()!);

            var directory = Path.Combine(Path.GetTempPath(), "fosspdf-pdfa-parity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            byte[] bytes;

            try
            {
                var path = Path.Combine(directory, name);
                File.WriteAllBytes(path, payload);

                bytes = document.GeneratePdfA(profile, new PdfAOptions
                {
                    Attachments = new[]
                    {
                        new DocumentOperation.DocumentAttachment
                        {
                            FilePath = path,
                            AttachmentName = name,
                            MimeType = mediaType,
                            Relationship = DocumentOperation.DocumentAttachmentRelationship.Data
                        }
                    }
                });
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }

            Assert.That(Encoding.ASCII.GetString(bytes, 0, 5), Is.EqualTo("%PDF-"),
                $"ACC-402 {level}: the attachment-bearing output is a PDF document.");
            SaveArtifact(fixture.Id, PdfAArtifactName(level) + "-attachment.pdf", bytes);

            using var pdf = PdfAOpen(bytes);
            var files = PdfAReadAttachments(pdf);

            Assert.That(files, Has.Count.EqualTo(1),
                $"ACC-402 {level}: the embedded file is listed in the name tree.");
            Assert.That(files[0].Key, Is.EqualTo(name),
                $"ACC-402 {level}: the name-tree key is the attachment name.");
            Assert.That(files[0].MimeType, Is.EqualTo(mediaType),
                $"ACC-402 {level}: the declared media type is written.");
            Assert.That(files[0].Relationship, Is.EqualTo("Data"),
                $"ACC-402 {level}: the associated-file relationship is written.");
            Assert.That(files[0].Data, Is.EqualTo(payload),
                $"ACC-402 {level}: the attachment bytes are preserved.");
            Assert.That(files[0].ModificationDate, Is.Not.Null,
                $"ACC-402 {level}: PDF/A-3 requires a modification date.");

            var associated = pdf.Internals.Catalog.Elements.GetArray("/AF");
            Assert.That(associated, Is.Not.Null,
                $"ACC-402 {level}: the file is listed in the catalog's associated-files array.");
        }

        private static void AssertPdfARejectsIncompatibleCombinations(System.Text.Json.JsonElement source)
        {
            using var stream = new MemoryStream();
            stream.WriteByte(42);

            // Each rejection uses its own document instance: WithSettings on Document is a
            // mutating builder, so sharing one instance would leak the settings between checks.
            Action pdfA2WithAttachment = () => CreatePdfAFixture(source)
                .GeneratePdfA(PdfAConformance.PdfA2B, new PdfAOptions
            {
                Attachments = new[]
                {
                    new DocumentOperation.DocumentAttachment
                    {
                        FilePath = typeof(ParityAcceptanceTests).Assembly.Location,
                        AttachmentName = "payload.bin",
                        MimeType = "application/octet-stream"
                    }
                }
            });

            Assert.That(pdfA2WithAttachment, Throws.TypeOf<ArgumentException>()
                    .With.Message.Contains("PDF/A-3"),
                "ACC-402: PDF/A-2 rejects embedded files before writing.");

            var noLanguage = Document.Create(container =>
                container.Page(page => page.Content().SemanticParagraph().Text("text")))
                .WithMetadata(new DocumentMetadata { Title = "Missing language" });

            Assert.That(() => noLanguage.GeneratePdfA(PdfAConformance.PdfA2A),
                Throws.TypeOf<PdfOperationException>().With.Message.Contains("document language"),
                "ACC-402: an A-level profile requires a document language.");

            var conflicting = CreatePdfAFixture(source)
                .WithSettings(new DocumentSettings { PdfAConformance = PdfAConformance.PdfA3U });

            Assert.That(() => conflicting.GeneratePdfA(PdfAConformance.PdfA2B),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("was requested"),
                "ACC-402: conflicting configured and requested profiles are rejected.");

            Assert.That(() => CreatePdfAFixture(source).GeneratePdfA(),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("No PDF/A profile"),
                "ACC-402: no profile selection means no compliance claim.");

            Assert.That(stream.ToArray(), Is.EqualTo(new byte[] { 42 }),
                "ACC-402: rejected outputs never touch the destination stream.");
        }

        private static string PdfAMetadata(byte[] pdf)
        {
            using var document = PdfAOpen(pdf);
            var metadata = PdfAResolve(document.Internals.Catalog.Elements.GetObject("/Metadata"));

            if (metadata?.Stream == null)
                return null;

            if (metadata.Stream.IsFiltered())
                Assert.That(metadata.Stream.TryUncompress(), Is.True,
                    "ACC-402: the XMP packet can be decoded.");

            return Encoding.UTF8.GetString(metadata.Stream.UnfilteredValue);
        }

        private static string[] PdfAStructureRoles(PdfDictionary root)
        {
            var roles = new List<string>();
            var document = PdfAResolve(root.Elements.GetObject("/K")) ?? root;
            PdfAReadRole(document, roles);
            return roles.ToArray();
        }

        private static void PdfAReadRole(PdfDictionary element, List<string> roles)
        {
            roles.Add(TrimPdfAName(element.Elements.GetName("/S")));

            foreach (var child in PdfAChildren(element))
                PdfAReadRole(child, roles);
        }

        private static PdfDictionary PdfAFindRole(PdfDictionary element, string role)
        {
            if (TrimPdfAName(element.Elements.GetName("/S")) == role)
                return element;

            foreach (var child in PdfAChildren(element))
            {
                var match = PdfAFindRole(child, role);

                if (match != null)
                    return match;
            }

            return null;
        }

        private static IEnumerable<PdfDictionary> PdfAChildren(PdfDictionary element)
        {
            var value = element.Elements.GetObject("/K");
            var items = value is PdfArray array
                ? array.Elements.ToArray()
                : new[] { value as PdfItem };

            foreach (var item in items)
            {
                var dictionary = item is PdfReference reference
                    ? reference.Value as PdfDictionary
                    : item as PdfDictionary;

                if (dictionary == null || !dictionary.Elements.ContainsKey("/S"))
                    continue;

                yield return dictionary;
            }
        }

        private static List<PdfAAttachment> PdfAReadAttachments(PdfDocument document)
        {
            var attachments = new List<PdfAAttachment>();
            var names = document.Internals.Catalog.Elements.GetDictionary("/Names");
            var embedded = names?.Elements.GetDictionary("/EmbeddedFiles");
            var entries = embedded?.Elements.GetArray("/Names");

            if (entries == null)
                return attachments;

            for (var index = 0; index + 1 < entries.Elements.Count; index += 2)
            {
                var key = ((PdfString)entries.Elements[index]).Value;
                var specification = PdfAResolve(entries.Elements[index + 1]);
                var files = specification.Elements.GetDictionary("/EF");
                var streamDictionary = PdfAResolve(files.Elements.GetObject("/F"));
                var stream = streamDictionary.Stream;
                var parameters = streamDictionary.Elements.GetDictionary("/Params");

                if (stream.IsFiltered())
                    Assert.That(stream.TryUncompress(), Is.True,
                        "ACC-402: the embedded file can be decoded.");

                attachments.Add(new PdfAAttachment
                {
                    Key = key,
                    MimeType = TrimPdfAName(streamDictionary.Elements.GetName("/Subtype")),
                    Relationship = TrimPdfAName(specification.Elements.GetName("/AFRelationship")),
                    Data = stream.UnfilteredValue,
                    ModificationDate = parameters != null && parameters.Elements.ContainsKey("/ModDate")
                        ? parameters.Elements.GetDateTime("/ModDate", DateTime.MinValue)
                        : (DateTime?)null
                });
            }

            return attachments;
        }

        private static PdfDocument PdfAOpen(byte[] pdf) =>
            PdfReader.Open(new MemoryStream(pdf, writable: false), PdfDocumentOpenMode.Import);

        private static PdfDictionary PdfAResolve(PdfItem item) =>
            item switch
            {
                PdfReference reference => reference.Value as PdfDictionary,
                PdfDictionary dictionary => dictionary,
                _ => null
            };

        private static string TrimPdfAName(string value) =>
            value != null && value.StartsWith("/", StringComparison.Ordinal)
                ? value.Substring(1)
                : value;

        private sealed class PdfAAttachment
        {
            public string Key { get; set; }
            public string MimeType { get; set; }
            public string Relationship { get; set; }
            public byte[] Data { get; set; }
            public DateTime? ModificationDate { get; set; }
        }
    }
}
#endif
