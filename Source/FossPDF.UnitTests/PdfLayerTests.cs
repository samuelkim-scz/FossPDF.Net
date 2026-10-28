#if NET9_0
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FluentAssertions;
using FossPDF.Fluent;
using FossPDF.PdfOperations;
using NUnit.Framework;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace FossPDF.UnitTests
{
    /// <summary>
    /// FP-303: overlay and underlay files applied to selected pages of an existing PDF. The
    /// operations companion targets net9.0, so the whole fixture is compiled only for that
    /// framework.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class PdfLayerTests
    {
        [Test]
        public void OverlayFile_ChangesOnlyTheTargetedPages()
        {
            var source = CreateSource((120, 100, "#d32f2f"), (120, 100, "#1976d2"));
            var layerPath = WriteTemporaryFile(CreateLayer(120, 100, "#00ff00", (15, 15, 20, 20)));

            try
            {
                var result = DocumentOperation
                    .Load(source)
                    .OverlayFile(new DocumentOperation.LayerConfiguration
                    {
                        FilePath = layerPath,
                        TargetPages = "1"
                    })
                    .SaveToBytes();

                var sourcePages = ReadPageSignatures(source);
                var resultPages = ReadPageSignatures(result);

                resultPages.Should().HaveCount(2);
                resultPages[1].Should().Be(sourcePages[1], "page 2 is not a target");
                resultPages[0].Should().NotBe(sourcePages[0], "page 1 receives the overlay");

                var streams = ReadStreams(result, 0);
                streams.Should().HaveCount(ReadStreams(source, 0).Length + 1);
                streams[^1].Should().Contain("/L0 Do");

                var form = ReadFormContent(result, 0, "/L0");
                form.Should().Contain("0 1 0 rg");
                form.Should().Contain("15 15 20 20 re");
            }
            finally
            {
                File.Delete(layerPath);
            }
        }

        [Test]
        public void UnderlayAndOverlay_AreDrawnAroundTheOriginalContent()
        {
            var source = CreateSource((120, 100, "#d32f2f"));
            var underlayPath = WriteTemporaryFile(CreateLayer(120, 100, "#ffff00"));
            var overlayPath = WriteTemporaryFile(CreateLayer(120, 100, "#00ff00", (15, 15, 20, 20)));

            try
            {
                var result = DocumentOperation
                    .Load(source)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration { FilePath = underlayPath })
                    .OverlayFile(new DocumentOperation.LayerConfiguration { FilePath = overlayPath })
                    .SaveToBytes();

                var streams = ReadStreams(result, 0);

                streams.Should().HaveCount(3);
                streams[0].Should().Contain("/L0 Do", "the yellow underlay is first");
                streams[1].Should().Be(ReadStreams(source, 0)[0], "the original page content is in the middle");
                streams[2].Should().Contain("/L1 Do", "the green overlay is last");

                ReadFormContent(result, 0, "/L0").Should().Contain("1 1 0 rg");
                ReadFormContent(result, 0, "/L1").Should().Contain("0 1 0 rg");
            }
            finally
            {
                File.Delete(underlayPath);
                File.Delete(overlayPath);
            }
        }

        [Test]
        public void MultipleLayers_KeepTheirRegistrationOrder()
        {
            var source = CreateSource((120, 100, "#d32f2f"));
            var underlayOne = WriteTemporaryFile(CreateLayer(120, 100, "#ffff00"));
            var underlayTwo = WriteTemporaryFile(CreateLayer(120, 100, "#00ffff"));
            var overlayOne = WriteTemporaryFile(CreateLayer(120, 100, "#00ff00", (0, 0, 10, 10)));
            var overlayTwo = WriteTemporaryFile(CreateLayer(120, 100, "#ff00ff", (10, 10, 10, 10)));

            try
            {
                var result = DocumentOperation
                    .Load(source)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration { FilePath = underlayOne })
                    .UnderlayFile(new DocumentOperation.LayerConfiguration { FilePath = underlayTwo })
                    .OverlayFile(new DocumentOperation.LayerConfiguration { FilePath = overlayOne })
                    .OverlayFile(new DocumentOperation.LayerConfiguration { FilePath = overlayTwo })
                    .SaveToBytes();

                var streams = ReadStreams(result, 0);

                streams.Should().HaveCount(5);
                streams[0].Should().Contain("/L0 Do");
                streams[1].Should().Contain("/L1 Do");
                streams[2].Should().Be(ReadStreams(source, 0)[0]);
                streams[3].Should().Contain("/L2 Do");
                streams[4].Should().Contain("/L3 Do");

                ReadFormContent(result, 0, "/L0").Should().Contain("1 1 0 rg");
                ReadFormContent(result, 0, "/L1").Should().Contain("0 1 1 rg");
                ReadFormContent(result, 0, "/L2").Should().Contain("0 1 0 rg");
                ReadFormContent(result, 0, "/L3").Should().Contain("1 0 1 rg");
            }
            finally
            {
                File.Delete(underlayOne);
                File.Delete(underlayTwo);
                File.Delete(overlayOne);
                File.Delete(overlayTwo);
            }
        }

        [Test]
        public void SourcePages_AreUsedSequentiallyAndStopWhenExhausted()
        {
            var source = CreateSource(
                (120, 100, "#d32f2f"),
                (120, 100, "#1976d2"),
                (120, 100, "#388e3c"));

            var layerPath = WriteTemporaryFile(CreateLayeredPdf(
                ("#ffff00", 0),
                ("#00ffff", 0)));

            try
            {
                var result = DocumentOperation
                    .Load(source)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration { FilePath = layerPath })
                    .SaveToBytes();

                var first = ReadStreams(result, 0);
                var second = ReadStreams(result, 1);
                var third = ReadStreams(result, 2);

                first.Should().HaveCount(2);
                ReadFormContent(result, 0, "/L0").Should().Contain("1 1 0 rg");

                second.Should().HaveCount(2);
                ReadFormContent(result, 1, "/L0").Should().Contain("0 1 1 rg");

                third.Should().HaveCount(1, "the layer has no pages left");
            }
            finally
            {
                File.Delete(layerPath);
            }
        }

        [Test]
        public void RepeatSourcePages_CyclesAfterTheInitialPages()
        {
            var source = CreateSource(
                (120, 100, "#d32f2f"),
                (120, 100, "#1976d2"),
                (120, 100, "#388e3c"),
                (120, 100, "#fbc02d"));

            var layerPath = WriteTemporaryFile(CreateLayeredPdf(
                ("#ffff00", 0),
                ("#00ffff", 0)));

            try
            {
                var result = DocumentOperation
                    .Load(source)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration
                    {
                        FilePath = layerPath,
                        SourcePages = "2",
                        RepeatSourcePages = "1-2"
                    })
                    .SaveToBytes();

                ReadFormContent(result, 0, "/L0").Should().Contain("0 1 1 rg");
                ReadFormContent(result, 1, "/L0").Should().Contain("1 1 0 rg");
                ReadFormContent(result, 2, "/L0").Should().Contain("0 1 1 rg");
                ReadFormContent(result, 3, "/L0").Should().Contain("1 1 0 rg");
            }
            finally
            {
                File.Delete(layerPath);
            }
        }

        [Test]
        public void RepeatSourcePages_StartsImmediatelyWithoutInitialPages()
        {
            var source = CreateSource(
                (120, 100, "#d32f2f"),
                (120, 100, "#1976d2"),
                (120, 100, "#388e3c"));

            var layerPath = WriteTemporaryFile(CreateLayeredPdf(
                ("#ffff00", 0),
                ("#00ffff", 0)));

            try
            {
                var result = DocumentOperation
                    .Load(source)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration
                    {
                        FilePath = layerPath,
                        RepeatSourcePages = "2,1"
                    })
                    .SaveToBytes();

                ReadFormContent(result, 0, "/L0").Should().Contain("0 1 1 rg");
                ReadFormContent(result, 1, "/L0").Should().Contain("1 1 0 rg");
                ReadFormContent(result, 2, "/L0").Should().Contain("0 1 1 rg");
            }
            finally
            {
                File.Delete(layerPath);
            }
        }

        [Test]
        public void TargetPages_OrderTheSourceSequenceByTargetOrder()
        {
            var source = CreateSource(
                (120, 100, "#d32f2f"),
                (120, 100, "#1976d2"),
                (120, 100, "#388e3c"));

            var layerPath = WriteTemporaryFile(CreateLayeredPdf(
                ("#ffff00", 0),
                ("#00ffff", 0)));

            try
            {
                var result = DocumentOperation
                    .Load(source)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration
                    {
                        FilePath = layerPath,
                        TargetPages = "3,1"
                    })
                    .SaveToBytes();

                ReadFormContent(result, 0, "/L0").Should().Contain("0 1 1 rg");
                ReadStreams(result, 1).Should().HaveCount(1);
                ReadFormContent(result, 2, "/L0").Should().Contain("1 1 0 rg");
            }
            finally
            {
                File.Delete(layerPath);
            }
        }

        [Test]
        public void Placement_CentersAndScalesLayersToFit()
        {
            var source = CreateSource((120, 100, "#d32f2f"), (120, 100, "#1976d2"));

            var fitted = WriteTemporaryFile(CreateLayer(60, 50, "#ffff00"));
            var centered = WriteTemporaryFile(CreateLayer(60, 100, "#00ffff"));

            try
            {
                var fittedResult = DocumentOperation
                    .Load(source)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration { FilePath = fitted })
                    .SaveToBytes();

                ReadPlacement(ReadStreams(fittedResult, 0)[0]).Should().Be("2 0 0 2 0 0 cm");

                var centeredResult = DocumentOperation
                    .Load(source)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration { FilePath = centered })
                    .SaveToBytes();

                ReadPlacement(ReadStreams(centeredResult, 0)[0]).Should().Be("1 0 0 1 30 0 cm");
            }
            finally
            {
                File.Delete(fitted);
                File.Delete(centered);
            }
        }

        [Test]
        public void Placement_UsesTheDestinationTrimBox()
        {
            var source = CreateSource((120, 100, "#d32f2f"));
            var layerPath = WriteTemporaryFile(CreateLayer(120, 100, "#ffff00"));

            try
            {
                var trimmed = CreateSourceWithTrimBox();
                var result = DocumentOperation
                    .Load(trimmed)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration { FilePath = layerPath })
                    .SaveToBytes();

                // Destination trim box is 100x80 at (10,10); a 120x100 layer scales to 0.8 and is
                // centered, so the content-space translation is (12, 10).
                ReadPlacement(ReadStreams(result, 0)[0]).Should().Be("0.8 0 0 0.8 12 10 cm");
            }
            finally
            {
                File.Delete(layerPath);
            }
        }

        [Test]
        public void Placement_AppliesSourceRotationAndCompensatesDestinationRotation()
        {
            var identity = new PdfRectangle(new XRect(0, 0, 120, 100));
            var source = new PdfRectangle(new XRect(0, 0, 60, 100));

            AssertMatrix(PdfLayers.ComputePlacementMatrix(identity, 0, source, 90),
                1.2, 0, 0, 1.2, 0, 14);

            AssertMatrix(PdfLayers.ComputePlacementMatrix(identity, 90, source, 0),
                0, 1.2, -1.2, 0, 120, 14);

            AssertMatrix(PdfLayers.ComputePlacementMatrix(identity, 180, source, 0),
                -1, 0, 0, -1, 90, 100);

            AssertMatrix(PdfLayers.ComputePlacementMatrix(identity, 270, source, 0),
                0, -1.2, 1.2, 0, 0, 86);
        }

        [Test]
        public void RotatedLayer_CarriesItsRotationMatrixAndKeepsPageGeometry()
        {
            var source = CreateSource((120, 100, "#d32f2f"));
            var layerPath = WriteTemporaryFile(CreateLayer(60, 100, "#ffff00", rotation: 90));

            try
            {
                var result = DocumentOperation
                    .Load(source)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration { FilePath = layerPath })
                    .SaveToBytes();

                using var document = Open(result);
                document.Pages[0].Width.Point.Should().BeApproximately(120, 0.01);
                document.Pages[0].Height.Point.Should().BeApproximately(100, 0.01);

                ReadPlacement(ReadStreams(result, 0)[0]).Should().Be("1.2 0 0 1.2 0 14 cm");

                var form = ReadForms(result, 0).Single();
                form.Matrix.Should().Equal(0d, -1d, 1d, 0d, -0d, 60d);
            }
            finally
            {
                File.Delete(layerPath);
            }
        }

        [Test]
        public void LayerResources_KeepTransparencyGroupsAndExtGState()
        {
            var source = CreateSource((120, 100, "#d32f2f"));
            var layerPath = WriteTemporaryFile(CreateLayer(120, 100, "#00ff00", (10, 10, 50, 50), alpha: 128));

            try
            {
                var result = DocumentOperation
                    .Load(source)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration { FilePath = layerPath })
                    .SaveToBytes();

                var form = ReadForms(result, 0).Single();

                form.Resources.Should().ContainKey("/ExtGState");

                var extGState = form.Resources.Elements.GetDictionary("/ExtGState");
                extGState.Should().NotBeNull();

                var state = extGState!.Elements.Values
                    .OfType<PdfReference>()
                    .Select(x => x.Value as PdfDictionary)
                    .Concat(extGState.Elements.Values.OfType<PdfDictionary>())
                    .First(x => x != null);

                state!.Elements.GetReal("/ca").Should().BeApproximately(0.5, 0.01);
            }
            finally
            {
                File.Delete(layerPath);
            }
        }

        [Test]
        public void LayerSourceAndPrimaryInput_AreNotModified()
        {
            var source = CreateSource((120, 100, "#d32f2f"));
            var layer = CreateLayer(120, 100, "#ffff00");
            var layerPath = WriteTemporaryFile(layer);

            var sourceCopy = source.ToArray();
            var layerCopy = layer.ToArray();

            try
            {
                DocumentOperation
                    .Load(source)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration { FilePath = layerPath })
                    .SaveToBytes();

                source.Should().Equal(sourceCopy);
                File.ReadAllBytes(layerPath).Should().Equal(layerCopy);
            }
            finally
            {
                File.Delete(layerPath);
            }
        }

        [Test]
        public void LayerNames_DoNotOverwriteExistingPageResources()
        {
            var source = CreateSourceWithL0XObject();
            var layerPath = WriteTemporaryFile(CreateLayer(120, 100, "#ffff00"));

            try
            {
                var result = DocumentOperation
                    .Load(source)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration { FilePath = layerPath })
                    .SaveToBytes();

                var streams = ReadStreams(result, 0);

                streams.Should().Contain(x => x.Contains("/L0 Do"), "the page's own XObject stays reachable");
                streams.Should().Contain(x => x.Contains("/L1 Do"), "the layer uses a fresh resource name");
                ReadFormContent(result, 0, "/L0").Should().Contain("0 0 1 rg");
                ReadFormContent(result, 0, "/L1").Should().Contain("1 1 0 rg");
            }
            finally
            {
                File.Delete(layerPath);
            }
        }

        [Test]
        public void UncompressedLayerContentStreams_ArePreserved()
        {
            var source = CreateSource((120, 100, "#d32f2f"));
            var layer = CreateLayer(120, 100, "#ffff00", compressContent: false);
            var layerPath = WriteTemporaryFile(layer);

            try
            {
                using (var layerDocument = Open(layer))
                {
                    layerDocument.Pages[0].Contents.Elements
                        .Select(Resolve)
                        .Where(x => x?.Stream != null)
                        .Should().OnlyContain(x => !x!.Stream!.IsFiltered(),
                            "the test must exercise an unfiltered layer stream");
                }

                var result = DocumentOperation
                    .Load(source)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration { FilePath = layerPath })
                    .SaveToBytes();

                ReadFormContent(result, 0, "/L0").Should().Contain("1 1 0 rg");
            }
            finally
            {
                File.Delete(layerPath);
            }
        }

        [Test]
        public void PrimaryAnnotations_ArePreserved()
        {
            var source = Document.Create(container => container.Page(page =>
            {
                page.Size(200, 100);
                page.Margin(10);
                page.Content().Hyperlink("https://example.com").Text("Example");
            })).GeneratePdf();

            var layerPath = WriteTemporaryFile(CreateLayer(200, 100, "#ffff00", alpha: 100));

            try
            {
                var result = DocumentOperation
                    .Load(source)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration { FilePath = layerPath })
                    .SaveToBytes();

                using var sourceDocument = Open(source);
                using var resultDocument = Open(result);

                resultDocument.Pages[0].Annotations.Count.Should()
                    .Be(sourceDocument.Pages[0].Annotations.Count);
            }
            finally
            {
                File.Delete(layerPath);
            }
        }

        [Test]
        public void LayerConfiguration_IsCopiedWhenTheLayerIsAdded()
        {
            var source = CreateSource((120, 100, "#d32f2f"), (120, 100, "#1976d2"));
            var layerPath = WriteTemporaryFile(CreateLayer(120, 100, "#ffff00"));

            try
            {
                var configuration = new DocumentOperation.LayerConfiguration
                {
                    FilePath = layerPath,
                    TargetPages = "1"
                };

                var operation = DocumentOperation.Load(source).UnderlayFile(configuration);
                configuration.TargetPages = "2";

                var result = operation.SaveToBytes();

                ReadStreams(result, 0).Should().HaveCount(2);
                ReadStreams(result, 1).Should().HaveCount(1);
            }
            finally
            {
                File.Delete(layerPath);
            }
        }

        [Test]
        public void InvalidConfiguration_FailsClearly()
        {
            var source = CreateSource((120, 100, "#d32f2f"));

            Action nullConfiguration = () => DocumentOperation.Load(source).OverlayFile(null!);
            nullConfiguration.Should().Throw<ArgumentNullException>();

            Action emptyPath = () => DocumentOperation.Load(source).OverlayFile(
                new DocumentOperation.LayerConfiguration { FilePath = " " });
            emptyPath.Should().Throw<ArgumentException>();

            Action emptySelector = () => DocumentOperation.Load(source).UnderlayFile(
                new DocumentOperation.LayerConfiguration { FilePath = "layer.pdf", TargetPages = " " });
            emptySelector.Should().Throw<ArgumentException>();

            Action missingFile = () => DocumentOperation.Load(source).UnderlayFile(
                new DocumentOperation.LayerConfiguration { FilePath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.pdf") });
            missingFile.Should().Throw<FileNotFoundException>();
        }

        [Test]
        public void InvalidLayerInput_FailsClearly()
        {
            var source = CreateSource((120, 100, "#d32f2f"), (120, 100, "#1976d2"));

            var malformed = WriteTemporaryFile(new byte[] { 1, 2, 3, 4 });
            var onePage = WriteTemporaryFile(CreateLayer(120, 100, "#ffff00"));
            var encrypted = WriteTemporaryFile(CreateEncryptedLayer());

            try
            {
                Action malformedLayer = () => DocumentOperation
                    .Load(source)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration { FilePath = malformed })
                    .SaveToBytes();
                malformedLayer.Should().Throw<PdfOperationException>();

                Action encryptedLayer = () => DocumentOperation
                    .Load(source)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration { FilePath = encrypted })
                    .SaveToBytes();
                encryptedLayer.Should().Throw<PdfOperationException>();

                Action targetOutOfRange = () => DocumentOperation
                    .Load(source)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration
                    {
                        FilePath = onePage,
                        TargetPages = "3"
                    })
                    .SaveToBytes();
                targetOutOfRange.Should().Throw<PdfOperationException>();

                Action sourceOutOfRange = () => DocumentOperation
                    .Load(source)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration
                    {
                        FilePath = onePage,
                        SourcePages = "2"
                    })
                    .SaveToBytes();
                sourceOutOfRange.Should().Throw<PdfOperationException>();

                Action repeatOutOfRange = () => DocumentOperation
                    .Load(source)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration
                    {
                        FilePath = onePage,
                        RepeatSourcePages = "2"
                    })
                    .SaveToBytes();
                repeatOutOfRange.Should().Throw<PdfOperationException>();

                Action invalidSyntax = () => DocumentOperation
                    .Load(source)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration
                    {
                        FilePath = onePage,
                        TargetPages = "1-"
                    })
                    .SaveToBytes();
                invalidSyntax.Should().Throw<ArgumentException>();
            }
            finally
            {
                File.Delete(malformed);
                File.Delete(onePage);
                File.Delete(encrypted);
            }
        }

        [Test]
        public void FailedLayerOperation_DoesNotWriteToTheOutputStream()
        {
            var source = CreateSource((120, 100, "#d32f2f"));
            var malformed = WriteTemporaryFile(new byte[] { 1, 2, 3, 4 });

            try
            {
                using var output = new MemoryStream();

                Action action = () => DocumentOperation
                    .Load(source)
                    .UnderlayFile(new DocumentOperation.LayerConfiguration { FilePath = malformed })
                    .Save(output);

                action.Should().Throw<PdfOperationException>();
                output.Length.Should().Be(0);
            }
            finally
            {
                File.Delete(malformed);
            }
        }

        private static void AssertMatrix(
            double[] actual,
            double a,
            double b,
            double c,
            double d,
            double e,
            double f)
        {
            actual.Should().HaveCount(6);
            actual[0].Should().BeApproximately(a, 0.0001);
            actual[1].Should().BeApproximately(b, 0.0001);
            actual[2].Should().BeApproximately(c, 0.0001);
            actual[3].Should().BeApproximately(d, 0.0001);
            actual[4].Should().BeApproximately(e, 0.0001);
            actual[5].Should().BeApproximately(f, 0.0001);
        }

        private static string ReadPlacement(string content)
        {
            return content
                .Split('\n')
                .Select(x => x.Trim())
                .Single(x => x.EndsWith(" cm", StringComparison.Ordinal));
        }

        private static PdfFormFacts[] ReadForms(byte[] pdf, int pageIndex)
        {
            using var document = Open(pdf);
            var resources = document.Pages[pageIndex].Elements.GetDictionary("/Resources");
            var xobjects = resources?.Elements.GetDictionary("/XObject");

            if (xobjects == null)
                return Array.Empty<PdfFormFacts>();

            return xobjects.Elements.Values
                .OfType<PdfReference>()
                .Select(x => x.Value as PdfDictionary)
                .Where(x => x != null && x.Elements.GetName("/Subtype") == "/Form")
                .Select(x => new PdfFormFacts(
                    x!.Elements.GetRectangle("/BBox").ToXRect(),
                    ReadMatrix(x),
                    x.Elements.GetDictionary("/Resources")))
                .ToArray();
        }

        private static double[] ReadMatrix(PdfDictionary form)
        {
            if (!form.Elements.ContainsKey("/Matrix"))
                return Array.Empty<double>();

            return form.Elements.GetArray("/Matrix")
                .Elements
                .Select(x => x switch
                {
                    PdfReal real => real.Value,
                    PdfInteger integer => integer.Value,
                    _ => double.NaN
                })
                .Where(x => !double.IsNaN(x))
                .ToArray();
        }

        private static string[] ReadPageSignatures(byte[] pdf)
        {
            using var document = Open(pdf);

            return Enumerable.Range(0, document.PageCount)
                .Select(index =>
                {
                    var builder = new StringBuilder();

                    foreach (var item in document.Pages[index].Contents.Elements)
                    {
                        var dictionary = Resolve(item);

                        if (dictionary?.Stream?.Value != null)
                            builder.Append(Convert.ToHexString(dictionary.Stream.Value));
                    }

                    return builder.ToString();
                })
                .ToArray();
        }

        private static string ReadFormContent(byte[] pdf, int pageIndex, string name)
        {
            using var document = Open(pdf);
            var resources = document.Pages[pageIndex].Elements.GetDictionary("/Resources");
            var xobjects = resources?.Elements.GetDictionary("/XObject");
            var form = Resolve(xobjects?.Elements.GetValue(name));

            form.Should().NotBeNull($"page {pageIndex + 1} should reference the form {name}");
            return Decode(form!.Stream!.Value);
        }

        private static string[] ReadStreams(byte[] pdf, int pageIndex)
        {
            using var document = Open(pdf);

            return document.Pages[pageIndex].Contents.Elements
                .Select(Resolve)
                .Where(x => x?.Stream?.Value != null)
                .Select(x => Decode(x!.Stream!.Value))
                .ToArray();
        }

        private static PdfDictionary Resolve(PdfItem item)
        {
            return item switch
            {
                PdfReference reference => reference.Value as PdfDictionary,
                PdfDictionary dictionary => dictionary,
                _ => null
            };
        }

        private static string Decode(byte[] bytes)
        {
            try
            {
                using var compressed = new MemoryStream(bytes, false);
                using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
                using var output = new MemoryStream();
                zlib.CopyTo(output);
                return Encoding.Latin1.GetString(output.ToArray());
            }
            catch (InvalidDataException)
            {
                return Encoding.Latin1.GetString(bytes);
            }
        }

        private static PdfDocument Open(byte[] pdf)
        {
            var stream = new MemoryStream(pdf, writable: false);
            return PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        }

        private static byte[] CreateSource(params (float Width, float Height, string Color)[] pages)
        {
            return Document.Create(container =>
            {
                foreach (var page in pages)
                {
                    container.Page(descriptor =>
                    {
                        descriptor.Size(page.Width, page.Height);
                        descriptor.Margin(0);
                        descriptor.PageColor(page.Color);
                    });
                }
            }).GeneratePdf();
        }

        private static byte[] CreateSourceWithTrimBox()
        {
            using var document = new PdfDocument();
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(120);
            page.Height = XUnit.FromPoint(100);
            page.TrimBox = new PdfRectangle(new XRect(10, 10, 100, 80));
            using var graphics = XGraphics.FromPdfPage(page);
            graphics.DrawRectangle(new XSolidBrush(XColor.FromArgb(0xd3, 0x2f, 0x2f)), 0, 0, 120, 20);

            using var buffer = new MemoryStream();
            document.Save(buffer, false);
            return buffer.ToArray();
        }

        private static byte[] CreateSourceWithL0XObject()
        {
            using var document = new PdfDocument();
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(120);
            page.Height = XUnit.FromPoint(100);

            var form = new PdfDictionary(document);
            form.Elements.SetName("/Type", "/XObject");
            form.Elements.SetName("/Subtype", "/Form");
            form.Elements.SetRectangle("/BBox", new PdfRectangle(new XRect(0, 0, 120, 100)));
            form.CreateStream(Encoding.ASCII.GetBytes("q\n0 0 1 rg\n0 0 120 100 re\nf\nQ\n"));
            document.Internals.AddObject(form);

            var resources = new PdfDictionary(document);
            var xobjects = new PdfDictionary(document);
            xobjects.Elements.SetReference("/L0", PdfInternals.GetReference(form));
            resources.Elements.SetObject("/XObject", xobjects);
            page.Elements.SetObject("/Resources", resources);

            var content = new PdfDictionary(document);
            content.CreateStream(Encoding.ASCII.GetBytes("q\n1 0 0 1 0 0 cm\n/L0 Do\nQ\n"));
            document.Internals.AddObject(content);
            page.Contents.Elements.Add(PdfInternals.GetReference(content));

            using var buffer = new MemoryStream();
            document.Save(buffer, false);
            return buffer.ToArray();
        }

        private static byte[] CreateLayer(
            float width,
            float height,
            string color,
            (float X, float Y, float Width, float Height)? rectangle = null,
            int rotation = 0,
            int? alpha = null,
            bool compressContent = true)
        {
            using var document = new PdfDocument();
            document.Options.CompressContentStreams = compressContent;

            var page = document.AddPage();
            page.Width = XUnit.FromPoint(width);
            page.Height = XUnit.FromPoint(height);
            page.Rotate = rotation;

            using var graphics = XGraphics.FromPdfPage(page);
            var area = rectangle ?? (0, 0, width, height);
            var (red, green, blue) = ParseColor(color);
            var brush = alpha.HasValue
                ? new XSolidBrush(XColor.FromArgb(alpha.Value, red, green, blue))
                : new XSolidBrush(XColor.FromArgb(red, green, blue));

            // XGraphics uses a top-left origin; convert the requested PDF rectangle back.
            graphics.DrawRectangle(
                brush,
                area.X,
                height - area.Y - area.Height,
                area.Width,
                area.Height);

            using var buffer = new MemoryStream();
            document.Save(buffer, false);
            return buffer.ToArray();
        }

        private static byte[] CreateLayeredPdf(params (string Color, int Rotation)[] pages)
        {
            using var document = new PdfDocument();

            foreach (var layer in pages)
            {
                var page = document.AddPage();
                page.Width = XUnit.FromPoint(120);
                page.Height = XUnit.FromPoint(100);
                page.Rotate = layer.Rotation;

                using var graphics = XGraphics.FromPdfPage(page);
                var (red, green, blue) = ParseColor(layer.Color);
                graphics.DrawRectangle(new XSolidBrush(XColor.FromArgb(red, green, blue)), 0, 0, 120, 100);
            }

            using var buffer = new MemoryStream();
            document.Save(buffer, false);
            return buffer.ToArray();
        }

        private static byte[] CreateEncryptedLayer()
        {
            using var document = new PdfDocument();
            document.AddPage();
            document.SecuritySettings.UserPassword = "user";
            document.SecuritySettings.OwnerPassword = "owner";

            using var buffer = new MemoryStream();
            document.Save(buffer, false);
            return buffer.ToArray();
        }

        private static (int Red, int Green, int Blue) ParseColor(string color)
        {
            var value = color.TrimStart('#');
            return (
                Convert.ToInt32(value.Substring(0, 2), 16),
                Convert.ToInt32(value.Substring(2, 2), 16),
                Convert.ToInt32(value.Substring(4, 2), 16));
        }

        private static string WriteTemporaryFile(byte[] content)
        {
            var path = Path.Combine(Path.GetTempPath(), $"foss-pdf-{Guid.NewGuid():N}.pdf");
            File.WriteAllBytes(path, content);
            return path;
        }

        private readonly struct PdfFormFacts
        {
            public PdfFormFacts(XRect bounds, double[] matrix, PdfDictionary resources)
            {
                Bounds = bounds;
                Matrix = matrix;
                Resources = resources;
            }

            public XRect Bounds { get; }
            public double[] Matrix { get; }
            public PdfDictionary Resources { get; }
        }
    }
}
#endif
