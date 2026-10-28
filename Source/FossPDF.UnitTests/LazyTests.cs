using System;
using System.Collections.Generic;
using System.Linq;
using FossPDF.Drawing;
using FossPDF.Elements;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;

namespace FossPDF.UnitTests
{
    [TestFixture]
    [NonParallelizable]
    public class LazyTests
    {
        [Test]
        public void Lazy_ProducesTheSamePageAsEagerContent()
        {
            var eager = CreateDocument(container => container.Page(page =>
            {
                ConfigurePage(page);
                page.Content().Text("deferred content");
            }));

            var lazy = CreateDocument(container => container.Page(page =>
            {
                ConfigurePage(page);
                page.Content().Lazy(content => content.Text("deferred content"));
            }));

            var eagerPages = eager.GenerateImages().ToArray();
            var lazyPages = lazy.GenerateImages().ToArray();

            Assert.That(lazyPages, Is.EqualTo(eagerPages));
        }

        [Test]
        public void Lazy_DefersCompositionAndReleasesContent()
        {
            var calls = 0;
            var lazy = new Lazy
            {
                ContentSource = content =>
                {
                    calls++;
                    content.Text("lazy");
                }
            };

            var document = CreateDocument(container => container.Page(page =>
            {
                ConfigurePage(page);
                page.Content().Element(lazy);
            }));

            Assert.That(calls, Is.Zero, "Lazy content must not be composed by the document factory.");

            document.GenerateImages().ToArray();

            Assert.That(calls, Is.GreaterThan(1),
                "A non-cached Lazy element is reconstructed for each render pass.");
            Assert.That(lazy.Child, Is.SameAs(Empty.Instance),
                "Non-cached content is released after the final render completes.");
        }

        [Test]
        public void LazyWithCache_ComposesOnceAndRetainsContent()
        {
            var calls = 0;
            var lazy = new Lazy
            {
                IsCacheable = true,
                ContentSource = content =>
                {
                    calls++;
                    content.Text("cached");
                }
            };

            var document = CreateDocument(container => container.Page(page =>
            {
                ConfigurePage(page);
                page.Content().Element(lazy);
            }));

            document.GenerateImages().ToArray();

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(lazy.Child, Is.Not.SameAs(Empty.Instance),
                "LazyWithCache retains the composed tree.");
        }

        [Test]
        public void Lazy_HandlesContentAcrossPages()
        {
            var eager = CreateDocument(container => container.Page(page =>
            {
                ConfigurePage(page);
                page.Content().Column(column =>
                {
                    for (var i = 0; i < 12; i++)
                        column.Item().Height(20).Background("#ff0000");
                });
            }));

            var lazy = CreateDocument(container => container.Page(page =>
            {
                ConfigurePage(page);
                page.Content().Lazy(content => content.Column(column =>
                {
                    for (var i = 0; i < 12; i++)
                        column.Item().Height(20).Background("#ff0000");
                }));
            }));

            var eagerPages = eager.GenerateImages().ToArray();
            var lazyPages = lazy.GenerateImages().ToArray();

            Assert.That(lazyPages.Length, Is.EqualTo(eagerPages.Length));
            Assert.That(lazyPages, Is.EqualTo(eagerPages));
        }

        [Test]
        public void Lazy_PropagatesConstructionFailuresAndAllowsRetry()
        {
            var shouldThrow = true;
            var lazy = new Lazy
            {
                ContentSource = content =>
                {
                    if (shouldThrow)
                        throw new InvalidOperationException("Construction failed.");

                    content.Text("recovered");
                }
            };

            var document = CreateDocument(container => container.Page(page =>
            {
                ConfigurePage(page);
                page.Content().Element(lazy);
            }));

            Assert.Throws<InvalidOperationException>(() => document.GenerateImages().ToArray());
            Assert.That(lazy.Child, Is.SameAs(Empty.Instance));

            shouldThrow = false;
            var pages = document.GenerateImages().ToArray();
            Assert.That(pages, Is.Not.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Lazy_RepeatedContentPreservesGlyphsForSubsetting(bool cache)
        {
            HashSet<uint> CollectGlyphs(bool deferred)
            {
                var glyphs = new HashSet<uint>();
                try
                {
                    FontManager.RegisterSubsetCallback((_, requests) =>
                    {
                        foreach (var request in requests)
                            glyphs.UnionWith(request.Glyphs);
                    });

                    var document = CreateDocument(container => container.Page(page =>
                    {
                        ConfigurePage(page);
                        var header = page.Header().Repeat();
                        if (!deferred)
                            header.Text("repeated");
                        else if (cache)
                            header.LazyWithCache(content => content.Text("repeated"));
                        else
                            header.Lazy(content => content.Text("repeated"));

                        page.Content().Column(column =>
                        {
                            column.Item().Height(20).Background("#ff0000");
                            column.Item().PageBreak();
                            column.Item().Height(20).Background("#0000ff");
                        });
                    }));

                    document.GeneratePdf(out var pages);
                    Assert.That(pages, Is.EqualTo(2));
                    return glyphs;
                }
                finally
                {
                    FontManager.RegisterSubsetCallback(null);
                }
            }

            var expected = CollectGlyphs(deferred: false);
            Assert.That(expected, Is.Not.Empty);
            Assert.That(CollectGlyphs(deferred: true), Is.EquivalentTo(expected),
                "Repeated sections must contribute their text even after a page resets their layout state.");
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void Lazy_InMultiColumnRestoresSpeculativeState(bool cache, bool balance)
        {
            Document Create(bool deferred) => CreateDocument(container => container.Page(page =>
            {
                ConfigurePage(page);
                page.Content().MultiColumn(columns =>
                {
                    columns.Columns(2);
                    columns.Spacing(10);
                    columns.BalanceHeight(balance);
                    var content = columns.Content();
                    void Compose(IContainer target) => target.Column(items =>
                    {
                        for (var index = 0; index < 12; index++)
                            items.Item().Height(20).Background(index % 2 == 0 ? "#ff0000" : "#0000ff");
                    });

                    if (!deferred)
                        Compose(content);
                    else if (cache)
                        content.LazyWithCache(Compose);
                    else
                        content.Lazy(Compose);
                });
            }));

            var expected = Create(deferred: false).GenerateImages().ToArray();
            Assert.That(expected.Length, Is.EqualTo(2));
            Assert.That(Create(deferred: true).GenerateImages().ToArray(), Is.EqualTo(expected));
        }

        private static Document CreateDocument(Action<IDocumentContainer> composition) =>
            Document.Create(composition);

        private static void ConfigurePage(PageDescriptor page)
        {
            page.Size(120, 100);
            page.Margin(10);
            page.PageColor("#ffffff");
            page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(16));
        }
    }
}
