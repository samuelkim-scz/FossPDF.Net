using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FossPDF.Drawing;
using FossPDF.Drawing.Exceptions;
using FossPDF.Elements.Text;
using FossPDF.Elements.Text.Calculation;
using FossPDF.Elements.Text.Items;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;
using SkiaSharp;
using SharedImage = FossPDF.Infrastructure.Image;

namespace FossPDF.UnitTests.Parity
{
    [TestFixture]
    [NonParallelizable]
    [Category("FeatureParity")]
    public class ParityAcceptanceTests
    {
        private static readonly IReadOnlyDictionary<string, Action<ParityFixture>> Runners = CreateRunners();

        private static IReadOnlyDictionary<string, Action<ParityFixture>> CreateRunners()
        {
            var runners = new Dictionary<string, Action<ParityFixture>>(StringComparer.Ordinal);

            foreach (var method in typeof(ParityAcceptanceTests)
                         .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var attribute = method.GetCustomAttribute<ParityFixtureRunnerAttribute>();

                if (attribute == null)
                    continue;

                var parameters = method.GetParameters();

                if (parameters.Length != 1 || parameters[0].ParameterType != typeof(ParityFixture))
                    throw new InvalidOperationException(
                        $"Parity runner '{method.Name}' must accept a single {nameof(ParityFixture)} parameter.");

                if (!runners.TryAdd(attribute.FixtureId, (Action<ParityFixture>)method.CreateDelegate(typeof(Action<ParityFixture>))))
                    throw new InvalidOperationException($"Multiple parity runners are registered for fixture '{attribute.FixtureId}'.");
            }

            return runners;
        }

        public static IEnumerable<TestCaseData> Fixtures()
        {
            foreach (var fixture in FixtureCatalog.Load().Fixtures)
            {
                var test = new TestCaseData(fixture.Id)
                    .SetName($"Parity_{fixture.Id}_{fixture.Execution}")
                    .SetDescription(fixture.Title);
                if (fixture.Execution == "planned")
                    test.Ignore("Planned acceptance contract; implement and activate with " +
                        string.Join(", ", fixture.WorkItems) + ". This is not a parity pass.");
                yield return test;
            }
        }

        [TestCaseSource(nameof(Fixtures))]
        public void RunFixture(string fixtureId)
        {
            var fixture = FixtureCatalog.Load().Fixtures.Single(x => x.Id == fixtureId);
            Assert.That(fixture.Execution, Is.EqualTo("active"),
                "Only explicitly activated baseline fixtures may execute.");
            Assert.That(Runners.ContainsKey(fixture.Id), Is.True,
                "An active fixture must have an executable implementation.");
            Runners[fixture.Id](fixture);
        }

        [Test]
        public void Catalog_AccountsForEveryFeatureWorkItemAndExecutableFixture()
        {
            var catalog = FixtureCatalog.Load();
            using var baseline = JsonDocument.Parse(
                File.ReadAllText(FixtureCatalog.DataPath("baseline-2026.9.1.json")));
            var workItems = FixtureCatalog.ReadArray(baseline.RootElement, "work_items")
                .ToDictionary(x => x.GetProperty("id").GetString(), x => x);
            var features = workItems.Where(x =>
                    x.Value.GetProperty("milestone").GetString() != "M0" && x.Key != "FP-504")
                .Select(x => x.Key).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(catalog.SchemaVersion, Is.EqualTo(1));
                Assert.That(catalog.Target.Questpdf, Is.EqualTo(
                    baseline.RootElement.GetProperty("target").GetProperty("questpdf").GetString()));
                Assert.That(catalog.Target.FosspdfBaselineCommit, Is.EqualTo(
                    baseline.RootElement.GetProperty("target").GetProperty("fosspdf_baseline_commit").GetString()));
                Assert.That(catalog.ContractPolicy, Is.Not.Empty);
                Assert.That(catalog.Capabilities.Select(x => x.WorkItem), Is.Unique);
                Assert.That(catalog.Capabilities.Select(x => x.WorkItem), Is.EquivalentTo(features));
                Assert.That(catalog.Fixtures.Select(x => x.Id), Is.Unique);
                Assert.That(catalog.Fixtures.Where(x => x.Execution == "active").Select(x => x.Id),
                    Is.EquivalentTo(Runners.Keys));
                Assert.That(catalog.VisualPolicy.SolidChannelTolerance, Is.InRange(0, 8));
                Assert.That(catalog.VisualPolicy.MaxChangedPixelFraction, Is.InRange(0, 0.01));
                Assert.That(catalog.VisualPolicy.GeometryTolerancePoints, Is.InRange(0.0, 0.5));
            });

            var fixtures = catalog.Fixtures.ToDictionary(x => x.Id);
            foreach (var capability in catalog.Capabilities)
            {
                Assert.That(capability.BaselineStatus, Is.EqualTo(
                    workItems[capability.WorkItem].GetProperty("baseline_status").GetString()));
                Assert.That(capability.CurrentCapability, Is.Not.Empty, capability.WorkItem);
                Assert.That(capability.PublicContract, Is.Not.Empty, capability.WorkItem);
                Assert.That(capability.FixtureIds, Is.Not.Empty, capability.WorkItem);
                foreach (var id in capability.FixtureIds)
                {
                    Assert.That(fixtures.ContainsKey(id), Is.True, id);
                    Assert.That(fixtures[id].WorkItems, Does.Contain(capability.WorkItem), id);
                }
            }

            foreach (var fixture in catalog.Fixtures)
            {
                Assert.That(fixture.Id, Does.Match("^[A-Z]+-[0-9]{3}$"));
                Assert.That(fixture.Title, Is.Not.Empty, fixture.Id);
                Assert.That(fixture.Execution, Is.AnyOf("active", "planned"), fixture.Id);
                Assert.That(fixture.WorkItems, Is.Not.Empty, fixture.Id);
                Assert.That(fixture.WorkItems.All(workItems.ContainsKey), Is.True, fixture.Id);
                Assert.That(fixture.Input.ValueKind, Is.EqualTo(JsonValueKind.Object), fixture.Id);
                Assert.That(fixture.Expected, Is.Not.Empty, fixture.Id);
                Assert.That(fixture.Expected.All(x => !string.IsNullOrWhiteSpace(x)), Is.True, fixture.Id);
                if (fixture.Execution == "planned")
                    Assert.That(catalog.Capabilities.Any(x => x.FixtureIds.Contains(fixture.Id)),
                        Is.True, "Planned fixtures must trace to a feature contract: " + fixture.Id);
            }

