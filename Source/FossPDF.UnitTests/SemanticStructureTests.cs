using System;
using System.Linq;
using System.Text;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Helpers;
using FossPDF.Infrastructure;
using NUnit.Framework;

namespace FossPDF.UnitTests
{
    [Category("SemanticTagging")]
    public class SemanticStructureTests
    {
        [Test]
        public void SemanticWrappersDoNotChangeRenderedPages()
        {
            var plain = PlainDocument().GenerateImages().ToArray();
            var semantic = SemanticDocument().GenerateImages().ToArray();

            Assert.That(semantic.Length, Is.EqualTo(plain.Length));

            for (var index = 0; index < plain.Length; index++)
                Assert.That(semantic[index], Is.EqualTo(plain[index]), $"page {index + 1} pixels changed");
        }

        [Test]
        public void CapturesRolesInReadingOrder()
        {
            var content = SemanticDocument().GenerateTaggedContent();

            Assert.That(content.PageCount, Is.EqualTo(1));
            Assert.That(content.Root.Role, Is.EqualTo("Document"));
            Assert.That(content.Root.Children.Select(x => x.Role), Is.EqualTo(new[] { "Sect" }));

            var section = content.Root.Children.Single();
            Assert.That(section.Children.Select(x => x.Role), Is.EqualTo(new[] { "H1", "P" }));

            Assert.That(content.Pages.Single().Batches
                .Where(x => x.NodeId != null)
                .Select(x => x.NodeId), Is.EqualTo(section.Children.Select(x => x.Id)));
        }

