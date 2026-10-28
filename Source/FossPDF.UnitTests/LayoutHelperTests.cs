using System;
using System.Linq;
using FossPDF.Drawing;
using FossPDF.Elements;
using FossPDF.Elements.Text.Calculation;
using FossPDF.Elements.Text.Items;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.UnitTests.TestEngine;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.UnitTests
{
    [TestFixture]
    public class LayoutHelperTests
    {
        [Test]
        public void ShrinkHorizontal_RetainsTheAvailableHeight()
        {
            TestPlan
                .For(x => new Shrink
                {
                    Child = x.CreateChild(),
                    Horizontal = true
                })
                .MeasureElement(new Size(100, 80))
                .ExpectChildMeasure(expectedInput: new Size(100, 80), returns: SpacePlan.FullRender(20, 10))
                .ExpectCanvasTranslate(0, 0)
                .ExpectChildDraw(new Size(20, 80))
                .ExpectCanvasTranslate(0, 0)
                .CheckDrawResult();
        }

        [Test]
        public void ShrinkVertical_RetainsTheAvailableWidth()
        {
            TestPlan
                .For(x => new Shrink
                {
                    Child = x.CreateChild(),
                    Vertical = true
                })
                .MeasureElement(new Size(100, 80))
                .ExpectChildMeasure(expectedInput: new Size(100, 80), returns: SpacePlan.FullRender(20, 10))
                .ExpectCanvasTranslate(0, 0)
                .ExpectChildDraw(new Size(100, 10))
                .ExpectCanvasTranslate(0, 0)
                .CheckDrawResult();
        }

        [Test]
        public void Shrink_RetainsBothChildAxes()
        {
            TestPlan
                .For(x => new Shrink
                {
                    Child = x.CreateChild(),
                    Horizontal = true,
                    Vertical = true
                })
                .MeasureElement(new Size(100, 80))
                .ExpectChildMeasure(expectedInput: new Size(100, 80), returns: SpacePlan.FullRender(20, 10))
                .ExpectCanvasTranslate(0, 0)
                .ExpectChildDraw(new Size(20, 10))
                .ExpectCanvasTranslate(0, 0)
                .CheckDrawResult();
        }

        [Test]
        public void ShrinkHorizontal_RightToLeftAlignsTheChildToTheRight()
        {
            TestPlan
                .For(x => new Shrink
                {
                    Child = x.CreateChild(),
                    Horizontal = true,
                    ContentDirection = ContentDirection.RightToLeft
                })
                .MeasureElement(new Size(100, 80))
                .ExpectChildMeasure(expectedInput: new Size(100, 80), returns: SpacePlan.FullRender(20, 10))
                .ExpectCanvasTranslate(80, 0)
                .ExpectChildDraw(new Size(20, 80))
                .ExpectCanvasTranslate(-80, 0)
                .CheckDrawResult();
        }

        [Test]
        public void ZIndex_ComposesOverlappingMarkersInDepthOrder()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(60, 60);
                page.PageColor("#ffffff");
                page.Content().Layers(layers =>
                {
                    layers.PrimaryLayer()
                        .ZIndex(2)
                        .Width(20).Height(20)
                        .Background("#ff0000");

                    layers.Layer()
                        .PaddingLeft(15).PaddingTop(15)
                        .Width(30).Height(30)
                        .Background("#00ff00");

                    layers.Layer()
                        .ZIndex(1)
                        .PaddingLeft(30).PaddingTop(30)
                        .Width(30).Height(30)
                        .Background("#0000ff");
                });
            }));

            using var page = SKBitmap.Decode(document.GenerateImages().Single());

            Assert.That(page.GetPixel(5, 5), Is.EqualTo(SKColor.Parse("#ff0000")),
                "The primary layer belongs to depth 0.");
            Assert.That(page.GetPixel(17, 17), Is.EqualTo(SKColor.Parse("#ff0000")),
                "Depth 2 must render above depth 0.");
            Assert.That(page.GetPixel(40, 40), Is.EqualTo(SKColor.Parse("#0000ff")),
                "Depth 2 must render above depths 0 and 1 where they overlap.");
            Assert.That(page.GetPixel(55, 55), Is.EqualTo(SKColor.Parse("#0000ff")));
        }

        [Test]
        public void ZIndex_EqualDepthsRetainDrawingOrder()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(40, 40);
                page.PageColor("#ffffff");
                page.Content().Layers(layers =>
                {
                    layers.PrimaryLayer().Background("#ff0000");
                    layers.Layer().ZIndex(1).Background("#00ff00");
                    layers.Layer().ZIndex(1).Background("#0000ff");
                });
            }));

            using var page = SKBitmap.Decode(document.GenerateImages().Single());

            Assert.That(page.GetPixel(20, 20), Is.EqualTo(SKColor.Parse("#0000ff")),
                "Equal depths render in composition order, so the later blue layer stays on top.");
        }

        [Test]
        public void ZIndex_RendersPdfSvgAndPreviewerOutputs()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(60, 60);
                page.PageColor("#ffffff");
                page.Content().Layers(layers =>
                {
                    layers.PrimaryLayer().Background("#ff0000");
                    layers.Layer().ZIndex(1).Width(20).Height(20).Background("#0000ff");
                });
            }));

            var pdf = document.GeneratePdf(out var pageCount);
            Assert.That(pageCount, Is.EqualTo(1));
            Assert.That(System.Text.Encoding.ASCII.GetString(pdf, 0, 5), Is.EqualTo("%PDF-"));

            var svg = document.GenerateSvg().Single();
            Assert.That(svg, Does.Contain("<svg"));

            var pictures = DocumentGenerator.GeneratePreviewerPictures(document).ToArray();
            try
            {
                using var page = new SKBitmap(60, 60);
                using (var canvas = new SKCanvas(page))
                    canvas.DrawPicture(pictures.Single().Picture);

                Assert.That(page.GetPixel(10, 10), Is.EqualTo(SKColor.Parse("#0000ff")));
            }
            finally
            {
                foreach (var picture in pictures)
                    picture.Picture.Dispose();
            }
        }

        [Test]
        public void ZIndex_NestedLayersComposeByDepth()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(60, 60);
                page.Margin(0);
                page.PageColor("#ffffff");

                page.Content().Layers(outer =>
                {
                    outer.PrimaryLayer().Background("#ff0000");
                    outer.Layer().ZIndex(1).Layers(inner =>
                    {
                        inner.PrimaryLayer().Background("#00ff00");
                        inner.Layer().ZIndex(3).Width(20).Height(20).Background("#0000ff");
                    });
                });
            }));

            using var page = SKBitmap.Decode(document.GenerateImages().Single());

            Assert.That(page.GetPixel(10, 10), Is.EqualTo(SKColor.Parse("#0000ff")),
                "The nested depth 3 layer renders above its enclosing depth 1 layer.");
            Assert.That(page.GetPixel(30, 30), Is.EqualTo(SKColor.Parse("#00ff00")),
                "The enclosing depth 1 layer remains above the primary depth 0 layer.");
        }

        [Test]
        public void InjectedElementAlignment_AccommodatesMixedHeights()
        {
            var span = new TextLineElement
            {
                Item = new TextBlockSpan { Text = "X" },
                Measurement = new TextMeasurementResult
                {
                    Width = 10,
                    Ascent = -20,
                    Descent = 5,
                    LineHeight = 1
                }
            };

            var small = CreateInjectedElement(TextInjectedElementAlignment.Top, 8);
            var large = CreateInjectedElement(TextInjectedElementAlignment.Bottom, 20);

            var line = TextLine.From(new[] { span, small, large }, 0, 100, true);

            Assert.That(small.InjectedElementTop, Is.EqualTo(-20));
            Assert.That(large.InjectedElementTop, Is.EqualTo(-40));
            Assert.That(line.Ascent, Is.EqualTo(-40));
            Assert.That(line.Descent, Is.EqualTo(5));
            Assert.That(line.LineHeight, Is.EqualTo(45),
                "The line grows enough to contain the tallest aligned element.");
        }

        [Test]
        public void InjectedElementAlignment_ComputesBaselineAndFontOffsets()
        {
            var text = CreateTextLine(TextInjectedElementAlignment.Top, 8);
            var middle = CreateTextLine(TextInjectedElementAlignment.Middle, 8);
            var bottom = CreateTextLine(TextInjectedElementAlignment.Bottom, 8);
            var baseline = CreateTextLine(TextInjectedElementAlignment.AboveBaseline, 8);

            Assert.That(GetInjectedElementTop(text), Is.EqualTo(-20));
            Assert.That(GetInjectedElementTop(middle), Is.EqualTo(-11.5f));
            Assert.That(GetInjectedElementTop(bottom), Is.EqualTo(-28));
            Assert.That(GetInjectedElementTop(baseline), Is.EqualTo(-8));
        }

        [Test]
        public void InjectedElementAlignment_RendersAllSupportedPositions()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(220, 60);
                page.Margin(5);
                page.PageColor("#ffffff");
                page.DefaultTextStyle(x => x.FontSize(24));

                page.Content().Text(text =>
                {
                    text.Span("X");
                    text.Element(TextInjectedElementAlignment.Top).Width(8).Height(8).Background("#ff0000");
                    text.Element(TextInjectedElementAlignment.Middle).Width(8).Height(8).Background("#00ff00");
                    text.Element(TextInjectedElementAlignment.Bottom).Width(8).Height(8).Background("#0000ff");
                    text.Element(TextInjectedElementAlignment.AboveBaseline).Width(8).Height(8).Background("#ffff00");
                });
            }));

            using var page = SKBitmap.Decode(document.GenerateImages().Single());

            var text = FindDarkBounds(page);
            var top = FindColorBounds(page, "#ff0000");
            var middle = FindColorBounds(page, "#00ff00");
            var bottom = FindColorBounds(page, "#0000ff");
            var baseline = FindColorBounds(page, "#ffff00");

            Assert.That(text, Is.Not.Null);
            Assert.That(top, Is.Not.Null);
            Assert.That(middle, Is.Not.Null);
            Assert.That(bottom, Is.Not.Null);
            Assert.That(baseline, Is.Not.Null);

            Assert.That(bottom.Value.Top, Is.LessThan(top.Value.Top),
                "Bottom-aligned content extends upward from the font top.");
            Assert.That(top.Value.Top, Is.LessThan(middle.Value.Top),
                "Top-aligned content starts at the font top.");
            Assert.That(middle.Value.Top, Is.LessThan(baseline.Value.Top),
                "Middle-aligned content sits above the baseline-aligned marker.");
            Assert.That(bottom.Value.Bottom + 1, Is.EqualTo(top.Value.Top).Within(1),
                "The bottom-aligned marker's bottom edge meets the top-aligned marker's top edge at the font top.");
            Assert.That(baseline.Value.Bottom, Is.EqualTo(text.Value.Bottom).Within(2),
                "The baseline-aligned marker ends on the text baseline.");
            Assert.That(top.Value.Left, Is.GreaterThan(text.Value.Right),
                "The text keeps its logical position before the injected markers.");
            Assert.That(top.Value.Left, Is.LessThan(middle.Value.Left));
            Assert.That(middle.Value.Left, Is.LessThan(bottom.Value.Left));
            Assert.That(bottom.Value.Left, Is.LessThan(baseline.Value.Left));
        }

        private static TextLine CreateTextLine(TextInjectedElementAlignment alignment, float elementHeight)
        {
            var span = new TextLineElement
            {
                Item = new TextBlockSpan { Text = "X" },
                Measurement = new TextMeasurementResult
                {
                    Width = 10,
                    Ascent = -20,
                    Descent = 5,
                    LineHeight = 1
                }
            };

            var element = CreateInjectedElement(alignment, elementHeight);

            var line = TextLine.From(new[] { span, element }, 0, 100, true);
            return line;
        }

        private static TextLineElement CreateInjectedElement(
            TextInjectedElementAlignment alignment,
            float height)
        {
            return new TextLineElement
            {
                Item = new TextBlockElement { Alignment = alignment },
                Measurement = new TextMeasurementResult
                {
                    Width = 8,
                    Ascent = -height,
                    Descent = 0,
                    LineHeight = 1,
                    InjectedElementAlignment = alignment
                }
            };
        }

        private static float GetInjectedElementTop(TextLine line)
        {
            return line.Elements
                .Single(x => x.Measurement.InjectedElementAlignment.HasValue)
                .InjectedElementTop;
        }

        private static Bounds? FindDarkBounds(SKBitmap bitmap)
        {
            return FindBounds(bitmap, pixel => pixel.Red < 60 && pixel.Green < 60 && pixel.Blue < 60);
        }

        private static Bounds? FindColorBounds(SKBitmap bitmap, string color)
        {
            var target = SKColor.Parse(color);
            return FindBounds(bitmap,
                pixel => pixel.Red == target.Red && pixel.Green == target.Green && pixel.Blue == target.Blue);
        }

        private static Bounds? FindBounds(SKBitmap bitmap, Func<SKColor, bool> predicate)
        {
            var left = int.MaxValue;
            var top = int.MaxValue;
            var right = int.MinValue;
            var bottom = int.MinValue;

            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (!predicate(bitmap.GetPixel(x, y)))
                    continue;

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }

            return left == int.MaxValue ? null : new Bounds(left, top, right, bottom);
        }

        private readonly struct Bounds
        {
            public int Left { get; }
            public int Top { get; }
            public int Right { get; }
            public int Bottom { get; }

            public Bounds(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }
        }
    }
}
