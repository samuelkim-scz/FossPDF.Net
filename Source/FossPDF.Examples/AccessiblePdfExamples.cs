using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace FossPDF.Examples
{
    /// <summary>
    /// Shows how semantic wrappers turn a visual document into a tagged PDF with a logical
    /// structure tree, alternative text, language changes and artifacts. All examples write their
    /// artifacts under the test directory.
    /// </summary>
    public class AccessiblePdfExamples
    {
        [Test]
        public void CreateATaggedDocumentWithHeadingsAndAlternativeText()
        {
            var directory = CreateOutputDirectory("accessible-pdf");
            var path = Path.Combine(directory, "tagged-report.pdf");

            // Semantic wrappers are layout-transparent: the rendered pages stay identical to the
            // plain Text/Image calls, while the writer records the logical structure.
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(320, 240);
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(12));

                    // Content without a semantic wrapper (such as this repeated header) is tagged
                    // as an artifact so assistive technology skips it on every page.
                    page.Header().SemanticIgnore().Text("Quarterly report");

                    page.Content().Column(column =>
                    {
                        column.Item().SemanticHeading1().Text("Quarterly totals");
                        column.Item().SemanticParagraph().Text("Revenue is twelve units.");

                        column.Item()
                            .SemanticFigure("Four colored quadrants.")
                            .Height(60)
                            .Background("#ff0000");

                        column.Item().SemanticParagraph().Text("The figure summarizes the quarter.");
                    });
                });
            }).WithMetadata(new DocumentMetadata
            {
                Title = "Accessible quarterly report",
                Author = "FossPDF",
                Language = "en-US"
            });

            document.GenerateTaggedPdf(path);

            Assert.That(File.Exists(path), Is.True);
            Assert.That(ReadStructure(path), Is.EqualTo(new[] { "Document", "H1", "P", "Figure", "P" }));

            using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);

            Assert.That(pdf.Info.Title, Is.EqualTo("Accessible quarterly report"));
            Assert.That(pdf.Internals.Catalog.Elements.GetString("/Lang"), Is.EqualTo("en-US"));
            Assert.That(pdf.Internals.Catalog.Elements.GetDictionary("/MarkInfo").Elements.GetBoolean("/Marked"),
                Is.True);
        }

        [Test]
        public void MarkDecorativeContentAndChangeLanguageInline()
        {
            var directory = CreateOutputDirectory("accessible-pdf");
            var path = Path.Combine(directory, "inline-language.pdf");

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(320, 200);
                    page.Margin(20);
                    page.Content().Column(column =>
                    {
                        column.Item().SemanticParagraph().Text("This paragraph is written in English.");

                        // A language wrapper scopes the inner content; screen readers switch voice.
                        column.Item().SemanticLanguage("de-DE").SemanticParagraph().Text("Dieser Absatz ist auf Deutsch.");
                        column.Item().SemanticLanguage("es-ES").SemanticParagraph().Text("Este párrafo está en español.");

                        // Decorative separators are marked as artifacts and never enter the tree.
                        column.Item().SemanticIgnore().Height(1).Background("#808080");
                    });
                });
            }).WithMetadata(new DocumentMetadata { Language = "en-US" });

            document.GenerateTaggedPdf(path);

            Assert.That(ReadStructure(path), Is.EqualTo(new[] { "Document", "P", "Span", "P", "Span", "P" }));

            using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            var root = pdf.Internals.Catalog.Elements.GetDictionary("/StructTreeRoot").Elements.GetDictionary("/K");
            var spans = ReadChildren(root).Where(x => x.Elements.GetName("/S") == "/Span").ToArray();

            Assert.That(spans.Select(x => x.Elements.GetString("/Lang")),
                Is.EqualTo(new[] { "de-DE", "es-ES" }));
        }

        [Test]
        public void TagLinksAndSectionNavigations()
        {
            var directory = CreateOutputDirectory("accessible-pdf");
            var path = Path.Combine(directory, "tagged-links.pdf");

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(320, 220);
                    page.Margin(20);
                    page.Content().Column(column =>
                    {
                        column.Item().SemanticLink("FossPDF website")
                            .Hyperlink("https://github.com/fosspdf/fosspdf")
                            .Text("FossPDF website");

                        column.Item().SemanticLink("Jump to the totals")
                            .SectionLink("totals")
                            .Text("Jump to the totals");
                    });
                });

                container.Page(page =>
                {
                    page.Size(320, 220);
                    page.Margin(20);
                    page.Content().Section("totals").SemanticHeading2().Text("Totals");
                });
            });

            document.GenerateTaggedPdf(path);

            Assert.That(ReadStructure(path), Is.EqualTo(new[] { "Document", "Link", "Link", "H2" }));

            using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            var annotations = new List<PdfDictionary>();

            foreach (var page in pdf.Pages.Cast<PdfPage>())
            {
                var annots = page.Elements.GetArray("/Annots");

                if (annots == null)
                    continue;

                foreach (var item in annots.Elements)
                {
                    if (item is PdfReference reference && reference.Value is PdfDictionary annotation)
                        annotations.Add(annotation);
                }
            }

            Assert.That(annotations, Has.Count.EqualTo(2));
            Assert.That(annotations.All(x => x.Elements.ContainsKey("/StructParent")), Is.True,
                "Both link annotations are associated with their Link structure element.");

            var external = annotations.Single(x => x.Elements.ContainsKey("/A"));
            Assert.That(external.Elements.GetDictionary("/A").Elements.GetString("/URI"),
                Is.EqualTo("https://github.com/fosspdf/fosspdf"));
        }

        private static string[] ReadStructure(string path)
        {
            using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            var root = pdf.Internals.Catalog.Elements.GetDictionary("/StructTreeRoot").Elements.GetDictionary("/K");
            var roles = new List<string>();

            Read(root, roles);

            return roles.ToArray();
        }

        private static void Read(PdfDictionary element, List<string> roles)
        {
            roles.Add(TrimName(element.Elements.GetName("/S")));

            foreach (var child in ReadChildren(element))
                Read(child, roles);
        }

        private static IEnumerable<PdfDictionary> ReadChildren(PdfDictionary element)
        {
            var value = element.Elements.GetObject("/K");
            var items = value is PdfArray array ? array.Elements.ToArray() : new[] { value as PdfItem };

            foreach (var item in items)
            {
                var dictionary = item is PdfReference reference
                    ? reference.Value as PdfDictionary
                    : item as PdfDictionary;

                if (dictionary == null || TrimName(dictionary.Elements.GetName("/Type")) == "MCR")
                    continue;

                if (TrimName(dictionary.Elements.GetName("/Type")) == "OBJR")
                    continue;

                if (dictionary.Elements.ContainsKey("/S"))
                    yield return dictionary;
            }
        }

        private static string TrimName(string value)
        {
            return value != null && value.StartsWith("/", StringComparison.Ordinal) ? value.Substring(1) : value;
        }

        private static string CreateOutputDirectory(string name)
        {
            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, name);
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