        [Test]
        public void CapturesAlternativeTextAndLanguage()
        {
            var content = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(300, 200);
                    page.Content().Column(column =>
                    {
                        column.Item().SemanticFigure("Four colored quadrants.").Height(40).Background("#ff0000");
                        column.Item().SemanticLanguage("es-ES").SemanticParagraph().Text("Hola, mundo.");
                    });
                });
            }).WithMetadata(new DocumentMetadata { Language = "en-US" }).GenerateTaggedContent();

            Assert.That(content.Metadata.Language, Is.EqualTo("en-US"));
            Assert.That(content.Root.Children.Select(x => x.Role), Is.EqualTo(new[] { "Figure", "Span" }));
            Assert.That(content.Root.Children[0].AlternativeText, Is.EqualTo("Four colored quadrants."));
            Assert.That(content.Root.Children[1].Language, Is.EqualTo("es-ES"));
            Assert.That(content.Root.Children[1].Children.Select(x => x.Role), Is.EqualTo(new[] { "P" }));
        }

        [Test]
        public void IgnoreProducesArtifactsOnly()
        {
            var content = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(300, 120);
                    page.Content().SemanticIgnore().Text("Decorative rule").Bold();
                });
            }).GenerateTaggedContent();

            Assert.That(content.Root.Children, Is.Empty);
            Assert.That(content.Pages.Single().Batches, Is.Not.Empty);
            Assert.That(content.Pages.Single().Batches.All(x => x.NodeId == null), Is.True);
        }

        [Test]
        public void NestedSemanticContentInsideIgnoreIsSuppressed()
        {
            var content = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(300, 120);
                    page.Content().SemanticIgnore().Column(column =>
                    {
                        column.Item().SemanticHeading1().Text("Hidden heading");
                        column.Item().Text("Hidden paragraph");
                    });
                });
            }).GenerateTaggedContent();

            Assert.That(content.Root.Children, Is.Empty);
            Assert.That(content.Pages.Single().Batches.All(x => x.NodeId == null), Is.True);
        }

        [Test]
        public void MultiPageNodeStaysOneStructureElement()
        {
            var content = LongDocument().GenerateTaggedContent();

            Assert.That(content.PageCount, Is.GreaterThan(1));

            var paragraph = content.Root.Children.Single();
            Assert.That(paragraph.Role, Is.EqualTo("P"));
            Assert.That(content.Pages.Count(page => page.Batches.Any(x => x.NodeId == paragraph.Id)),
                Is.EqualTo(content.PageCount));
        }

        [Test]
        public void RepeatedHeaderStaysOneArtifactAcrossPages()
        {
            var content = Document.Create(container =>
            {
                for (var index = 0; index < 2; index++)
                {
                    container.Page(page =>
                    {
                        page.Size(300, 200);
                        page.Header().SemanticIgnore().Text($"Page header");
                        page.Content().Text(Placeholders.LoremIpsum());
                    });
                }
            }).GenerateTaggedContent();

            Assert.That(content.PageCount, Is.EqualTo(2));
            Assert.That(content.Root.Children, Is.Empty);

            Assert.That(content.Pages.All(page => page.Batches.Any(x => x.NodeId == null)), Is.True);
        }

        [Test]
        public void FragmentsCarryThePageSize()
        {
            var content = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(280, 140);
                    page.Content().Column(column =>
                    {
                        column.Item().SemanticParagraph().Text("Visit the project page.");
                        column.Item().SemanticLink("Project website").Hyperlink("https://example.com/foss-pdf").Text("Project website");
                    });
                });
            }).GenerateTaggedContent();

            var pageContent = content.Pages.Single();
            Assert.That(pageContent.Width, Is.EqualTo(280));
            Assert.That(pageContent.Height, Is.EqualTo(140));

            Assert.That(pageContent.Batches.Select(batch => batch.NodeId), Is.EqualTo(new[] { "n1", "n2" }));

            var fragments = pageContent.Batches.Select(batch => Encoding.Latin1.GetString(batch.Fragment)).ToArray();

            Assert.That(fragments.All(text => text.StartsWith("%PDF-", StringComparison.Ordinal)), Is.True);
            Assert.That(fragments.All(text => text.Contains("/MediaBox")), Is.True);

            var linkFragment = fragments.Single(text => text.Contains("/Annots"));
            Assert.That(linkFragment, Does.Contain("https://example.com/foss-pdf").IgnoreCase);
        }

        [Test]
        public void ZIndexIsRejected()
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(200, 100);
                    page.Content().ZIndex(1).Text("Stacked");
                });
            });

            Assert.Throws<NotSupportedException>(() => document.GenerateTaggedContent());
        }

        [Test]
        public void QpdfOptimizationIsRejected()
        {
            var document = PlainDocument().WithSettings(new DocumentSettings
            {
                EnableAdditionalCompression = true,
                QpdfExecutablePath = "qpdf.exe"
            });

            Assert.Throws<NotSupportedException>(() => document.GenerateTaggedContent());
        }

        [Test]
        public void LanguageIsValidated()
        {
            var container = new TestContainer();

            Assert.Throws<ArgumentException>(() => container.SemanticLanguage(" "));
            Assert.Throws<ArgumentNullException>(() => SemanticExtensions.SemanticSection(null));
            Assert.Throws<ArgumentNullException>(() => SemanticExtensions.SemanticIgnore(null));
        }

        private sealed class TestContainer : IContainer
        {
            public IElement Child { get; set; }
        }

        private static Document PlainDocument()
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(320, 200);
                    page.Margin(20);
                    page.Content().Column(column =>
                    {
                        column.Item().Text("Quarterly totals").FontSize(18).Bold();
                        column.Item().PaddingTop(8).Text("Revenue is twelve units.");
                    });
                });
            });
        }

        private static Document SemanticDocument()
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(320, 200);
                    page.Margin(20);
                    page.Content().SemanticSection().Column(column =>
                    {
                        column.Item().SemanticHeading1().Text("Quarterly totals").FontSize(18).Bold();
                        column.Item().PaddingTop(8).SemanticParagraph().Text("Revenue is twelve units.");
                    });
                });
            });
        }

        private static Document LongDocument()
        {
            return Document.Create(container =>
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
        }
    }
}
