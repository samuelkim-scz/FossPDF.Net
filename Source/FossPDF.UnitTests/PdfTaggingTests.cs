#if NET9_0
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;

namespace FossPDF.UnitTests
{
    /// <summary>
    /// FP-401: the tagged-PDF backend assembles the captured semantic tree and paint batches into a
    /// real structure tree with marked content, parent-tree links, alternative text, language and
    /// artifact marks. The operations companion targets net9.0, so the fixture is compiled only for
    /// that framework.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("SemanticTagging")]
    public class PdfTaggingTests
    {
        [Test]
        public void StructureTreeContainsRolesInReadingOrder()
        {
            using var pdf = TaggedPdfInspector.Open(CreateAccessibleDocument().GenerateTaggedPdf());

            Assert.That(pdf.Catalog.Elements.GetString("/Lang"), Is.EqualTo("en-US"));
            Assert.That(pdf.Catalog.Elements.GetDictionary("/MarkInfo")?.Elements.GetBoolean("/Marked"), Is.True);
            Assert.That(pdf.Catalog.Elements.GetDictionary("/ViewerPreferences")?.Elements.GetBoolean("/DisplayDocTitle"),
                Is.True);
            Assert.That(pdf.Info.Title, Is.EqualTo("Accessible fixture"));

            var root = pdf.StructureRoot;

            Assert.That(root.Role, Is.EqualTo("Document"));
            Assert.That(root.Children.Select(x => x.Role), Is.EqualTo(new[] { "H1", "P", "Figure" }));

            var figure = root.Children[2];
            Assert.That(figure.AlternativeText, Is.EqualTo("Four colored quadrants."));
            Assert.That(root.Children.Select(x => x.Parent!), Is.All.SameAs(root));

            Assert.That(root.Children.All(x => x.MarkedContent.Count == 1), Is.True,
                "Every semantic element owns exactly one marked-content sequence.");
            Assert.That(root.Children.All(x => x.ObjectReferences.Count == 0), Is.True);

            pdf.AssertMarkedContentIsMapped(root);
        }

        [Test]
        public void ArtifactContentIsMarkedAndAbsentFromTheStructureTree()
        {
            using var pdf = TaggedPdfInspector.Open(CreateAccessibleDocument().GenerateTaggedPdf());

            var artifacts = pdf.Pages.SelectMany(x => x.Forms).Where(x => x.Role == null).ToArray();

            Assert.That(artifacts, Is.Not.Empty, "Unwrapped content must be captured as artifacts.");
            Assert.That(artifacts.All(x => x.Content.StartsWith("/Artifact BMC", StringComparison.Ordinal)), Is.True);
            Assert.That(artifacts.All(x => !x.StructParents.HasValue), Is.True);
            Assert.That(pdf.StructureRoles(), Does.Not.Contain("Artifact"));
        }