            TestContext.Progress.WriteLine(
                $"{catalog.Fixtures.Count(x => x.Execution == "active")} executable fixtures; " +
                $"{catalog.Fixtures.Count(x => x.Execution == "planned")} planned contracts (not parity passes).");
        }

        [TestCase(1, 0.0)]
        [TestCase(2, 0.0)]
        [TestCase(3, 0.0625)]
        public void RasterTolerance_DetectsChangesOutsideTheChannelBudget(int delta, double changedFraction)
        {
            using var expected = new SKBitmap(4, 4);
            using var actual = new SKBitmap(4, 4);
            expected.Erase(SKColors.White);
            actual.Erase(SKColors.White);
            actual.SetPixel(1, 1, new SKColor((byte)(255 - delta), 255, 255));
            Assert.That(RasterComparison.ChangedPixelFraction(expected, actual, 2),
                Is.EqualTo(changedFraction));
        }

        [Test]
        public void RasterTolerance_RejectsDifferentDimensions()
        {
            using var expected = new SKBitmap(4, 4);
            using var actual = new SKBitmap(5, 4);
            Assert.Throws<ArgumentException>(() =>
                RasterComparison.ChangedPixelFraction(expected, actual, 2));
        }

        private static Document CreatePage(ParityFixture fixture, Action<PageDescriptor> content)
        {
            var size = fixture.Input.GetProperty("page");
            return Document.Create(container => container.Page(page =>
            {
                page.Size(size[0].GetInt32(), size[1].GetInt32());
                page.Margin(fixture.Input.GetProperty("margin").GetInt32());
                page.PageColor("#ffffff");
                content(page);
            })).WithMetadata(new DocumentMetadata
            {
                Title = "FossPDF independent parity fixture " + fixture.Id,
                RasterDpi = fixture.Input.TryGetProperty("dpi", out var dpi) ? dpi.GetInt32() : 72,
                CreationDate = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ModifiedDate = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        }

        private static Document ExplicitPagination(ParityFixture fixture) => CreatePage(fixture, page =>
            page.Content().Column(column =>
            {
                foreach (var block in FixtureCatalog.ReadArray(fixture.Input, "blocks"))
                {
                    if (block.ValueKind == JsonValueKind.String)
                        column.Item().PageBreak();
                    else
                        column.Item().Height(block.GetProperty("height").GetInt32())
                            .Background(block.GetProperty("color").GetString());
                }
            }));

        [ParityFixtureRunner("PAG-001")]

        private static void RunExplicitPagination(ParityFixture fixture) =>
            RenderAndCheck(ExplicitPagination(fixture), fixture);

        [ParityFixtureRunner("PAG-002")]

        private static void RunOverflowPagination(ParityFixture fixture) =>
            RenderAndCheck(CreatePage(fixture, page =>
            {
                page.Header().Height(fixture.Input.GetProperty("header_height").GetInt32()).Background("#000000");
                page.Footer().Height(fixture.Input.GetProperty("footer_height").GetInt32()).Background("#808080");
                page.Content().Column(column =>
                {
                    foreach (var color in FixtureCatalog.ReadArray(fixture.Input, "block_colors"))
                        column.Item().Height(fixture.Input.GetProperty("block_height").GetInt32())
                            .Background(color.GetString());
                });
            }), fixture);

        [ParityFixtureRunner("PAG-105")]

        private static void RunPageControls(ParityFixture fixture)
        {
            var margin = fixture.Input.GetProperty("margin").GetInt32();
            var prefixHeight = fixture.Input.GetProperty("prefix_height").GetInt32();
            var groupHeights = FixtureCatalog.ReadArray(fixture.Input, "group_heights")
                .Select(x => x.GetInt32()).ToArray();
            var marker = fixture.Input.GetProperty("repeat_marker").GetString();

            Assert.That(groupHeights.Length, Is.GreaterThanOrEqualTo(2),
                "PAG-105 requires two group heights.");
            Assert.That(marker, Is.Not.Null.And.Not.Empty);

            AssertPreventPageBreak(fixture, margin, prefixHeight, groupHeights);
            AssertRepeat(fixture, margin, marker);
            AssertPageAwareShowIf(fixture, margin);
        }

        private static void AssertPreventPageBreak(ParityFixture fixture, int margin, int prefixHeight, int[] groupHeights)
        {
            var document = CreatePage(fixture, page => page.Content().Column(column =>
            {
                column.Item().Height(prefixHeight).Background("#808080");
                column.Item().PreventPageBreak().Column(group =>
                {
                    group.Item().Height(groupHeights[0]).Background("#ff0000");
                    group.Item().Height(groupHeights[1]).Background("#0000ff");
                });
            }));

            var pages = RenderScenario(document, "prevent-page-break", 2);

            try
            {
                var centerX = margin + 40;

                Assert.That(pages[0].GetPixel(centerX, margin + 5), Is.EqualTo(SKColor.Parse("#808080")),
                    "The prefix must render on the first page.");
                Assert.That(pages[0].GetPixel(centerX, margin + prefixHeight + 5), Is.EqualTo(SKColors.White),
                    "The group must not start on the first page when the remainder is too small.");
                Assert.That(CountPixels(pages[0], IsRed), Is.EqualTo(0), "The group must not be split onto the first page.");

                Assert.That(pages[1].GetPixel(centerX, margin + 5), Is.EqualTo(SKColor.Parse("#ff0000")),
                    "The first group item must start the next page.");
                Assert.That(pages[1].GetPixel(centerX, margin + groupHeights[0] + 5), Is.EqualTo(SKColor.Parse("#0000ff")),
                    "The group must remain contiguous on the next page.");
            }
            finally
            {
                DisposePages(pages);
            }
        }

        private static void AssertRepeat(ParityFixture fixture, int margin, string marker)
        {
            var document = CreatePage(fixture, page => page.Content().Row(row =>
            {
                row.ConstantItem(20)
                    .Repeat()
                    .Height(20)
                    .Background("#ff0000")
                    .AlignCenter()
                    .Text(text => text.Span(marker).FontColor("#ffffff").FontSize(10));

                row.RelativeItem().Column(column =>
                {
                    for (var index = 0; index < 8; index++)
                        column.Item().Height(15).Background("#0000ff");
                });
            }));

            var pages = RenderScenario(document, "repeat", 2);
            var repeated = RenderScenario(document, "repeat-second-generation", 2);

            try
            {
                foreach (var page in pages)
                {
                    Assert.That(CountPixels(page, IsRed), Is.GreaterThan(100),
                        "The repeated marker must render on every page the row spans.");
                    Assert.That(CountPixels(page, IsBlue), Is.GreaterThan(1000),
                        "The continuing content must render on every page the row spans.");
                }

                for (var index = 0; index < pages.Length; index++)
                {
                    Assert.That(CountPixels(repeated[index], IsRed), Is.EqualTo(CountPixels(pages[index], IsRed)),
                        "Generating again must not consume or duplicate repeated content.");
                    Assert.That(CountPixels(repeated[index], IsBlue), Is.EqualTo(CountPixels(pages[index], IsBlue)),
                        "Repeated measurement passes must not alter content.");
                }

                Assert.That(pages[0].GetPixel(margin + 5, margin + 5), Is.EqualTo(SKColor.Parse("#ff0000")),
                    "The repeated marker belongs in the leading row cell.");
            }
            finally
            {
                DisposePages(pages);
                DisposePages(repeated);
            }
        }

        private static void AssertPageAwareShowIf(ParityFixture fixture, int margin)
        {
            var contexts = new List<(int Page, int? TotalPages)>();

            var document = CreatePage(fixture, page => page.Content().Column(column =>
            {
                column.Item().PageBreak();
                column.Item()
                    .ShowIf(context =>
                    {
                        contexts.Add((context.PageNumber, context.TotalPages));
                        return context.PageNumber % 2 == 0 && context.TotalPages == 2;
                    })
                    .Height(20)
                    .Background("#00ff00");
                column.Item().Height(20).Background("#0000ff");
            }));

            var pages = RenderScenario(document, "show-if", 2);

            try
            {
                var centerX = margin + 40;

                Assert.That(pages[0].GetPixel(centerX, margin + 5), Is.EqualTo(SKColors.White),
                    "The predicate must hide content on odd pages.");
                Assert.That(pages[1].GetPixel(centerX, margin + 5), Is.EqualTo(SKColor.Parse("#00ff00")),
                    "The predicate must show content on even pages.");
                Assert.That(pages[1].GetPixel(centerX, margin + 25), Is.EqualTo(SKColor.Parse("#0000ff")),
                    "Conditional content must not shift the following content.");

                Assert.That(contexts, Does.Contain((2, (int?)null)),
                    "The initial pass must expose an unknown total page count.");
                Assert.That(contexts, Does.Contain((2, (int?)2)),
                    "The final pass must expose the resolved total page count.");
            }
            finally
            {
                DisposePages(pages);
            }
        }

        private static SKBitmap[] RenderScenario(IDocument document, string scenario, int expectedPages)
        {
            var pdf = document.GeneratePdf(out var count);
            var images = document.GenerateImages().ToArray();

            Assert.That(Encoding.ASCII.GetString(pdf, 0, 5), Is.EqualTo("%PDF-"));
            Assert.That(count, Is.EqualTo(expectedPages), scenario + " PDF page count");
            Assert.That(images.Length, Is.EqualTo(expectedPages), scenario + " raster page count");

            SaveArtifact("PAG-105", scenario + "/document.pdf", pdf);

            var pages = new SKBitmap[images.Length];

            for (var index = 0; index < images.Length; index++)
            {
                SaveArtifact("PAG-105", $"{scenario}/page-{index + 1}.png", images[index]);
                pages[index] = SKBitmap.Decode(images[index]);
                Assert.That(pages[index], Is.Not.Null, "Raster output could not be decoded.");
            }

            return pages;
        }

        private static void DisposePages(IEnumerable<SKBitmap> pages)
        {
            foreach (var page in pages)
                page.Dispose();
        }

        private static bool IsRed(SKColor color) =>
            color.Red > 200 && color.Green < 80 && color.Blue < 80;

        private static bool IsBlue(SKColor color) =>
            color.Blue > 200 && color.Red < 80 && color.Green < 80;

        private static int CountPixels(SKBitmap bitmap, Func<SKColor, bool> predicate)
        {
            var count = 0;

            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (predicate(bitmap.GetPixel(x, y)))
                    count++;
            }

            return count;
        }

        [ParityFixtureRunner("OUT-001")]

        private static void RunRasterDpi(ParityFixture fixture) =>
            RenderAndCheck(CreatePage(fixture, page =>
                page.Content().Height(fixture.Input.GetProperty("block_height").GetInt32())
                    .Background(fixture.Input.GetProperty("color").GetString())), fixture);

        [ParityFixtureRunner("OUT-203")]

        private static void RunImageFormats(ParityFixture fixture)
        {
            var source = FixtureCatalog.Load().Fixtures.Single(x =>
                x.Id == fixture.Input.GetProperty("source").GetString());
            var document = ExplicitPagination(source);
            foreach (var name in FixtureCatalog.ReadArray(fixture.Input, "formats"))
            {
                var format = Enum.Parse<ImageOutputFormat>(name.GetString(), ignoreCase: true);
                var pages = document.GenerateImages(new ImageGenerationSettings
                {
                    Format = format,
                    RasterDpi = fixture.Input.GetProperty("dpi").GetInt32(),
                    CompressionQuality = fixture.Input.GetProperty("quality").GetInt32()
                }).ToArray();
                Assert.That(pages.Length, Is.EqualTo(2));
                for (var index = 0; index < pages.Length; index++)
                {
                    var extension = format.ToString().ToLowerInvariant();
                    SaveArtifact(fixture.Id, $"page-{index + 1}.{extension}", pages[index]);
                    using var data = SKData.CreateCopy(pages[index]);
                    using var codec = SKCodec.Create(data);
                    Assert.That(codec.EncodedFormat, Is.EqualTo(format switch
                    {
                        ImageOutputFormat.Png => SKEncodedImageFormat.Png,
                        ImageOutputFormat.Jpeg => SKEncodedImageFormat.Jpeg,
                        _ => SKEncodedImageFormat.Webp
                    }));
                    using var bitmap = SKBitmap.Decode(pages[index]);
                    Assert.That(bitmap.Width, Is.EqualTo(240));
                    Assert.That(bitmap.Height, Is.EqualTo(200));
                    var marker = bitmap.GetPixel(60, 40);
                    var expected = index == 0 ? SKColors.Red : SKColors.Blue;
                    var tolerance = format == ImageOutputFormat.Png ? 0 : 8;
                    Assert.That((int)marker.Red, Is.EqualTo((int)expected.Red).Within(tolerance));
                    Assert.That((int)marker.Green, Is.EqualTo((int)expected.Green).Within(tolerance));
                    Assert.That((int)marker.Blue, Is.EqualTo((int)expected.Blue).Within(tolerance));
                    using var decoded = SKImage.FromBitmap(bitmap);
                    using var preview = decoded.Encode(SKEncodedImageFormat.Png, 100);
                    SaveArtifact(fixture.Id, $"decoded-{extension}-{index + 1}.png", preview.ToArray());
                }
            }
        }

        [ParityFixtureRunner("SET-004")]

        private static void RunDocumentSettings(ParityFixture fixture)
        {
            var metadata = new DocumentMetadata
            {
                RasterDpi = fixture.Input.GetProperty("legacy_dpi").GetInt32()
            };
            var document = CreatePage(fixture, page => page.Content()
                    .Height(fixture.Input.GetProperty("block_height").GetInt32())
                    .Background(fixture.Input.GetProperty("color").GetString()))
                .WithMetadata(metadata)
                .WithSettings(new DocumentSettings
                {
                    ImageRasterDpi = fixture.Input.GetProperty("document_dpi").GetInt32(),
                    DocumentLayoutExceptionThreshold = 1
                });
            RenderAndCheck(document, fixture);
            Assert.That(metadata.RasterDpi, Is.EqualTo(fixture.Input.GetProperty("legacy_dpi").GetInt32()));
        }

        [ParityFixtureRunner("IMG-001")]

        private static void RunRasterImage(ParityFixture fixture)
        {
            var sourceSize = fixture.Input.GetProperty("image");
            using var bitmap = new SKBitmap(sourceSize[0].GetInt32(), sourceSize[1].GetInt32());
            using (var canvas = new SKCanvas(bitmap))
            {
                var colors = FixtureCatalog.ReadArray(fixture.Input, "quadrants");
                for (var index = 0; index < colors.Length; index++)
                {
                    using var paint = new SKPaint { Color = SKColor.Parse(colors[index].GetString()) };
                    canvas.DrawRect((index % 2) * bitmap.Width / 2f, (index / 2) * bitmap.Height / 2f,
                        bitmap.Width / 2f, bitmap.Height / 2f, paint);
                }
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            using var sharedImage = SharedImage.FromBinaryData(encoded.ToArray());
            RenderAndCheck(CreatePage(fixture, page =>
                page.Content().Width(fixture.Input.GetProperty("display_width").GetInt32())
                    .Image(sharedImage)), fixture);
        }

        private const float ParagraphGeometryTolerance = 0.01f;

        // Skia's text-blob bounds are conservative for descenders and antialiasing adds a
        // fringe, so the decoded ink box may differ by up to 2.5 pixels at the 72 DPI fixture.
        private const float ParagraphInkTolerance = 2.5f;

        [ParityFixtureRunner("TXT-101")]
        private static void RunParagraphLayout(ParityFixture fixture)
        {
            var text = fixture.Input.GetProperty("text").GetString();
            var width = fixture.Input.GetProperty("width").GetInt32();
            var indentation = fixture.Input.GetProperty("first_line_indent").GetInt32();
            var wordSpacing = fixture.Input.GetProperty("word_spacing").GetInt32();

            var scenarios = FixtureCatalog.ReadArray(fixture.Input, "scenarios")
                .Select(x => x.GetString())
                .ToArray();

            CollectionAssert.AreEquivalent(
                new[] { "ltr_justify", "rtl_start", "narrow", "word_spacing", "multipage" },
                scenarios,
                "TXT-101 scenario inventory");

            AssertJustifiedLayout(fixture, text, width, indentation);
            AssertLogicalAlignment(fixture, text, width);
            AssertNarrowLayout(fixture, text, width);
            AssertWordSpacing(fixture, text, wordSpacing);
            AssertPageContinuation(fixture, text, width, indentation);

            foreach (var scenario in scenarios)
                RenderParagraphScenario(fixture, scenario, text, width, indentation, wordSpacing);
        }

        private static void AssertJustifiedLayout(ParityFixture fixture, string text, int width, int indentation)
        {
            var style = CreateParagraphStyle(fixture);
            var block = CreateParagraphBlock(text, style, TextHorizontalAlignment.Justify, indentation);
            var lines = MeasureParagraphLines(block, width);

            Assert.That(lines.Count, Is.GreaterThan(1), "TXT-101: the justified paragraph must wrap.");
            Assert.That(lines[0].Indentation, Is.EqualTo(indentation).Within(ParagraphGeometryTolerance));
            Assert.That(lines.Skip(1).Select(x => x.Indentation), Is.All.EqualTo(0).Within(ParagraphGeometryTolerance),
                "TXT-101: continuation lines are not indented.");
            Assert.That(lines[0].BoxWidth, Is.EqualTo(width - indentation).Within(ParagraphGeometryTolerance));
            Assert.That(lines.Skip(1).Select(x => x.BoxWidth), Is.All.EqualTo(width).Within(ParagraphGeometryTolerance));
            Assert.That(lines.Take(lines.Count - 1).Select(x => x.IsLastLineOfParagraph), Is.All.False);
            Assert.That(lines.Last().IsLastLineOfParagraph, Is.True);

            foreach (var line in lines.Take(lines.Count - 1))
            {
                Assert.That(line.SpaceCount, Is.GreaterThan(0),
                    "TXT-101: a non-final justified line contains word gaps.");
                Assert.That(line.Width, Is.LessThanOrEqualTo(line.BoxWidth + ParagraphGeometryTolerance));

                var box = TextLineLayout.GetLineBox(width, ContentDirection.LeftToRight, line.Indentation);
                var additionalSpacing = TextLineLayout.GetAdditionalWordSpacing(
                    TextHorizontalAlignment.Justify, box, line.Width, line.SpaceCount);

                Assert.That(line.Width + line.SpaceCount * additionalSpacing,
                    Is.EqualTo(line.BoxWidth).Within(ParagraphGeometryTolerance),
                    "TXT-101: justification fills exactly the available line box.");
            }

            var canvas = DrawParagraph(block, width);
            Assert.That(canvas.Runs.Count, Is.EqualTo(lines.Count));
            Assert.That(canvas.Runs[0].PenX, Is.EqualTo(indentation).Within(ParagraphGeometryTolerance));
            Assert.That(canvas.Runs.Skip(1).Select(x => x.PenX), Is.All.EqualTo(0).Within(ParagraphGeometryTolerance),
                "TXT-101: only the first line starts at the indented origin.");
        }

        private static void AssertLogicalAlignment(ParityFixture fixture, string text, int width)
        {
            var ltrStyle = CreateParagraphStyle(fixture);
            var ltrStartLines = MeasureParagraphLines(
                CreateParagraphBlock(text, ltrStyle, TextHorizontalAlignment.Start, 0), width);
            var ltrStart = DrawParagraph(
                CreateParagraphBlock(text, ltrStyle, TextHorizontalAlignment.Start, 0), width);

            Assert.That(ltrStart.Runs[0].PenX, Is.EqualTo(0).Within(ParagraphGeometryTolerance),
                "TXT-101: LTR Start aligns to the left edge.");

            var ltrEndLines = MeasureParagraphLines(
                CreateParagraphBlock(text, ltrStyle, TextHorizontalAlignment.End, 0), width);
            var ltrEnd = DrawParagraph(
                CreateParagraphBlock(text, ltrStyle, TextHorizontalAlignment.End, 0), width);

            Assert.That(ltrEnd.Runs[0].PenX, Is.EqualTo(width - ltrEndLines[0].Width).Within(ParagraphGeometryTolerance),
                "TXT-101: LTR End aligns to the right edge.");

            var rtlStyle = CreateParagraphStyle(fixture, rightToLeft: true);
            var rtlStartLines = MeasureParagraphLines(
                CreateParagraphBlock(text, rtlStyle, TextHorizontalAlignment.Start, 0, ContentDirection.RightToLeft),
                width);
            var rtlStart = DrawParagraph(
                CreateParagraphBlock(text, rtlStyle, TextHorizontalAlignment.Start, 0, ContentDirection.RightToLeft),
                width);

            Assert.That(rtlStart.Runs[0].PenX, Is.EqualTo(width - rtlStartLines[0].Width).Within(ParagraphGeometryTolerance),
                "TXT-101: RTL Start is the right paragraph edge.");

            var rtlEndLines = MeasureParagraphLines(
                CreateParagraphBlock(text, rtlStyle, TextHorizontalAlignment.End, 0, ContentDirection.RightToLeft),
                width);
            var rtlEnd = DrawParagraph(
                CreateParagraphBlock(text, rtlStyle, TextHorizontalAlignment.End, 0, ContentDirection.RightToLeft),
                width);

            Assert.That(rtlEnd.Runs[0].PenX, Is.EqualTo(0).Within(ParagraphGeometryTolerance),
                "TXT-101: RTL End is the left paragraph edge.");
            Assert.That(rtlEndLines[0].Width, Is.LessThanOrEqualTo(width + ParagraphGeometryTolerance));
        }

        private static void AssertNarrowLayout(ParityFixture fixture, string text, int width)
        {
            var style = CreateParagraphStyle(fixture);
            var narrowWidth = width / 2;

            var wideLines = MeasureParagraphLines(
                CreateParagraphBlock(text, style, TextHorizontalAlignment.Start, 0), width);
            var narrowBlock = CreateParagraphBlock(text, style, TextHorizontalAlignment.Start, 0);
            var narrowLines = MeasureParagraphLines(narrowBlock, narrowWidth);

            Assert.That(narrowLines.Count, Is.GreaterThan(wideLines.Count),
                "TXT-101: a narrower box wraps earlier.");
            Assert.That(narrowLines.Select(x => x.Width),
                Is.All.LessThanOrEqualTo(narrowWidth + ParagraphGeometryTolerance));

            var canvas = DrawParagraph(narrowBlock, narrowWidth);
            Assert.That(canvas.Runs.Count, Is.EqualTo(narrowLines.Count));
            Assert.That(canvas.Runs.Select(x => x.PenX), Is.All.EqualTo(0).Within(ParagraphGeometryTolerance),
                "TXT-101: default Start alignment keeps narrow lines at the left edge.");
        }

        private static void AssertWordSpacing(ParityFixture fixture, string text, int wordSpacing)
        {
            var fontSize = fixture.Input.GetProperty("font_size").GetInt32();
            var spaces = text.Count(x => x == ' ');

            var defaultStyle = CreateParagraphStyle(fixture);
            var spacedStyle = CreateParagraphStyle(fixture, wordSpacing: wordSpacing);

            var defaultLine = MeasureParagraphLines(
                CreateParagraphBlock(text, defaultStyle, TextHorizontalAlignment.Start, 0), 10000).Single();
            var spacedLine = MeasureParagraphLines(
                CreateParagraphBlock(text, spacedStyle, TextHorizontalAlignment.Start, 0), 10000).Single();

            Assert.That(spacedLine.Width - defaultLine.Width,
                Is.EqualTo(spaces * wordSpacing * fontSize).Within(ParagraphGeometryTolerance),
                "TXT-101: word spacing is a factor of the font size.");

            var defaultWrapped = MeasureParagraphLines(
                CreateParagraphBlock(text, defaultStyle, TextHorizontalAlignment.Start, 0), defaultLine.Width);
            var spacedWrapped = MeasureParagraphLines(
                CreateParagraphBlock(text, spacedStyle, TextHorizontalAlignment.Start, 0), defaultLine.Width);

            Assert.That(spacedWrapped.Count, Is.GreaterThan(defaultWrapped.Count),
                "TXT-101: configured word spacing contributes to line breaking.");
        }

        private static void AssertPageContinuation(ParityFixture fixture, string text, int width, int indentation)
        {
            var style = CreateParagraphStyle(fixture);
            var longText = string.Join(" ", Enumerable.Repeat(text, 20));
            var block = CreateParagraphBlock(longText, style, TextHorizontalAlignment.Justify, indentation);

            block.PageContext = CreateParagraphPageContext();
            block.ResetState();

            var lines = block.DivideTextItemsIntoLines(width, 10000).ToList();
            var lineHeight = lines[0].LineHeight;

            var firstPage = new InkRecordingCanvas();
            block.Canvas = firstPage;
            block.Draw(new Size(width, lineHeight));

            Assert.That(firstPage.Runs.Count, Is.GreaterThan(0));
            Assert.That(firstPage.Runs[0].PenX, Is.EqualTo(indentation).Within(ParagraphGeometryTolerance),
                "TXT-101: page 1 begins with the indented first line.");

            var secondPage = new InkRecordingCanvas();
            block.Canvas = secondPage;
            block.Draw(new Size(width, 10000));

            Assert.That(secondPage.Runs.Count, Is.GreaterThan(0));
            Assert.That(secondPage.Runs[0].PenX, Is.EqualTo(0).Within(ParagraphGeometryTolerance),
                "TXT-101: a paragraph continued on the next page does not repeat the indentation.");
        }

        private static void RenderParagraphScenario(
            ParityFixture fixture,
            string scenario,
            string text,
            int width,
            int indentation,
            int wordSpacing)
        {
            var font = fixture.Input.GetProperty("font").GetString();
            var fontSize = fixture.Input.GetProperty("font_size").GetInt32();
            var renderedText = scenario == "multipage"
                ? string.Join(" ", Enumerable.Repeat(text, 20))
                : text;

            var document = CreatePage(fixture, page =>
            {
                page.DefaultTextStyle(style => style.FontFamily(font).FontSize(fontSize));
                ConfigureParagraphScenario(page, scenario, renderedText, width, indentation, wordSpacing);
            });

            var pdf = document.GeneratePdf(out var pageCount);
            var pages = document.GenerateImages().ToArray();

            Assert.That(Encoding.ASCII.GetString(pdf, 0, 5), Is.EqualTo("%PDF-"));
            Assert.That(pages.Length, Is.EqualTo(pageCount));

            SaveArtifact(fixture.Id, $"scenario-{scenario}.pdf", pdf);
            for (var index = 0; index < pages.Length; index++)
                SaveArtifact(fixture.Id, $"scenario-{scenario}-page-{index + 1}.png", pages[index]);

            if (scenario == "multipage")
            {
                var margin = fixture.Input.GetProperty("margin").GetInt32();
                Assert.That(pageCount, Is.GreaterThanOrEqualTo(2),
                    "TXT-101: the long paragraph spans multiple pages.");
                Assert.That(GetInkLeftInTopBand(pages[0], 16), Is.GreaterThanOrEqualTo(margin + indentation - 2),
                    "TXT-101: page 1 starts with the indented first line.");
                Assert.That(GetInkLeftInTopBand(pages[1], 16), Is.LessThanOrEqualTo(margin + 2),
                    "TXT-101: the continued paragraph on page 2 starts at the margin.");
                return;
            }

            Assert.That(pageCount, Is.EqualTo(1), $"TXT-101 {scenario}: expected a single page.");
            AssertInkMatchesLayout(fixture, scenario, pages[0], text, width, indentation, wordSpacing);
        }

        private static void ConfigureParagraphScenario(
            PageDescriptor page,
            string scenario,
            string text,
            int width,
            int indentation,
            int wordSpacing)
        {
            switch (scenario)
            {
                case "ltr_justify":
                    page.Content().Width(width).Text(descriptor =>
                    {
                        descriptor.Justify();
                        descriptor.ParagraphFirstLineIndentation(indentation);
                        descriptor.Span(text);
                    });
                    break;

                case "rtl_start":
                    page.ContentFromRightToLeft();
                    page.Content().Width(width).Text(descriptor =>
                    {
                        descriptor.AlignStart();
                        descriptor.Span(text).DirectionFromRightToLeft();
                    });
                    break;

                case "narrow":
                    page.Content().Width(width / 2).Text(descriptor =>
                    {
                        descriptor.AlignStart();
                        descriptor.Span(text);
                    });
                    break;

                case "word_spacing":
                    page.Content().Width(width).Text(descriptor =>
                    {
                        descriptor.Justify();
                        descriptor.Span(text).WordSpacing(wordSpacing);
                    });
                    break;

                case "multipage":
                    page.Content().Width(width).Text(descriptor =>
                    {
                        descriptor.Justify();
                        descriptor.ParagraphFirstLineIndentation(indentation);
                        descriptor.Span(text);
                    });
                    break;

                default:
                    Assert.Fail($"Unknown TXT-101 scenario '{scenario}'.");
                    break;
            }
        }

        private static void AssertInkMatchesLayout(
            ParityFixture fixture,
            string scenario,
            byte[] page,
            string text,
            int width,
            int indentation,
            int wordSpacing)
        {
            var scenarioWidth = scenario == "narrow" ? width / 2 : width;
            var style = CreateParagraphStyle(
                fixture,
                rightToLeft: scenario == "rtl_start",
                wordSpacing: scenario == "word_spacing" ? wordSpacing : 0);

            var justified = scenario == "ltr_justify" || scenario == "word_spacing";

            var block = CreateParagraphBlock(
                text,
                style,
                justified ? TextHorizontalAlignment.Justify : TextHorizontalAlignment.Start,
                scenario == "ltr_justify" ? indentation : 0,
                scenario == "rtl_start" ? ContentDirection.RightToLeft : ContentDirection.LeftToRight);

            var layout = DrawParagraph(block, scenarioWidth);
            Assert.That(layout.HasInk, Is.True);

            var lines = MeasureParagraphLines(block, scenarioWidth);
            var horizontal = GetHorizontalInkBounds(block, lines, scenarioWidth);

            var margin = fixture.Input.GetProperty("margin").GetInt32();
            var expected = new InkBounds(
                margin + horizontal.Left,
                margin + layout.Top,
                margin + horizontal.Right,
                margin + layout.Bottom);
            var actual = GetRasterInkBounds(page);

            Assert.That(Math.Abs(actual.Left - expected.Left), Is.LessThanOrEqualTo(ParagraphInkTolerance),
                $"{scenario} ink left: layout {expected.Left:F2}, raster {actual.Left:F2}");
            Assert.That(Math.Abs(actual.Top - expected.Top), Is.LessThanOrEqualTo(ParagraphInkTolerance),
                $"{scenario} ink top: layout {expected.Top:F2}, raster {actual.Top:F2}");
            Assert.That(Math.Abs(actual.Right - expected.Right), Is.LessThanOrEqualTo(ParagraphInkTolerance),
                $"{scenario} ink right: layout {expected.Right:F2}, raster {actual.Right:F2}");
            Assert.That(Math.Abs(actual.Bottom - expected.Bottom), Is.LessThanOrEqualTo(ParagraphInkTolerance),
                $"{scenario} ink bottom: layout {expected.Bottom:F2}, raster {actual.Bottom:F2}");
        }

        /// <summary>
        /// Horizontal ink bounds are derived from the measured glyph bearings because
        /// <see cref="SKTextBlob.Bounds"/> overestimates the justified line extent while the
        /// rasterizer draws the normalized, spacing-adjusted glyph positions.
        /// </summary>
        private static (float Left, float Right) GetHorizontalInkBounds(
            TextBlock block,
            IReadOnlyCollection<TextLine> lines,
            float width)
        {
            var alignment = block.Alignment.GetValueOrDefault(TextHorizontalAlignment.Start);
            var left = float.MaxValue;
            var right = float.MinValue;

            foreach (var line in lines)
            {
                var resolved = TextLineLayout.ResolveAlignment(
                    alignment, line.IsLastLineOfParagraph, line.SpaceCount);
                var box = TextLineLayout.GetLineBox(width, block.ContentDirection, line.Indentation);
                var additionalSpacing = TextLineLayout.GetAdditionalWordSpacing(
                    resolved, box, line.Width, line.SpaceCount);

                var renderedWidth = line.Width + line.SpaceCount * additionalSpacing;
                var cursor = TextLineLayout.GetContentStart(
                    resolved, block.ContentDirection, box, renderedWidth);

                var first = line.Elements.First().Measurement;
                var last = line.Elements.Last().Measurement;

                if (block.ContentDirection == ContentDirection.LeftToRight)
                {
                    left = Math.Min(left, cursor + first.FirstGlyphBearing);
                    right = Math.Max(right, cursor + renderedWidth - last.LastGlyphBearing);
                }
                else
                {
                    right = Math.Max(right, cursor - first.LastGlyphBearing);
                    left = Math.Min(left, cursor - renderedWidth + last.FirstGlyphBearing);
                }
            }

            return (left, right);
        }

        private static InkBounds GetRasterInkBounds(byte[] png)
        {
            using var bitmap = SKBitmap.Decode(png);
            Assert.That(bitmap, Is.Not.Null);

            var left = int.MaxValue;
            var top = int.MaxValue;
            var right = int.MinValue;
            var bottom = int.MinValue;

            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var color = bitmap.GetPixel(x, y);

                    // Include faint antialiasing so the measured box approaches the glyph
                    // outline bounds rather than only the fully covered pixels.
                    if (color.Red >= 250 && color.Green >= 250 && color.Blue >= 250)
                        continue;

                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }

            Assert.That(right, Is.GreaterThanOrEqualTo(left), "The raster page contains text ink.");
            return new InkBounds(left, top, right + 1, bottom + 1);
        }

        /// <summary>
        /// Returns the leftmost ink pixel within the first text line band. The very first ink
        /// row only contains the apex of tall glyphs, which is not a reliable left edge.
        /// </summary>
        private static int GetInkLeftInTopBand(byte[] png, int bandHeight)
        {
            using var bitmap = SKBitmap.Decode(png);
            Assert.That(bitmap, Is.Not.Null);

            var top = -1;

            for (var y = 0; y < bitmap.Height && top < 0; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var color = bitmap.GetPixel(x, y);

                    if (color.Red < 250 || color.Green < 250 || color.Blue < 250)
                    {
                        top = y;
                        break;
                    }
                }
            }

            Assert.That(top, Is.GreaterThanOrEqualTo(0), "The raster page contains no text ink.");

            var left = int.MaxValue;

            for (var y = top; y < Math.Min(bitmap.Height, top + bandHeight); y++)
            {
                for (var x = 0; x < left; x++)
                {
                    var color = bitmap.GetPixel(x, y);

                    if (color.Red < 250 || color.Green < 250 || color.Blue < 250)
                    {
                        left = x;
                        break;
                    }
                }
            }

            Assert.That(left, Is.LessThan(int.MaxValue), "The raster page contains no text ink.");
            return left;
        }

        private static TextStyle CreateParagraphStyle(
            ParityFixture fixture,
            bool rightToLeft = false,
            int wordSpacing = 0)
        {
            var style = TextStyle.Default
                .FontFamily(fixture.Input.GetProperty("font").GetString())
                .FontSize(fixture.Input.GetProperty("font_size").GetInt32())
                .WordSpacing(wordSpacing);

            if (rightToLeft)
                style = style.DirectionFromRightToLeft();

            return style.ApplyGlobalStyle();
        }

        private static TextBlock CreateParagraphBlock(
            string text,
            TextStyle style,
            TextHorizontalAlignment alignment,
            float indentation,
            ContentDirection contentDirection = ContentDirection.LeftToRight)
        {
            return new TextBlock
            {
                Alignment = alignment,
                FirstLineIndentation = indentation,
                ContentDirection = contentDirection,
                Items = new List<ITextBlockItem>
                {
                    new TextBlockSpan
                    {
                        Text = text,
                        Style = style
                    }
                }
            };
        }

        private static PageContext CreateParagraphPageContext()
        {
            return new PageContext
            {
                FontManager = FontManager.MakeDocumentSpecific(),
                CheckIfAllTextGlyphsAreAvailable = false
            };
        }

        private static List<TextLine> MeasureParagraphLines(TextBlock block, float width)
        {
            block.PageContext = CreateParagraphPageContext();
            block.ResetState();

            return block.DivideTextItemsIntoLines(width, 10000).ToList();
        }

        private static InkRecordingCanvas DrawParagraph(TextBlock block, float width, float height = 10000)
        {
            if (block.PageContext == null)
            {
                block.PageContext = CreateParagraphPageContext();
                block.ResetState();
            }

            var canvas = new InkRecordingCanvas();
            block.Canvas = canvas;
            block.Draw(new Size(width, height));

            return canvas;
        }

        private sealed class InkRecordingCanvas : ICanvas
        {
            private float _offsetX;
            private float _offsetY;

            public List<TextRun> Runs { get; } = new();

            public float Left { get; private set; } = float.MaxValue;
            public float Top { get; private set; } = float.MaxValue;
            public float Right { get; private set; } = float.MinValue;
            public float Bottom { get; private set; } = float.MinValue;

            public bool HasInk => Right > Left;

            public void Translate(Position vector)
            {
                _offsetX += vector.X;
                _offsetY += vector.Y;
            }

            public void DrawText(
                SKTextBlob skTextBlob,
                Position position,
                TextStyle style,
                DocumentSpecificFontManager fontManager)
            {
                var bounds = skTextBlob.Bounds;
                var penX = _offsetX + position.X;
                var penY = _offsetY + position.Y;

                Runs.Add(new TextRun(penX, penY, bounds));

                Left = Math.Min(Left, penX + bounds.Left);
                Top = Math.Min(Top, penY + bounds.Top);
                Right = Math.Max(Right, penX + bounds.Right);
                Bottom = Math.Max(Bottom, penY + bounds.Bottom);
            }

            public void DrawRectangle(Position vector, Size size, string color) { }
            public void DrawImage(SKImage image, Position position, Size size) { }
            public void DrawHyperlink(string url, Size size) { }
            public void DrawSectionLink(string sectionName, Size size) { }
            public void DrawSection(string sectionName) { }
            public void Rotate(float angle) { }
            public void Scale(float scaleX, float scaleY) { }
        }

        private readonly struct TextRun
        {
            public TextRun(float penX, float penY, SKRect bounds)
            {
                PenX = penX;
                PenY = penY;
                Bounds = bounds;
            }

            public float PenX { get; }
            public float PenY { get; }
            public SKRect Bounds { get; }
        }

        private readonly struct InkBounds
        {
            public InkBounds(float left, float top, float right, float bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }

            public float Left { get; }
            public float Top { get; }
            public float Right { get; }
            public float Bottom { get; }
        }

        [ParityFixtureRunner("TXT-001")]

        private static void RunStyledText(ParityFixture fixture)
        {
            var document = CreatePage(fixture, page =>
            {
                page.DefaultTextStyle(style => style.FontFamily(fixture.Input.GetProperty("font").GetString())
                    .FontSize(fixture.Input.GetProperty("font_size").GetInt32()));
                page.Content().Text(text =>
                {
                    foreach (var span in FixtureCatalog.ReadArray(fixture.Input, "spans"))
                    {
                        var descriptor = text.Span(span.GetProperty("text").GetString())
                            .FontColor(span.GetProperty("color").GetString());
                        if (span.TryGetProperty("bold", out var bold) && bold.GetBoolean())
                            descriptor.Bold();
                        if (span.TryGetProperty("italic", out var italic) && italic.GetBoolean())
                            descriptor.Italic();
                    }
                });
            });

            var pages = RenderAndCheck(document, fixture);
            using var bitmap = SKBitmap.Decode(pages.Single());
            var red = new List<SKPointI>();
            var blue = new List<SKPointI>();
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.Red > color.Green + 100 && color.Red > color.Blue + 100)
                    red.Add(new SKPointI(x, y));
                if (color.Blue > color.Red + 100 && color.Blue > color.Green + 100)
                    blue.Add(new SKPointI(x, y));
            }

            Assert.That(red.Count, Is.GreaterThan(5), "The red text run must visibly render.");
            Assert.That(blue.Count, Is.GreaterThan(5), "The blue text run must visibly render.");
            Assert.That(red.Max(x => x.X), Is.LessThan(blue.Min(x => x.X)), "Styled span order changed.");
            var margin = fixture.Input.GetProperty("margin").GetInt32();
            Assert.That(red.Concat(blue).All(point =>
                point.X >= margin && point.X < bitmap.Width - margin &&
                point.Y >= margin && point.Y < bitmap.Height - margin), Is.True,
                "Styled text escaped the content area.");
        }

        [ParityFixtureRunner("FAIL-001")]

        private static void RunMalformedImage(ParityFixture fixture)
        {
            var input = Convert.FromHexString(fixture.Input.GetProperty("image_bytes_hex").GetString());
            var document = Document.Create(container => container.Page(page => page.Content().Image(input)));
            Assert.Throws<DocumentComposeException>(() => document.GeneratePdf());
        }

        [ParityFixtureRunner("FAIL-002")]

        private static void RunImpossibleLayout(ParityFixture fixture)
        {
            var size = fixture.Input.GetProperty("content");
            var document = CreatePage(fixture, page =>
                page.Content().Width(size[0].GetInt32()).Height(size[1].GetInt32()).Background("#ff0000"));
            Assert.Throws<DocumentLayoutException>(() => document.GeneratePdf());
        }

        [ParityFixtureRunner("RES-001")]

        private static void RunCallerOwnedStream(ParityFixture fixture)
        {
            var source = FixtureCatalog.Load().Fixtures.Single(x =>
                x.Id == fixture.Input.GetProperty("source").GetString());
            using var stream = new MemoryStream();
            ExplicitPagination(source).GeneratePdf(stream);
            Assert.That(stream.CanRead && stream.CanSeek && stream.CanWrite, Is.True);
            stream.Position = 0;
            var header = new byte[5];
            Assert.That(stream.Read(header), Is.EqualTo(header.Length));
            Assert.That(Encoding.ASCII.GetString(header), Is.EqualTo("%PDF-"));
            SaveArtifact(fixture.Id, "document.pdf", stream.ToArray());
            stream.Position = stream.Length;
            Assert.DoesNotThrow(() => stream.WriteByte(0));
        }

        private static byte[][] RenderAndCheck(IDocument document, ParityFixture fixture)
        {
            var pdf = document.GeneratePdf(out var count);
            var pages = document.GenerateImages().ToArray();
            Assert.That(Encoding.ASCII.GetString(pdf, 0, 5), Is.EqualTo("%PDF-"));
            var expectedCount = fixture.Raster?.Pages.Count ?? 1;
            Assert.That(count, Is.EqualTo(expectedCount), fixture.Id + " PDF-reported page count");
            Assert.That(pages.Length, Is.EqualTo(expectedCount), fixture.Id + " raster page count");
            SaveArtifact(fixture.Id, "document.pdf", pdf);
            for (var index = 0; index < pages.Length; index++)
            {
                SaveArtifact(fixture.Id, $"page-{index + 1}.png", pages[index]);
                if (fixture.Raster != null)
                    CompareReference(fixture, index, pages[index]);
            }
            return pages;
        }

        private static void CompareReference(ParityFixture fixture, int pageIndex, byte[] bytes)
        {
            var raster = fixture.Raster;
            using var actual = SKBitmap.Decode(bytes);
            Assert.That(actual, Is.Not.Null, "Raster output could not be decoded.");
            Assert.That(actual.Width, Is.EqualTo(raster.Width));
            Assert.That(actual.Height, Is.EqualTo(raster.Height));
            using var expected = new SKBitmap(raster.Width, raster.Height);
            expected.Erase(SKColors.White);
            using (var canvas = new SKCanvas(expected))
            {
                foreach (var rectangle in raster.Pages[pageIndex])
                {
                    using var paint = new SKPaint { Color = SKColor.Parse(rectangle.Color), IsAntialias = false };
                    canvas.DrawRect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, paint);
                }
            }

            using var referenceImage = SKImage.FromBitmap(expected);
            using var referenceData = referenceImage.Encode(SKEncodedImageFormat.Png, 100);
            SaveArtifact(fixture.Id, $"reference-{pageIndex + 1}.png", referenceData.ToArray());
            var policy = FixtureCatalog.Load().VisualPolicy;
            var changed = RasterComparison.ChangedPixelFraction(expected, actual, policy.SolidChannelTolerance);
            Assert.That(changed, Is.LessThanOrEqualTo(policy.MaxChangedPixelFraction),
                $"{fixture.Id} page {pageIndex + 1}: {changed:P3} of pixels exceeded " +
                $"the {policy.SolidChannelTolerance}-level channel budget. Inspect the attached reference and actual PNGs.");
        }

        private static void SaveArtifact(string fixtureId, string name, byte[] bytes)
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "parity-artifacts", fixtureId);
            var path = Path.Combine(directory, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? directory);
            File.WriteAllBytes(path, bytes);
            TestContext.AddTestAttachment(path);
        }
    }
}
