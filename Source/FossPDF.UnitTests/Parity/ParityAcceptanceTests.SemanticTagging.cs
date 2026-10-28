#if NET9_0
#nullable enable
using System;
using System.Linq;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;

namespace FossPDF.UnitTests.Parity
{
    /// <summary>
    /// FP-401 acceptance: render the ACC-401 document and verify, from the produced bytes, that the
    /// structure tree, marked content, alternative text, language, artifacts and parent tree are
    /// consistent. The operations companion targets net9.0 only, so the fixture declares
    /// frameworks: ["net9.0"].
    /// </summary>
    public partial class ParityAcceptanceTests
    {
        [ParityFixtureRunner("ACC-401")]
        private static void RunSemanticTagging(ParityFixture fixture)
        {
            var language = fixture.Input.GetProperty("language").GetString()!;
            var title = fixture.Input.GetProperty("title").GetString()!;
            var content = fixture.Input.GetProperty("content").EnumerateArray().ToArray();

            string Text(string role) => content
                .Single(x => x.GetProperty("role").GetString() == role)
                .GetProperty("text").GetString()!;

            var heading = Text("H1");
            var paragraph = Text("P");
            var figure = content.Single(x => x.GetProperty("role").GetString() == "Figure");
            var figureAlt = figure.GetProperty("alt").GetString()!;
            var artifact = Text("Artifact");

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(320, 240);
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(12));

                    // Repeated header: a reusable component that must stay an artifact on every page.
                    page.Header().SemanticIgnore().Text(title);

                    page.Content().Column(column =>
                    {
                        column.Item().SemanticHeading1().Text(heading);
                        column.Item().SemanticParagraph().Text(paragraph);
                        column.Item().SemanticFigure(figureAlt).Height(40).Background("#ff0000");
                        column.Item().SemanticIgnore().Text(artifact);

                        // Forces the semantic paragraph across at least two pages.
                        column.Item().SemanticParagraph().Text(
                            string.Join("\n", Enumerable.Range(0, 60)
                                .Select(index => "Line " + index + " of the accessible fixture.")));
                    });
                });
            }).WithMetadata(new DocumentMetadata
            {
                Title = title,
                Language = language
            });

            var output = document.GenerateTaggedPdf();

            Assert.That(System.Text.Encoding.ASCII.GetString(output, 0, 5), Is.EqualTo("%PDF-"),
                "ACC-401: the result is a PDF document.");
            SaveArtifact(fixture.Id, "tagged.pdf", output);

            using var pdf = TaggedPdfInspector.Open(output);

            Assert.That(pdf.Info.Title, Is.EqualTo(title),
                "ACC-401: the document title is present.");
            Assert.That(pdf.Catalog.Elements.GetString("/Lang"), Is.EqualTo(language),
                "ACC-401: the document declares the requested language.");
            Assert.That(pdf.Catalog.Elements.GetDictionary("/MarkInfo")?.Elements.GetBoolean("/Marked"),
                Is.True, "ACC-401: the catalog declares a tagged document.");
            Assert.That(pdf.Catalog.Elements.GetDictionary("/ViewerPreferences")?.Elements.GetBoolean("/DisplayDocTitle"),
                Is.True, "ACC-401: the title is displayed by the viewer.");
            Assert.That(pdf.StructTreeRoot.Elements.GetInteger("/ParentTreeNextKey"), Is.GreaterThan(0),
                "ACC-401: the parent tree declares the next free key.");

            Assert.That(pdf.StructureRoles(),
                Is.EqualTo(new[] { "Document", "H1", "P", "Figure", "P" }),
                "ACC-401: the structure tree holds the headings and paragraphs in reading order.");

            var structureFigure = pdf.Nodes().Single(x => x.Role == "Figure");
            Assert.That(structureFigure.AlternativeText, Is.EqualTo(figureAlt),
                "ACC-401: the figure alternative text is exact.");
            Assert.That(structureFigure.MarkedContent, Has.Count.EqualTo(1),
                "ACC-401: the figure owns one marked-content sequence.");

            var artifacts = pdf.Pages.SelectMany(x => x.Forms).Where(x => x.Role == null).ToArray();
            Assert.That(artifacts, Is.Not.Empty,
                "ACC-401: decorative content is captured as an artifact.");
            Assert.That(artifacts.All(x => x.Content.StartsWith("/Artifact BMC", StringComparison.Ordinal)),
                Is.True, "ACC-401: artifacts are marked with /Artifact BMC.");
            Assert.That(artifacts.All(x => !x.StructParents.HasValue), Is.True,
                "ACC-401: artifacts are not linked to the parent tree.");
            Assert.That(pdf.StructureRoles(), Does.Not.Contain("Artifact"),
                "ACC-401: decorative content is absent from the structure tree.");

            Assert.That(pdf.Pages.Count, Is.GreaterThan(1),
                "ACC-401: the fixture spans more than one page.");
            Assert.That(pdf.Pages.All(page => page.Forms.Any(x => x.Role == null)), Is.True,
                "ACC-401: the repeated header stays an artifact on every page.");

            var spanningParagraph = pdf.Nodes().Last();
            Assert.That(spanningParagraph.Role, Is.EqualTo("P"));
            Assert.That(spanningParagraph.MarkedContent, Has.Count.EqualTo(pdf.Pages.Count),
                "ACC-401: a semantic element split across pages stays one structure element.");
            Assert.That(spanningParagraph.MarkedContent
                    .Select(x => pdf.ResolvePageIndex(x.Elements.GetObject("/Pg")!))
                    .OrderBy(x => x),
                Is.EqualTo(Enumerable.Range(0, pdf.Pages.Count)),
                "ACC-401: each page contributes its own marked-content reference.");

            pdf.AssertMarkedContentIsMapped(pdf.StructureRoot);
        }
    }
}
#endif
