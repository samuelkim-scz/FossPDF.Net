using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using FossPDF.Drawing;
using FossPDF.Elements;
using FossPDF.Fluent;
using FossPDF.Helpers;
using FossPDF.Infrastructure;
using FossPDF.UnitTests.TestEngine;
using FossPDF.UnitTests.TestEngine.Operations;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.UnitTests
{
    [TestFixture]
    public class LineAndShadowTests
    {
        #region Line fluent API

        [Test]
        public void LineGradient_RequiresAtLeastTwoColors()
        {
            var line = new Line();

            Action noColors = () => line.LineGradient();
            Action oneColor = () => line.LineGradient(Colors.Red.Medium);
            Action nullColors = () => line.LineGradient(null!);

            noColors.Should().Throw<ArgumentException>();
            oneColor.Should().Throw<ArgumentException>();
            nullColors.Should().Throw<ArgumentException>();
        }

        [Test]
        public void LineGradient_ValidatesEveryColor()
        {
            var line = new Line();

            Action action = () => line.LineGradient(Colors.Red.Medium, "not-a-color");

            action.Should().Throw<ArgumentException>();
        }

        [Test]
        public void LineGradient_CopiesTheColorArray()
        {
            var line = new Line();
            var colors = new[] { Colors.Red.Medium, Colors.Blue.Medium };

            line.LineGradient(colors);
            colors[0] = Colors.Green.Medium;

            line.GradientColors.Should().Equal(Colors.Red.Medium, Colors.Blue.Medium);
        }

        [Test]
        public void LineDashPattern_RejectsInvalidPatterns()
        {
            var line = new Line();

            Action nullPattern = () => line.LineDashPattern(null!);
            Action empty = () => line.LineDashPattern(Array.Empty<float>());
            Action odd = () => line.LineDashPattern(new[] { 1f, 2f, 3f });
            Action negative = () => line.LineDashPattern(new[] { 1f, -1f });
            Action nan = () => line.LineDashPattern(new[] { float.NaN, 1f });
            Action infinity = () => line.LineDashPattern(new[] { 1f, float.PositiveInfinity });
            Action allZero = () => line.LineDashPattern(new[] { 0f, 0f });

            nullPattern.Should().Throw<ArgumentNullException>();
            empty.Should().Throw<ArgumentException>();
            odd.Should().Throw<ArgumentException>();
            negative.Should().Throw<ArgumentException>();
            nan.Should().Throw<ArgumentException>();
            infinity.Should().Throw<ArgumentException>();
            allZero.Should().Throw<ArgumentException>();
        }

        [Test]
        public void LineDashPattern_RejectsNonFinitePhase()
        {
            var line = new Line();

            Action nan = () => line.LineDashPattern(new[] { 1f, 1f }, float.NaN);
            Action infinity = () => line.LineDashPattern(new[] { 1f, 1f }, float.PositiveInfinity);

            nan.Should().Throw<ArgumentOutOfRangeException>();
            infinity.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Test]
        public void LineDashPattern_ConvertsPatternAndPhaseToPoints()
        {
            var line = new Line();

            line.LineDashPattern(new[] { 2f, 1f }, 0.5f, Unit.Millimetre);

            line.DashPattern.Should().NotBeNull();
            line.DashPattern![0].Should().BeApproximately(2f * 72f / 25.4f, 0.0001f);
            line.DashPattern[1].Should().BeApproximately(1f * 72f / 25.4f, 0.0001f);
            line.DashPhase.Should().BeApproximately(0.5f * 72f / 25.4f, 0.0001f);
        }

        [Test]
        public void LineDashPattern_AcceptsZeroLengthDashWithPositiveGap()
        {
            var line = new Line();

            Action action = () => line.LineDashPattern(new[] { 0f, 4f });

            action.Should().NotThrow();
        }

        #endregion

        #region Line drawing contract

        [Test]
        public void SolidLine_KeepsLegacyRectangleDrawing()
        {
            var horizontal = new Line { Type = LineType.Horizontal, Size = 2, Color = Colors.Black };
            var operations = Draw(horizontal, new Size(100, 10));

            operations.Operations.Should().ContainSingle();
            var rectangle = operations.Operations.Single().Should().BeOfType<CanvasDrawRectangleOperation>().Subject;
            rectangle.Position.Should().Be(new Position(0, -1));
            rectangle.Size.Should().Be(new Size(100, 2));
            rectangle.Color.Should().Be(Colors.Black);
        }

        [Test]
        public void SolidVerticalLine_KeepsLegacyRectangleDrawing()
        {
            var vertical = new Line { Type = LineType.Vertical, Size = 3, Color = Colors.Red.Medium };
            var operations = Draw(vertical, new Size(10, 80));

            var rectangle = operations.Operations.Single().Should().BeOfType<CanvasDrawRectangleOperation>().Subject;
            rectangle.Position.Should().Be(new Position(-1.5f, 0));
            rectangle.Size.Should().Be(new Size(3, 80));
        }

        [Test]
        public void GradientLine_UsesTheLineDrawingContract()
        {
            var line = new Line { Type = LineType.Horizontal, Size = 4, Color = Colors.Black };
            line.LineGradient(Colors.Red.Medium, Colors.Blue.Medium);

            var operations = Draw(line, new Size(100, 10));

            var draw = operations.Operations.Single().Should().BeOfType<CanvasDrawLineOperation>().Subject;
            draw.Start.Should().Be(Position.Zero);
            draw.End.Should().Be(new Position(100, 0));
            draw.Style.Width.Should().Be(4);
            draw.Style.GradientColors.Should().Equal(Colors.Red.Medium, Colors.Blue.Medium);
            draw.Style.DashPattern.Should().BeNull();
        }

        [Test]
        public void DashedLine_UsesTheLineDrawingContract()
        {
            var line = new Line { Type = LineType.Horizontal, Size = 2 };
            line.LineDashPattern(new[] { 6f, 3f }, 2f);

            var operations = Draw(line, new Size(100, 10));

            var draw = operations.Operations.Single().Should().BeOfType<CanvasDrawLineOperation>().Subject;
            draw.Style.DashPattern.Should().Equal(6f, 3f);
            draw.Style.DashPhase.Should().Be(2f);
            draw.Style.GradientColors.Should().BeNull();
        }

        [Test]
        public void ZeroWidthLine_DoesNotRenderInk()
        {
            using var bitmap = RenderPage(72, 100, 20, page => page.Content()
                .PaddingTop(8)
                .LineHorizontal(0)
                .LineDashPattern(new[] { 6f, 3f }));

            ScanDarkRuns(bitmap, y: 10).Should().BeEmpty();
        }

        #endregion

        #region Shadow fluent API and drawing order

        [Test]
        public void Shadow_RequiresANonNullStyle()
        {
            var container = new Container();

            Action action = () => container.Shadow(null!);

            action.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void Shadow_ValidatesItsValues()
        {
            var container = new Container();

            Action negativeBlur = () => container.Shadow(new BoxShadowStyle { Blur = -1 });
            Action nanOffset = () => container.Shadow(new BoxShadowStyle { OffsetX = float.NaN });
            Action infiniteSpread = () => container.Shadow(new BoxShadowStyle { Spread = float.PositiveInfinity });
            Action invalidColor = () => container.Shadow(new BoxShadowStyle { Color = "not-a-color" });

            negativeBlur.Should().Throw<ArgumentOutOfRangeException>();
            nanOffset.Should().Throw<ArgumentOutOfRangeException>();
            infiniteSpread.Should().Throw<ArgumentOutOfRangeException>();
            invalidColor.Should().Throw<ArgumentException>();
        }

        [Test]
        public void Shadow_TargetsTheSameDecorationAsBackground()
        {
            var container = new Container();
            var style = new BoxShadowStyle { OffsetX = 2, Blur = 3 };

            var shadowed = container.Shadow(style);
            var background = shadowed.Background(Colors.Red.Medium);

            background.Should().BeSameAs(shadowed);

            var decoration = background.Should().BeAssignableTo<RectangleDecoration>().Subject;
            decoration.Shadow.Should().BeSameAs(style);
            decoration.FillColor.Should().Be(Colors.Red.Medium);
        }

        [Test]
        public void Shadow_IsPaintedBeforeFillBorderAndContent()
        {
            var style = new BoxShadowStyle { OffsetX = 2, OffsetY = 3, Blur = 4 };
            var decorated = (RectangleDecoration) new Container().Shadow(style);

            decorated.BorderColor = Colors.Black;
            decorated.Top = 1;
            decorated.Child = new Background { Color = Colors.Green.Medium };

            var operations = Draw(decorated, new Size(50, 40));

            operations.Operations.First().Should().BeOfType<CanvasDrawShadowOperation>();
            var shadow = operations.Operations.First().Should().BeOfType<CanvasDrawShadowOperation>().Subject;
            shadow.Position.Should().Be(Position.Zero);
            shadow.Size.Should().Be(new Size(50, 40));
            shadow.CornerRadius.Should().Be(BorderRadius.Zero);
            shadow.Shadow.Should().BeSameAs(style);

            var operationsList = operations.Operations.ToArray();
            var shadowIndex = Array.FindIndex(operationsList, x => x is CanvasDrawShadowOperation);
            var firstFillIndex = Array.FindIndex(operationsList, x => x is CanvasDrawRectangleOperation);
            var lastBorderIndex = Array.FindLastIndex(operationsList, x => x is CanvasDrawRectangleOperation);

            shadowIndex.Should().Be(0);
            firstFillIndex.Should().BeGreaterThan(shadowIndex);
            lastBorderIndex.Should().BeGreaterThan(firstFillIndex,
                "the border is painted after the content");
        }

        [Test]
        public void Shadow_DrawsBeforeTheClipState()
        {
            var decorated = (RectangleDecoration) new Container()
                .Shadow(new BoxShadowStyle())
                .Clip();

            decorated.Child = new Background { Color = Colors.Green.Medium };

            var operations = Draw(decorated, new Size(50, 40)).Operations.ToArray();

            operations[0].Should().BeOfType<CanvasDrawShadowOperation>();
            operations[1].Should().BeOfType<CanvasSaveStateOperation>();
            operations[2].Should().BeOfType<CanvasClipRectangleOperation>();
        }

        #endregion

        #region Dashed lines in raster output

        [Test]
        public void DashedLine_RendersTheRequestedIntervalsAndPhase()
        {
            using var bitmap = RenderPage(72, 120, 40, page => page.Content()
                .PaddingLeft(10)
                .PaddingTop(10)
                .Width(100)
                .LineHorizontal(2)
                .LineDashPattern(new[] { 6f, 3f }, 2f));

            var runs = ScanDarkRuns(bitmap, y: 10);

            var expected = new List<(float Start, float End)>
            {
                (10, 14), (17, 23), (26, 32), (35, 41), (44, 50), (53, 59),
                (62, 68), (71, 77), (80, 86), (89, 95), (98, 104), (107, 110)
            };

            AssertRuns(runs, expected, tolerance: 1.5f);
        }

        [Test]
        public void DashPhase_ShiftsThePattern()
        {
            using var noPhase = RenderPage(72, 120, 40, page => page.Content()
                .PaddingLeft(10)
                .PaddingTop(10)
                .Width(100)
                .LineHorizontal(2)
                .LineDashPattern(new[] { 6f, 3f }));

            using var phase = RenderPage(72, 120, 40, page => page.Content()
                .PaddingLeft(10)
                .PaddingTop(10)
                .Width(100)
                .LineHorizontal(2)
                .LineDashPattern(new[] { 6f, 3f }, 2f));

            var withoutPhase = ScanDarkRuns(noPhase, y: 10);
            var withPhase = ScanDarkRuns(phase, y: 10);

            withoutPhase[0].Start.Should().BeApproximately(10, 1.5f);
            withoutPhase[0].End.Should().BeApproximately(16, 1.5f);
            withPhase[0].Start.Should().BeApproximately(10, 1.5f);
            withPhase[0].End.Should().BeApproximately(14, 1.5f);
        }

        [Test]
        public void DashedLine_ConvertsMillimetersToPoints()
        {
            using var bitmap = RenderPage(72, 120, 40, page => page.Content()
                .PaddingLeft(10)
                .PaddingTop(10)
                .Width(100)
                .LineHorizontal(2)
                .LineDashPattern(new[] { 2f, 2f }, Unit.Millimetre));

            var runs = ScanDarkRuns(bitmap, y: 10);
            var dash = 2f * 72f / 25.4f;

            runs[0].Start.Should().BeApproximately(10, 1.5f);
            runs[0].End.Should().BeApproximately(10 + dash, 1.5f);
            runs[1].Start.Should().BeApproximately(10 + 2 * dash, 1.5f);
        }

        [Test]
        public void DashedLine_ScalesWithCanvasTransforms()
        {
            using var bitmap = RenderPage(72, 120, 40, page => page.Content()
                .PaddingLeft(10)
                .PaddingTop(10)
                .Width(100)
                .Height(10)
                .Scale(2)
                .LineHorizontal(2)
                .LineDashPattern(new[] { 6f, 3f }, 2f));

            var runs = ScanDarkRuns(bitmap, y: 10);

            var expected = new List<(float Start, float End)>
            {
                (10, 18), (24, 36), (42, 54), (60, 72), (78, 90), (96, 108)
            };

            AssertRuns(runs, expected, tolerance: 1.5f);

            // The dash lengths doubled with the transform, while the width also doubled.
            var lineHeight = GetVerticalInkBounds(bitmap, x: 30);
            lineHeight.Should().BeInRange(3, 5, "a 2pt line scaled twice is 4pt tall");
        }

        [Test]
        public void ZeroLengthDash_WithPositiveGap_RendersNothing()
        {
            using var bitmap = RenderPage(72, 120, 40, page => page.Content()
                .PaddingLeft(10)
                .PaddingTop(10)
                .Width(100)
                .LineHorizontal(2)
                .LineDashPattern(new[] { 0f, 4f }));

            ScanDarkRuns(bitmap, y: 10).Should().BeEmpty();
        }

        [Test]
        public void DashedLine_StaysVectorInPdfAndSvg()
        {
            var document = CreateDocument(120, 40, page => page.Content()
                .PaddingLeft(10)
                .PaddingTop(10)
                .Width(100)
                .LineHorizontal(2)
                .LineDashPattern(new[] { 6f, 3f }, 2f));

            var pdf = document.GeneratePdf();
            var svg = document.GenerateSvg().Single();

            Encoding.Latin1.GetString(pdf).Should().NotContain("/Subtype /Image",
                "dashes are geometry and must stay vector in PDF output");

            svg.Should().Contain("<path", "the dash quads are emitted as SVG paths");
            svg.Should().NotContain("<image", "dashes must not be rasterized for SVG output");

            // The content stream contains one line segment per dash edge, not a dash operator.
            var content = DecodePdfStreams(pdf);
            Regex.Matches(content, " l\n").Count.Should().BeGreaterThan(20);
        }

        #endregion

        #region Gradient lines in raster output

        [Test]
        public void GradientLine_InterpolatesFromStartToEnd()
        {
            using var bitmap = RenderPage(72, 100, 20, page => page.Content()
                .PaddingTop(8)
                .LineHorizontal(4)
                .LineGradient("#ff0000", "#0000ff"));

            AssertColorApproximately(bitmap.GetPixel(1, 8), new SKColor(255, 0, 0), 8);
            AssertColorApproximately(bitmap.GetPixel(50, 8), new SKColor(128, 0, 128), 8);
            AssertColorApproximately(bitmap.GetPixel(98, 8), new SKColor(0, 0, 255), 8);
        }

        [Test]
        public void GradientLine_SupportsMultipleStops()
        {
            using var bitmap = RenderPage(72, 100, 20, page => page.Content()
                .PaddingTop(8)
                .LineHorizontal(4)
                .LineGradient("#ff0000", "#00ff00", "#0000ff"));

            AssertColorApproximately(bitmap.GetPixel(1, 8), new SKColor(255, 0, 0), 8);
            AssertColorApproximately(bitmap.GetPixel(25, 8), new SKColor(128, 128, 0), 8);
            AssertColorApproximately(bitmap.GetPixel(50, 8), new SKColor(0, 255, 0), 8);
            AssertColorApproximately(bitmap.GetPixel(75, 8), new SKColor(0, 128, 128), 8);
            AssertColorApproximately(bitmap.GetPixel(98, 8), new SKColor(0, 0, 255), 8);
        }

        [Test]
        public void GradientLine_InterpolatesTransparentStopsUnpremultiplied()
        {
            using var bitmap = RenderPage(72, 100, 20, page => page.Content()
                .PaddingTop(8)
                .LineHorizontal(4)
                .LineGradient("#00ff0000", "#ff0000ff"));

            // Stops interpolate as unpremultiplied sRGB, so the transparent red endpoint keeps its
            // red hue while the alpha ramps up. The midpoint is half-transparent purple.
            AssertColorApproximately(bitmap.GetPixel(1, 8), SKColors.White, 8);
            AssertColorApproximately(bitmap.GetPixel(50, 8), new SKColor(191, 127, 191), 10);
            AssertColorApproximately(bitmap.GetPixel(98, 8), new SKColor(0, 0, 255), 8);
        }

        [Test]
        public void GradientLine_StaysVectorInPdfAndSvg()
        {
            var document = CreateDocument(100, 20, page => page.Content()
                .PaddingTop(8)
                .LineHorizontal(4)
                .LineGradient(Colors.Red.Medium, Colors.Blue.Medium));

            var pdf = document.GeneratePdf();
            var pdfText = Encoding.Latin1.GetString(pdf);
            var svg = document.GenerateSvg().Single();

            pdfText.Should().Contain("/ShadingType",
                "the PDF keeps the line gradient as a vector shading");
            pdfText.Should().NotContain("/Subtype /Image",
                "the line gradient must not be rasterized for PDF output");
            svg.Should().Contain("linearGradient",
                "SVG keeps the line gradient as a vector paint server");
            svg.Should().NotContain("<image");
        }

        #endregion

        #region Shadows in raster output

        [Test]
        public void SharpShadow_RespectsOffsetAndSpread()
        {
            using var bitmap = RenderPage(72, 120, 100, page => page.Content()
                .PaddingLeft(20)
                .PaddingTop(20)
                .Width(40)
                .Height(30)
                .Shadow(new BoxShadowStyle
                {
                    OffsetX = 3,
                    OffsetY = 4,
                    Spread = 2,
                    Color = Colors.Black
                }));

            var bounds = GetInkBounds(bitmap);

            bounds.Left.Should().BeInRange(20, 22);
            bounds.Top.Should().BeInRange(21, 23);
            bounds.Right.Should().BeInRange(63, 65);
            bounds.Bottom.Should().BeInRange(54, 56);
        }

        [Test]
        public void SharpShadow_IsPaintedBehindTheBackgroundAndContent()
        {
            using var bitmap = RenderPage(72, 120, 100, page => page.Content()
                .PaddingLeft(20)
                .PaddingTop(20)
                .Width(40)
                .Height(30)
                .Shadow(new BoxShadowStyle { OffsetX = 3, OffsetY = 4, Color = Colors.Black })
                .Background(Colors.Red.Medium));

            bitmap.GetPixel(30, 30).Should().Be(SKColor.Parse(Colors.Red.Medium));
            bitmap.GetPixel(59, 30).Should().Be(SKColor.Parse(Colors.Red.Medium));
            bitmap.GetPixel(61, 35).Should().Be(SKColors.Black);
            bitmap.GetPixel(30, 52).Should().Be(SKColors.Black);

            // The box corners are covered by the background, not darkened by the shadow.
            bitmap.GetPixel(20, 20).Should().Be(SKColor.Parse(Colors.Red.Medium));
        }

        [Test]
        public void SharpShadow_FollowsCornerRadii()
        {
            using var bitmap = RenderPage(72, 100, 100, page => page.Content()
                .PaddingLeft(20)
                .PaddingTop(20)
                .Width(40)
                .Height(40)
                .CornerRadius(20)
                .Shadow(new BoxShadowStyle { Color = Colors.Black }));

            bitmap.GetPixel(22, 22).Should().Be(SKColors.White);
            bitmap.GetPixel(40, 40).Should().Be(SKColors.Black);
            bitmap.GetPixel(40, 21).Should().Be(SKColors.Black);
        }

        [Test]
        public void BlurredShadow_AlphaProfileMatchesGaussianReference()
        {
            const float blur = 10;
            const float alpha = 128;

            using var bitmap = RenderPage(72, 120, 80, page => page.Content()
                .PaddingLeft(30)
                .PaddingTop(20)
                .Width(40)
                .Height(30)
                .Shadow(new BoxShadowStyle { Blur = blur, Color = "#80000000" }));

            // The box has no background, so the shadow is visible through it. At the middle of the
            // left edge, an infinite straight edge blurred by a Gaussian with sigma = blur / 2 has
            // opacity 0.5 - 0.5 * erf(d / (sigma * sqrt(2))) at signed distance d outside.
            var sigma = blur / 2;

            AssertShadowPixel(bitmap, 40, 35, 10, sigma, alpha, "shadow center");
            AssertShadowPixel(bitmap, 30, 35, 0, sigma, alpha, "shadow edge");
            AssertShadowPixel(bitmap, 25, 35, -4.5f, sigma, alpha, "shadow falloff");
            AssertShadowPixel(bitmap, 20, 35, -9.5f, sigma, alpha, "shadow outer falloff");
        }

        [Test]
        public void BlurredShadow_RespectsOffset()
        {
            using var bitmap = RenderPage(72, 120, 80, page => page.Content()
                .PaddingLeft(30)
                .PaddingTop(20)
                .Width(40)
                .Height(30)
                .Shadow(new BoxShadowStyle
                {
                    OffsetX = 10,
                    OffsetY = 0,
                    Blur = 10,
                    Color = "#80000000"
                }));

            // With a 10pt offset the shadow center is gray while the area far to its left is white.
            Assert.That(bitmap.GetPixel(50, 35).Red, Is.InRange(120, 135));
            Assert.That(bitmap.GetPixel(20, 35).Red, Is.GreaterThan(250));
        }

        [Test]
        public void BlurredShadow_UsesTheRequestedColorAndAlpha()
        {
            using var bitmap = RenderPage(72, 120, 80, page => page.Content()
                .PaddingLeft(30)
                .PaddingTop(20)
                .Width(40)
                .Height(30)
                .Shadow(new BoxShadowStyle { Blur = 4, Color = "#80ff0000" }));

            var center = bitmap.GetPixel(50, 35);

            center.Red.Should().BeInRange(247, 255);
            center.Green.Should().BeInRange(120, 134);
            center.Blue.Should().BeInRange(120, 134);
        }

        [Test]
        public void SharpShadow_StaysVectorInPdfAndSvg()
        {
            var document = CreateShadowDocument(blur: 0);

            var pdf = document.GeneratePdf();
            var svg = document.GenerateSvg().Single();

            Encoding.Latin1.GetString(pdf).Should().NotContain("/Subtype /Image",
                "a sharp shadow is vector geometry");
            svg.Should().NotContain("<image", "a sharp shadow is vector geometry");
            svg.Should().Contain("width=\"120\" height=\"80\"",
                "the sharp shadow follows the declared box geometry");
        }

        [Test]
        public void BlurredShadow_RasterizesInPdfAtTheConfiguredRasterDpi()
        {
            var document = CreateShadowDocument(blur: 10);

            var pdf72 = document.GeneratePdf();
            var pdf300 = document
                .WithSettings(new DocumentSettings { ImageRasterDpi = 300 })
                .GeneratePdf();

            var text72 = Encoding.Latin1.GetString(pdf72);
            var text300 = Encoding.Latin1.GetString(pdf300);

            text72.Should().Contain("/Subtype /Image",
                "SkiaSharp rasterizes blurred shadows inside PDF output");
            text300.Should().Contain("/Subtype /Image");

            var size72 = GetFirstImageSize(text72);
            var size300 = GetFirstImageSize(text300);

            size72.Should().NotBeNull();
            size300.Should().NotBeNull();

            // RasterDpi controls the embedded image resolution (default 72). This is a documented
            // fallback: increase ImageRasterDpi for sharper blurred shadows in PDF. Integer image
            // dimensions round the exact 300/72 ratio, so allow a few percent.
            var ratio = (float) size300!.Value.Width / size72!.Value.Width;
            ratio.Should().BeInRange(300f / 72f * 0.97f, 300f / 72f * 1.03f);
            size300.Value.Height.Should().BeGreaterThan(size72.Value.Height * 3,
                "the raster fallback scales in both dimensions");
        }

        [Test]
        public void BlurredShadow_UsesTheDocumentedRasterFallbackInSvg()
        {
            var document = CreateShadowDocument(blur: 10);
            var svg = document.GenerateSvg().Single();

            // SKSvgCanvas has no mask-filter support, so the blurred shadow is embedded as a
            // 72-DPI raster image.
            svg.Should().Contain("<image");
            svg.Should().NotContain("<filter");
        }

        [Test]
        public void BlurredShadow_SvgFallbackIsDeterministicAndRepeatable()
        {
            var document = CreateShadowDocument(blur: 6);

            var first = document.GenerateSvg().Single();
            var second = document.GenerateSvg().Single();

            second.Should().Be(first);

            // Repeated rendering exercises the temporary bitmap/canvas/paint/image disposal path.
            for (var index = 0; index < 25; index++)
                document.GenerateSvg().Single().Should().Be(first);
        }

        [Test]
        public void Rendering_IsDeterministicAcrossBackends()
        {
            var document = CreateShadowDocument(blur: 6);

            document.GeneratePdf().Should().Equal(document.GeneratePdf());
            document.GenerateSvg().Single().Should().Be(document.GenerateSvg().Single());
        }

        #endregion

        #region Helpers

        private static OperationRecordingCanvas Draw(Element element, Size availableSpace)
        {
            var canvas = new OperationRecordingCanvas();
            element.InjectDependencies(null, canvas);
            element.Draw(availableSpace);
            return canvas;
        }

        private static Document CreateShadowDocument(float blur)
        {
            return CreateDocument(300, 200, page => page.Content()
                .PaddingLeft(40)
                .PaddingTop(40)
                .Width(120)
                .Height(80)
                .Shadow(new BoxShadowStyle
                {
                    OffsetX = 10,
                    OffsetY = 15,
                    Blur = blur,
                    Color = "#80000000"
                }));
        }

        private static Document CreateDocument(float width, float height, Action<PageDescriptor> content)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(width, height);
                    page.Margin(0);
                    page.PageColor("#ffffff");
                    content(page);
                });
            }).WithMetadata(new DocumentMetadata
            {
                Title = "FossPDF FP-205 line and shadow test",
                CreationDate = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ModifiedDate = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        }

        private static SKBitmap RenderPage(int dpi, float width, float height, Action<PageDescriptor> content)
        {
            var bytes = CreateDocument(width, height, content)
                .GenerateImages(new ImageGenerationSettings { RasterDpi = dpi })
                .Single();

            return SKBitmap.Decode(bytes);
        }

        private static List<(float Start, float End)> ScanDarkRuns(SKBitmap bitmap, int y, int threshold = 128)
        {
            var runs = new List<(float Start, float End)>();
            var runStart = -1;

            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                var isDark = (color.Red + color.Green + color.Blue) / 3f < threshold;

                if (isDark && runStart < 0)
                    runStart = x;

                if (!isDark && runStart >= 0)
                {
                    runs.Add((runStart, x));
                    runStart = -1;
                }
            }

            if (runStart >= 0)
                runs.Add((runStart, bitmap.Width));

            return runs;
        }

        private static void AssertRuns(
            IReadOnlyList<(float Start, float End)> actual,
            IReadOnlyList<(float Start, float End)> expected,
            float tolerance)
        {
            actual.Should().HaveCount(expected.Count);

            for (var index = 0; index < expected.Count; index++)
            {
                actual[index].Start.Should().BeApproximately(expected[index].Start, tolerance,
                    $"dash {index} start");
                actual[index].End.Should().BeApproximately(expected[index].End, tolerance,
                    $"dash {index} end");
            }
        }

        private static float GetVerticalInkBounds(SKBitmap bitmap, int x)
        {
            var top = int.MaxValue;
            var bottom = int.MinValue;

            for (var y = 0; y < bitmap.Height; y++)
            {
                var color = bitmap.GetPixel(x, y);

                if (color.Red >= 250 && color.Green >= 250 && color.Blue >= 250)
                    continue;

                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }

            return bottom - top + 1;
        }

        private static (int Left, int Top, int Right, int Bottom) GetInkBounds(SKBitmap bitmap)
        {
            var left = int.MaxValue;
            var top = int.MaxValue;
            var right = int.MinValue;
            var bottom = int.MinValue;

            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);

                if (color.Red >= 250 && color.Green >= 250 && color.Blue >= 250)
                    continue;

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }

            Assert.That(left, Is.Not.EqualTo(int.MaxValue), "the page should contain rendered ink");

            return (left, top, right, bottom);
        }

        private static void AssertShadowPixel(
            SKBitmap bitmap, int x, int y, float distanceInside, float sigma, float alpha, string scenario)
        {
            // Signed distance inside the shadow edge: the blurred edge is the complementary error
            // function, so d = 0 gives half alpha and d >= 3 sigma is effectively opaque.
            var expectedAlpha = alpha * 0.5f * (float) Erfc(-distanceInside / (sigma * (float) Math.Sqrt(2)));
            var expectedChannel = 255 - expectedAlpha;
            var actual = bitmap.GetPixel(x, y);

            Math.Abs(actual.Red - expectedChannel).Should().BeLessThanOrEqualTo(8,
                $"{scenario}: red channel at ({x},{y}) was {actual.Red}, expected about {expectedChannel:F1}");
            Math.Abs(actual.Green - expectedChannel).Should().BeLessThanOrEqualTo(8,
                $"{scenario}: green channel at ({x},{y}) was {actual.Green}, expected about {expectedChannel:F1}");
            Math.Abs(actual.Blue - expectedChannel).Should().BeLessThanOrEqualTo(8,
                $"{scenario}: blue channel at ({x},{y}) was {actual.Blue}, expected about {expectedChannel:F1}");
        }

        private static double Erfc(double value)
        {
            // erf is not available on the netstandard2.1 library surface; use the .NET 7+ method
            // through the double-based approximation below to keep the test self-contained.
            return 1 - Erf(value);
        }

        private static double Erf(double value)
        {
            // Abramowitz and Stegun 7.1.26, absolute error below 1.5e-7.
            var sign = Math.Sign(value);
            var x = Math.Abs(value);
            var t = 1 / (1 + 0.3275911 * x);
            var polynomial = t * (0.254829592
                + t * (-0.284496736
                + t * (1.421413741
                + t * (-1.453152027
                + t * 1.061405429))));

            var erf = 1 - polynomial * Math.Exp(-x * x);

            return sign * erf;
        }

        private static (int Width, int Height)? GetFirstImageSize(string pdf)
        {
            var match = Regex.Match(pdf, @"/Subtype /Image[^>]*?/Width (\d+)[^>]*?/Height (\d+)");

            if (!match.Success)
                match = Regex.Match(pdf, @"/Width (\d+)[^>]*?/Height (\d+)[^>]*?/Subtype /Image");

            if (!match.Success)
                return null;

            return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
        }

        private static void AssertColorApproximately(SKColor actual, SKColor expected, int tolerance)
        {
            Math.Abs(actual.Red - expected.Red).Should().BeLessThanOrEqualTo(tolerance, "red channel");
            Math.Abs(actual.Green - expected.Green).Should().BeLessThanOrEqualTo(tolerance, "green channel");
            Math.Abs(actual.Blue - expected.Blue).Should().BeLessThanOrEqualTo(tolerance, "blue channel");
            Math.Abs(actual.Alpha - expected.Alpha).Should().BeLessThanOrEqualTo(tolerance, "alpha channel");
        }

        private static string DecodePdfStreams(byte[] pdf)
        {
            var raw = Encoding.Latin1.GetString(pdf);
            var result = new StringBuilder();
            var index = 0;

            while ((index = raw.IndexOf("stream", index, StringComparison.Ordinal)) >= 0)
            {
                var start = index + "stream".Length;

                if (start < raw.Length && raw[start] == '\r')
                    start++;

                if (start < raw.Length && raw[start] == '\n')
                    start++;

                var end = raw.IndexOf("endstream", start, StringComparison.Ordinal);

                if (end < 0)
                    break;

                var bytes = new byte[end - start];
                Array.Copy(pdf, start, bytes, 0, bytes.Length);

                try
                {
                    using var compressed = new System.IO.MemoryStream(bytes, writable: false);
                    using var zlib = new System.IO.Compression.ZLibStream(
                        compressed, System.IO.Compression.CompressionMode.Decompress);
                    using var output = new System.IO.MemoryStream();
                    zlib.CopyTo(output);
                    result.Append(Encoding.Latin1.GetString(output.ToArray()));
                }
                catch (System.IO.InvalidDataException)
                {
                    result.Append(Encoding.Latin1.GetString(bytes));
                }

                index = end + "endstream".Length;
            }

            return result.ToString();
        }

        #endregion
    }
}
