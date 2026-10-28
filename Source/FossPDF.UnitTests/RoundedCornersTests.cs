using System;
using System.Linq;
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
    public class RoundedCornersTests
    {
        private const float Tolerance = 0.01f;

        #region BorderRadius

        [Test]
        public void BorderRadius_Uniform_AppliesToEveryCorner()
        {
            var radius = BorderRadius.Uniform(6);

            radius.TopLeft.Should().Be(6);
            radius.TopRight.Should().Be(6);
            radius.BottomRight.Should().Be(6);
            radius.BottomLeft.Should().Be(6);
            radius.IsZero.Should().BeFalse();
        }

        [Test]
        public void BorderRadius_Zero_IsDetected()
        {
            BorderRadius.Zero.IsZero.Should().BeTrue();
            BorderRadius.Uniform(0).IsZero.Should().BeTrue();
        }

        [Test]
        public void BorderRadius_RejectsNegativeValue()
        {
            Action action = () => BorderRadius.Uniform(-1);
            action.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Test]
        public void BorderRadius_RejectsNonFiniteValue()
        {
            Action nan = () => BorderRadius.Uniform(float.NaN);
            Action infinity = () => BorderRadius.Uniform(float.PositiveInfinity);

            nan.Should().Throw<ArgumentOutOfRangeException>();
            infinity.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Test]
        public void BorderRadius_Equality_UsesEveryCorner()
        {
            var radius = new BorderRadius(1, 2, 3, 4);

            (radius == new BorderRadius(1, 2, 3, 4)).Should().BeTrue();
            (radius != new BorderRadius(1, 2, 3, 5)).Should().BeTrue();
            radius.GetHashCode().Should().Be(new BorderRadius(1, 2, 3, 4).GetHashCode());
        }

        #endregion

        #region Fluent API

        [Test]
        public void FluentStyling_TargetsOneDecorationInAnyOrder()
        {
            var container = new Container();
            var background = container.Background(Colors.Red.Medium);
            var border = background.Border(2);
            var rounded = border.CornerRadius(8);

            rounded.Should().BeSameAs(border);
            rounded.Should().BeSameAs(background);

            var decoration = rounded.Should().BeAssignableTo<RectangleDecoration>().Subject;
            decoration.FillColor.Should().Be(Colors.Red.Medium);
            decoration.Top.Should().Be(2);
            decoration.CornerRadius.Should().Be(BorderRadius.Uniform(8));
        }

        [Test]
        public void FluentStyling_BackgroundAfterBorder_RetargetsTheSameDecoration()
        {
            var container = new Container();
            var border = container.Border(3);
            var background = border.Background(Colors.Blue.Medium);

            background.Should().BeSameAs(border);

            var decoration = (RectangleDecoration) background;
            decoration.Top.Should().Be(3);
            decoration.FillColor.Should().Be(Colors.Blue.Medium);
        }

        [Test]
        public void CornerRadius_ConvertsUnitsToPoints()
        {
            var container = new Container();
            var decoration = (RectangleDecoration) container.CornerRadius(1, Unit.Inch);

            decoration.CornerRadius.Should().Be(BorderRadius.Uniform(72));
        }

        [Test]
        public void CornerRadius_PerCorner_KeepsIndependentValues()
        {
            var container = new Container();
            var decoration = (RectangleDecoration) container
                .CornerRadiusTopLeft(1)
                .CornerRadiusTopRight(2)
                .CornerRadiusBottomRight(3)
                .CornerRadiusBottomLeft(4);

            decoration.CornerRadius.Should().Be(new BorderRadius(1, 2, 3, 4));
        }

        [Test]
        public void Clip_MarksTheDecorationForClipping()
        {
            var container = new Container();
            var decoration = (RectangleDecoration) container.CornerRadius(5).Clip();

            decoration.ClipContent.Should().BeTrue();
            decoration.CornerRadius.Should().Be(BorderRadius.Uniform(5));
        }

        [Test]
        public void Clip_WithoutDecoration_CreatesAClipElement()
        {
            var container = new Container();

            container.Clip().Should().BeOfType<Clip>();
        }

        [Test]
        public void BorderAlignment_RejectsUnknownValue()
        {
            var container = new Container();

            Action action = () => container.BorderAlignment((BorderAlignment) 42);
            action.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Test]
        public void LinearGradient_RejectsInvalidColors()
        {
            Action invalidFrom = () => new LinearGradient("not-a-color", Colors.Black);
            Action invalidTo = () => new LinearGradient(Colors.Black, "#12");

            invalidFrom.Should().Throw<ArgumentException>();
            invalidTo.Should().Throw<ArgumentException>();
        }

        #endregion

        #region Drawing contracts

        [Test]
        public void SolidSquareBackground_KeepsLegacyDrawingOperation()
        {
            var element = new Background { Color = Colors.Red.Medium };

            var canvas = Draw(element, new Size(100, 50));

            canvas.Operations.OfType<CanvasDrawRectangleStyleOperation>().Should().BeEmpty();
            var operation = canvas.Operations.OfType<CanvasDrawRectangleOperation>().Single();
            operation.Position.Should().Be(Position.Zero);
            operation.Size.Should().Be(new Size(100, 50));
            operation.Color.Should().Be(Colors.Red.Medium);
        }

        [Test]
        public void RoundedBackground_EmitsStyleOperationWithCornerRadius()
        {
            var element = new Background
            {
                Color = Colors.Red.Medium,
                CornerRadius = BorderRadius.Uniform(6)
            };

            var canvas = Draw(element, new Size(100, 50));

            var operation = canvas.Operations.OfType<CanvasDrawRectangleStyleOperation>().Single();
            operation.Position.Should().Be(Position.Zero);
            operation.Size.Should().Be(new Size(100, 50));
            operation.Style.FillColor.Should().Be(Colors.Red.Medium);
            operation.Style.CornerRadius.Should().Be(BorderRadius.Uniform(6));
        }

        [Test]
        public void GradientBackground_EmitsStyleOperationWithGradient()
        {
            var gradient = LinearGradient.Horizontal(Colors.Red.Medium, Colors.Blue.Medium);
            var element = new Background { FillGradient = gradient };

            var canvas = Draw(element, new Size(100, 50));

            var operation = canvas.Operations.OfType<CanvasDrawRectangleStyleOperation>().Single();
            operation.Style.FillGradient.Should().BeSameAs(gradient);
            operation.Style.FillColor.Should().BeNull();
        }

        [Test]
        public void Border_DefaultAlignment_KeepsLegacyRectangleSequence()
        {
            var element = new Border
            {
                Top = 10,
                Right = 20,
                Bottom = 30,
                Left = 40,
                Color = Colors.Red.Medium
            };

            var canvas = Draw(element, new Size(400, 300));

            var operations = canvas.Operations.OfType<CanvasDrawRectangleOperation>().ToArray();
            operations.Should().HaveCount(4);
            operations[0].Position.Should().Be(new Position(-20, -5));
            operations[0].Size.Should().Be(new Size(430, 10));
        }

        [Test]
        public void Border_ExplicitAlignment_EmitsStyleOperation()
        {
            var element = new Border
            {
                Top = 4,
                Right = 4,
                Bottom = 4,
                Left = 4,
                Color = Colors.Black,
                CornerRadius = BorderRadius.Uniform(8),
                BorderAlignment = BorderAlignment.Outside
            };

            var canvas = Draw(element, new Size(100, 50));

            canvas.Operations.OfType<CanvasDrawRectangleOperation>().Should().BeEmpty();
            var operation = canvas.Operations.OfType<CanvasDrawRectangleStyleOperation>().Single();
            operation.Style.Top.Should().Be(4);
            operation.Style.BorderAlignment.Should().Be(BorderAlignment.Outside);
            operation.Style.CornerRadius.Should().Be(BorderRadius.Uniform(8));
        }

        [Test]
        public void Clip_DrawsSaveClipChildRestore()
        {
            var element = new Clip
            {
                CornerRadius = BorderRadius.Uniform(7),
                Child = new Background { Color = Colors.Green.Medium }
            };

            var canvas = Draw(element, new Size(100, 50));

            canvas.Operations.Should().HaveCount(4);
            canvas.Operations.ElementAt(0).Should().BeOfType<CanvasSaveStateOperation>();
            var clip = canvas.Operations.ElementAt(1).Should().BeOfType<CanvasClipRectangleOperation>().Subject;
            clip.CornerRadius.Should().Be(BorderRadius.Uniform(7));
            canvas.Operations.ElementAt(2).Should().BeOfType<CanvasDrawRectangleOperation>();
            canvas.Operations.ElementAt(3).Should().BeOfType<CanvasRestoreStateOperation>();
        }

        private static OperationRecordingCanvas Draw(Element element, Size availableSpace)
        {
            var canvas = new OperationRecordingCanvas();
            element.InjectDependencies(null, canvas);
            element.Draw(availableSpace);
            return canvas;
        }

        #endregion

        #region Raster output

        [Test]
        public void RoundedFill_CutsOutCorners()
        {
            using var bitmap = RenderPage(dpi: 72, 80, 40, page => page.Content()
                .Background(Colors.Red.Medium)
                .CornerRadius(8));

            // (1,1) lies outside the 8-point rounded corner, (4,4) lies inside it.
            bitmap.GetPixel(1, 1).Should().Be(SKColors.White);
            bitmap.GetPixel(4, 4).Should().Be(SKColor.Parse(Colors.Red.Medium));
            bitmap.GetPixel(40, 20).Should().Be(SKColor.Parse(Colors.Red.Medium));
        }

        [Test]
        public void Clip_CutsOverflowingContentAtRoundedBounds()
        {
            // The scaled child is larger than the container, so only the rounded clip keeps it in.
            using var bitmap = RenderPage(dpi: 72, 80, 40, page => page.Content()
                .CornerRadius(8)
                .Clip()
                .Scale(2)
                .Background(Colors.Blue.Medium));

            bitmap.GetPixel(1, 1).Should().Be(SKColors.White);
            bitmap.GetPixel(40, 20).Should().Be(SKColor.Parse(Colors.Blue.Medium));
        }

        [Test]
        public void Clip_DoesNotClipTheBackgroundOrTheBorder()
        {
            using var bitmap = RenderPage(dpi: 72, 80, 40, page => page.Content()
                .Background(Colors.Green.Medium)
                .Clip()
                .Border(4)
                .BorderAlignment(BorderAlignment.Inside)
                .BorderColor(Colors.Black)
                .CornerRadius(8));

            // The rounded fill and the inside-aligned border are painted outside the child clip.
            bitmap.GetPixel(40, 2).Should().Be(SKColor.Parse(Colors.Black));
            bitmap.GetPixel(40, 20).Should().Be(SKColor.Parse(Colors.Green.Medium));
            bitmap.GetPixel(1, 1).Should().Be(SKColors.White);
        }

        [Test]
        public void HorizontalGradient_InterpolatesEndpointsAndMidpoint()
        {
            using var bitmap = RenderPage(dpi: 200, 100, 20, page => page.Content()
                .BackgroundGradient("#ff0000", "#0000ff", GradientDirection.Horizontal));

            AssertColorApproximately(bitmap.GetPixel(1, 10), new SKColor(255, 0, 0), 6);
            AssertColorApproximately(bitmap.GetPixel(bitmap.Width / 2, 10), new SKColor(128, 0, 128), 6);
            AssertColorApproximately(bitmap.GetPixel(bitmap.Width - 2, 10), new SKColor(0, 0, 255), 6);
        }

        [Test]
        public void TransparentGradient_BlendsAlphaWithThePage()
        {
            using var bitmap = RenderPage(dpi: 72, 100, 20, page => page.Content()
                .BackgroundGradient("#00000000", "#ffff0000", GradientDirection.Horizontal));

            // Gradient stops are interpolated without premultiplication, so the transparent end also
            // ramps its RGB from black: the first pixels are a very light, almost neutral grey rather
            // than pure white. Only the opacity of the transparent end is contractual.
            var start = bitmap.GetPixel(1, 10);
            start.Red.Should().BeGreaterThanOrEqualTo(248);
            start.Green.Should().BeGreaterThanOrEqualTo(248);
            start.Blue.Should().BeGreaterThanOrEqualTo(248);
            Math.Abs(start.Red - start.Green).Should().BeLessThanOrEqualTo(2);
            Math.Abs(start.Red - start.Blue).Should().BeLessThanOrEqualTo(2);

            AssertColorApproximately(bitmap.GetPixel(99, 10), new SKColor(255, 0, 0), 6);
        }

        [Test]
        public void BorderAlignment_ExtentsFollowTheRequestedPlacement()
        {
            // 288 DPI gives 4 pixels per point, so one pixel is 0.25 points.
            const int dpi = 288;
            const int scale = 4;

            foreach (var alignment in new[] { BorderAlignment.Inside, BorderAlignment.Center, BorderAlignment.Outside })
            {
                using var bitmap = RenderPage(dpi, 160, 120, page => page.Content()
                    .PaddingLeft(20)
                    .PaddingTop(20)
                    .Width(80)
                    .Height(40)
                    .Border(4)
                    .BorderAlignment(alignment)
                    .BorderColor(Colors.Black));

                var bounds = GetInkBounds(bitmap);

                var expansion = alignment switch
                {
                    BorderAlignment.Inside => 0,
                    BorderAlignment.Center => 2,
                    _ => 4
                };

                var expectedLeft = (20 - expansion) * scale;
                var expectedTop = (20 - expansion) * scale;
                var expectedRight = (100 + expansion) * scale - 1;
                var expectedBottom = (60 + expansion) * scale - 1;

                bounds.Left.Should().BeInRange(expectedLeft - 1, expectedLeft + 1, alignment + " left");
                bounds.Top.Should().BeInRange(expectedTop - 1, expectedTop + 1, alignment + " top");
                bounds.Right.Should().BeInRange(expectedRight - 1, expectedRight + 1, alignment + " right");
                bounds.Bottom.Should().BeInRange(expectedBottom - 1, expectedBottom + 1, alignment + " bottom");
            }
        }

        [Test]
        public void RoundedBorder_LeavesTheCornerCutOutEmpty()
        {
            const int dpi = 288;

            using var bitmap = RenderPage(dpi, 160, 120, page => page.Content()
                .PaddingLeft(20)
                .PaddingTop(20)
                .Width(80)
                .Height(40)
                .Border(4)
                .BorderAlignment(BorderAlignment.Inside)
                .BorderColor(Colors.Black)
                .CornerRadius(8));

            // The 8-point corner arc starts at (28,28). (22,22) is outside the arc, (24,24) is
            // inside the border ring and (28,26) is inside the transparent hole.
            bitmap.GetPixel(22 * 4, 22 * 4).Should().Be(SKColors.White);
            bitmap.GetPixel(24 * 4, 24 * 4).Should().Be(SKColor.Parse(Colors.Black));
            bitmap.GetPixel(28 * 4, 26 * 4).Should().Be(SKColors.White);
        }

        [Test]
        public void SvgOutput_KeepsGradientAndClip()
        {
            // The clip only reaches the SVG output when the clipped child actually paints something,
            // so the child is a scaled background that overflows the rounded bounds.
            var document = CreateDocument(100, 50, page => page.Content()
                .CornerRadius(8)
                .Clip()
                .BackgroundGradient("#ff0000", "#0000ff")
                .Scale(2)
                .Background(Colors.Green.Medium));

            var svg = document.GenerateSvg().Single();

            svg.Should().Contain("linearGradient");
            svg.Should().Contain("clipPath");
        }

        #endregion

        #region Helpers

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
            });
        }

        private static SKBitmap RenderPage(int dpi, float width, float height, Action<PageDescriptor> content)
        {
            var bytes = CreateDocument(width, height, content)
                .GenerateImages(new ImageGenerationSettings { RasterDpi = dpi })
                .Single();

            return SKBitmap.Decode(bytes);
        }

        private static (int Left, int Top, int Right, int Bottom) GetInkBounds(SKBitmap bitmap)
        {
            var left = int.MaxValue;
            var top = int.MaxValue;
            var right = int.MinValue;
            var bottom = int.MinValue;

            for (var y = 0; y < bitmap.Height; y++)
            {
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
            }

            left.Should().NotBe(int.MaxValue, "the page should contain rendered ink");
            return (left, top, right, bottom);
        }

        private static void AssertColorApproximately(SKColor actual, SKColor expected, int tolerance)
        {
            Math.Abs(actual.Red - expected.Red).Should().BeLessThanOrEqualTo(tolerance, "red channel");
            Math.Abs(actual.Green - expected.Green).Should().BeLessThanOrEqualTo(tolerance, "green channel");
            Math.Abs(actual.Blue - expected.Blue).Should().BeLessThanOrEqualTo(tolerance, "blue channel");
            Math.Abs(actual.Alpha - expected.Alpha).Should().BeLessThanOrEqualTo(tolerance, "alpha channel");
        }

        #endregion
    }
}