        [Test]
        public void NestedListsAndTablesProduceNestedStructure()
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(420, 320);
                    page.Margin(20);
                    page.Content().Column(column =>
                    {
                        column.Item().SemanticList().Column(list =>
                        {
                            list.Item().SemanticListItem().Row(row =>
                            {
                                row.ConstantItem(24).SemanticListLabel().Text("1.");
                                row.RelativeItem().SemanticListItemBody().Text("First item");
                            });

                            list.Item().SemanticListItem().Row(row =>
                            {
                                row.ConstantItem(24).SemanticListLabel().Text("2.");
                                row.RelativeItem().SemanticListItemBody().SemanticParagraph().Text("Second item");
                            });
                        });

                        column.Item().SemanticTable().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            table.Header(header =>
                            {
                                header.Cell().SemanticHorizontalHeader().Text("Name");
                                header.Cell().SemanticHorizontalHeader().Text("Value");
                            });

                            table.Cell().SemanticParagraph().Text("Revenue");
                            table.Cell().SemanticParagraph().Text("12");
                        });
                    });
                });
            });

            using var pdf = TaggedPdfInspector.Open(document.GenerateTaggedPdf());
            var roles = pdf.StructureRoot.Children.Select(x => x.Role).ToArray();

            Assert.That(roles, Is.EqualTo(new[] { "L", "Table" }));

            var list = pdf.StructureRoot.Children[0];
            Assert.That(list.Children.Select(x => x.Role), Is.EqualTo(new[] { "LI", "LI" }));
            Assert.That(list.Children[0].Children.Select(x => x.Role), Is.EqualTo(new[] { "Lbl", "LBody" }));
            Assert.That(list.Children[1].Children.Select(x => x.Role), Is.EqualTo(new[] { "Lbl", "LBody" }));
            Assert.That(list.Children[1].Children[1].Children.Select(x => x.Role), Is.EqualTo(new[] { "P" }));

            var table = pdf.StructureRoot.Children[1];
            Assert.That(table.Children.Select(x => x.Role), Is.EqualTo(new[] { "TH", "TH", "P", "P" }));

            pdf.AssertMarkedContentIsMapped(pdf.StructureRoot);
        }

        [Test]
        public void MultiPageSemanticElementStaysOneStructureElement()
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(300, 200);
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(12));
                    page.Content().SemanticParagraph().Text(
                        string.Join("\n", Enumerable.Range(0, 120)
                            .Select(index => "Line " + index + " of the accessible document.")));
                });
            });

            using var pdf = TaggedPdfInspector.Open(document.GenerateTaggedPdf());

            Assert.That(pdf.Pages.Count, Is.GreaterThan(1));

            var paragraph = pdf.StructureRoot.Children.Single();
            Assert.That(paragraph.Role, Is.EqualTo("P"));
            Assert.That(paragraph.MarkedContent.Count, Is.EqualTo(pdf.Pages.Count));

            var referencedPages = paragraph.MarkedContent
                .Select(x => x.Elements.GetReference("/Pg"))
                .ToArray();

            Assert.That(referencedPages, Is.Unique, "Each page needs its own marked-content reference.");
            Assert.That(referencedPages.All(x => x != null), Is.True);
            Assert.That(referencedPages.Select(x => pdf.PageReferences.IndexOf(x!)).OrderBy(x => x),
                Is.EqualTo(Enumerable.Range(0, pdf.Pages.Count)));

            pdf.AssertMarkedContentIsMapped(pdf.StructureRoot);
        }

        [Test]
        public void RepeatedHeadersAndFootersAreArtifactsOnEveryPage()
        {
            var document = Document.Create(container =>
            {
                for (var index = 0; index < 2; index++)
                {
                    container.Page(page =>
                    {
                        page.Size(300, 220);
                        page.Margin(20);
                        page.Header().Text($"Page header {index + 1}").Bold();
                        page.Content().SemanticParagraph().Text("Short semantic content.");
                        page.Footer().SemanticIgnore().Text($"Page footer {index + 1}");
                    });
                }
            });

            using var pdf = TaggedPdfInspector.Open(document.GenerateTaggedPdf());

            Assert.That(pdf.Pages, Has.Count.EqualTo(2));
            Assert.That(pdf.StructureRoot.Children.Select(x => x.Role), Is.EqualTo(new[] { "P", "P" }));

            foreach (var page in pdf.Pages)
            {
                var artifacts = page.Forms.Where(x => x.Role == null).ToArray();

                Assert.That(artifacts, Has.Length.EqualTo(2),
                    "The unwrapped header and the ignored footer stay artifacts on every page.");
                Assert.That(artifacts.All(x => !x.StructParents.HasValue), Is.True);
            }

            pdf.AssertMarkedContentIsMapped(pdf.StructureRoot);
        }

        [Test]
        public void LinksReferenceTheirAnnotations()
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(320, 220);
                    page.Margin(20);
                    page.Content().Column(column =>
                    {
                        column.Item().SemanticLink("Project website")
                            .Hyperlink("https://example.com/foss-pdf")
                            .Text("Project website");

                        column.Item().SemanticLink("Jump to target")
                            .SectionLink("target")
                            .Text("Jump to target");
                    });
                });

                container.Page(page =>
                {
                    page.Size(320, 220);
                    page.Margin(20);
                    page.Content().Section("target").SemanticParagraph().Text("Target page");
                });
            });

            using var pdf = TaggedPdfInspector.Open(document.GenerateTaggedPdf());

            var links = pdf.StructureRoot.Children.Where(x => x.Role == "Link").ToArray();
            Assert.That(links, Has.Length.EqualTo(2));

            foreach (var link in links)
            {
                Assert.That(link.MarkedContent, Has.Count.EqualTo(1));
                Assert.That(link.ObjectReferences, Has.Count.EqualTo(1));
                pdf.AssertObjectReferenceIsMapped(link);
            }

            var annotations = pdf.Pages.SelectMany(x => x.Annotations).ToArray();
            Assert.That(annotations, Has.Length.EqualTo(2));

            var external = annotations.Single(x => x.Elements.ContainsKey("/A"));
            Assert.That(external.Elements.GetDictionary("/A")?.Elements.GetString("/URI"),
                Is.EqualTo("https://example.com/foss-pdf"));

            var internalLink = annotations.Single(x => x.Elements.ContainsKey("/Dest"));
            var destinationName = internalLink.Elements.GetName("/Dest");
            Assert.That(destinationName, Is.EqualTo("/target"));

            var destination = pdf.Destinations[destinationName!];
            Assert.That(pdf.ResolvePageIndex(destination.Elements[0]), Is.EqualTo(1),
                "The named destination is rewritten to the destination page, not the fragment page.");
        }

        [Test]
        public void MixedLanguagesAreWrittenOnStructureElements()
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(320, 220);
                    page.Margin(20);
                    page.Content().Column(column =>
                    {
                        column.Item().SemanticParagraph().Text("English paragraph.");
                        column.Item().SemanticLanguage("es-ES").SemanticParagraph().Text("Hola, mundo.");
                        column.Item().SemanticSpan("abbreviation")
                            .SemanticLanguage("de-DE")
                            .Text("z. B.");
                    });
                });
            }).WithMetadata(new DocumentMetadata { Language = "en-US" });

            using var pdf = TaggedPdfInspector.Open(document.GenerateTaggedPdf());

            Assert.That(pdf.Catalog.Elements.GetString("/Lang"), Is.EqualTo("en-US"));

            var children = pdf.StructureRoot.Children;
            Assert.That(children.Select(x => x.Role), Is.EqualTo(new[] { "P", "Span", "Span" }));
            Assert.That(children[0].Language, Is.Null);
            Assert.That(children[1].Language, Is.EqualTo("es-ES"));
            Assert.That(children[1].Children.Single().Role, Is.EqualTo("P"));

            // Wrappers are layout-transparent and nest, so an alternative text on an outer span and
            // a language on an inner span each stay on their own structure element.
            Assert.That(children[2].AlternativeText, Is.EqualTo("abbreviation"));
            Assert.That(children[2].Language, Is.Null);

            var languageSpan = children[2].Children.Single();
            Assert.That(languageSpan.Role, Is.EqualTo("Span"));
            Assert.That(languageSpan.Language, Is.EqualTo("de-DE"));
            Assert.That(languageSpan.MarkedContent, Has.Count.EqualTo(1));
        }

        [Test]
        public void SemanticIgnoreRemovesContentFromTheStructureTree()
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(320, 220);
                    page.Margin(20);
                    page.Content().SemanticIgnore().Column(column =>
                    {
                        column.Item().SemanticHeading1().Text("Hidden heading");
                        column.Item().SemanticParagraph().Text("Hidden paragraph");
                    });
                });
            });

            using var pdf = TaggedPdfInspector.Open(document.GenerateTaggedPdf());

            Assert.That(pdf.StructureRoot.Children, Is.Empty);

            var forms = pdf.Pages.SelectMany(x => x.Forms).ToArray();
            Assert.That(forms, Is.Not.Empty);
            Assert.That(forms.All(x => x.Role == null), Is.True);
        }

        [Test]
        public void DocumentWithoutSemanticContentIsStillMarked()
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(200, 120);
                    page.Content().Text("Plain content");
                });
            });

            using var pdf = TaggedPdfInspector.Open(document.GenerateTaggedPdf());

            Assert.That(pdf.StructureRoot.Role, Is.EqualTo("Document"));
            Assert.That(pdf.StructureRoot.Children, Is.Empty);
            Assert.That(pdf.ParentTree, Is.Empty);
            Assert.That(pdf.Catalog.Elements.GetDictionary("/MarkInfo")?.Elements.GetBoolean("/Marked"), Is.True);
        }

        [Test]
        public void MetadataIsCopiedToTheTaggedDocument()
        {
            var creation = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var modified = creation.AddDays(1);

            var document = CreateAccessibleDocument().WithMetadata(new DocumentMetadata
            {
                Title = "Accessible fixture",
                Author = "Ada Example",
                Subject = "Quarterly report",
                Keywords = "accessibility, totals",
                Creator = "FossPDF",
                Producer = "FossPDF tagged backend",
                CreationDate = creation,
                ModifiedDate = modified,
                Language = "en-US"
            });

            using var pdf = TaggedPdfInspector.Open(document.GenerateTaggedPdf());

            Assert.That(pdf.Info.Title, Is.EqualTo("Accessible fixture"));
            Assert.That(pdf.Info.Author, Is.EqualTo("Ada Example"));
            Assert.That(pdf.Info.Subject, Is.EqualTo("Quarterly report"));
            Assert.That(pdf.Info.Keywords, Is.EqualTo("accessibility, totals"));
            Assert.That(pdf.Info.Elements.GetString("/Creator"), Is.EqualTo("FossPDF"));
            // PDFsharp records its own producer while keeping the caller's value as the original.
            Assert.That(pdf.Info.Elements.GetString("/Producer"), Does.Contain("FossPDF tagged backend"));
            // PDF dates are stored in UTC; reading converts them back to local time.
            Assert.That(pdf.Info.CreationDate.ToUniversalTime(), Is.EqualTo(creation));
            Assert.That(pdf.Info.ModificationDate.ToUniversalTime(), Is.EqualTo(modified));
        }

        [Test]
        public void OverloadsWriteTheSameTaggedDocument()
        {
            var document = CreateAccessibleDocument();
            var bytes = document.GenerateTaggedPdf();
            var content = document.GenerateTaggedContent();
            var assembled = content.ToTaggedPdf();

            using (var fromBytes = TaggedPdfInspector.Open(bytes))
            using (var fromContent = TaggedPdfInspector.Open(assembled))
            {
                Assert.That(fromContent.StructureRoles(), Is.EqualTo(fromBytes.StructureRoles()));
            }

            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "parity-artifacts", "FP-401");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "overloads.pdf");
            File.WriteAllText(path, "stale");

            document.GenerateTaggedPdf(path);

            using (var fromFile = TaggedPdfInspector.Open(File.ReadAllBytes(path)))
                Assert.That(fromFile.StructureRoles(), Is.EqualTo(new[] { "Document", "H1", "P", "Figure" }));

            using var stream = new MemoryStream();
            document.GenerateTaggedPdf(stream);

            Assert.That(stream.CanRead, Is.True, "The caller-owned stream stays open.");
            Assert.That(stream.Position, Is.GreaterThan(0));

            using var readOnly = new MemoryStream(new byte[0], writable: false);
            Assert.Throws<ArgumentException>(() => document.GenerateTaggedPdf(readOnly));
            Assert.Throws<ArgumentNullException>(() => document.GenerateTaggedPdf((string)null!));
            Assert.Throws<ArgumentNullException>(() => document.GenerateTaggedPdf((Stream)null!));
            Assert.Throws<ArgumentNullException>(() => TaggedPdfExtensions.ToTaggedPdf(null!));
            Assert.Throws<ArgumentNullException>(() => TaggedPdfExtensions.GenerateTaggedPdf((IDocument)null!));
        }

        private static Document CreateAccessibleDocument()
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(320, 240);
                    page.Margin(20);
                    page.Content().Column(column =>
                    {
                        column.Item().SemanticHeading1().Text("Quarterly totals");
                        column.Item().SemanticParagraph().Text("Revenue is twelve units.");
                        column.Item().SemanticFigure("Four colored quadrants.").Height(40).Background("#ff0000");
                        column.Item().SemanticIgnore().Text("Decorative rule");
                    });
                });
            }).WithMetadata(new DocumentMetadata
            {
                Title = "Accessible fixture",
                Language = "en-US"
            });
        }
    }
}
#endif
