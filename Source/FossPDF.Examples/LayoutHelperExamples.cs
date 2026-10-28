using System.IO;
using System.Linq;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.Examples
{
    public class LayoutHelperExamples
    {
        [Test]
        public void ShrinkAxesIndependently()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(300, 80);
                page.Margin(0);
                page.PageColor("#ffffff");

                page.Content().Row(row =>
                {
                    row.ConstantItem(100).Height(80)
                        .ShrinkHorizontal()
                        .Background("#ff0000")
                        .Width(20).Height(10);

                    row.ConstantItem(100).Height(80)
                        .ShrinkVertical()
                        .Background("#00ff00")
                        .Width(20).Height(10);

                    row.ConstantItem(100).Height(80)
                        .Shrink()
                        .Background("#0000ff")
                        .Width(20).Height(10);
                });
            }));

            var pdf = document.GeneratePdf();
            using var page = SKBitmap.Decode(document.GenerateImages().Single());

            Assert.That(page.GetPixel(10, 40), Is.EqualTo(SKColor.Parse("#ff0000")));
            Assert.That(page.GetPixel(150, 5), Is.EqualTo(SKColor.Parse("#00ff00")));
            Assert.That(page.GetPixel(210, 5), Is.EqualTo(SKColor.Parse("#0000ff")));
            Assert.That(page.GetPixel(210, 40), Is.EqualTo(SKColors.White));

            Save("shrink.pdf", pdf);
        }

        [Test]
        public void StackContentByZIndex()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(60, 60);
                page.Margin(0);
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

            var pdf = document.GeneratePdf();
            using var page = SKBitmap.Decode(document.GenerateImages().Single());

            Assert.That(page.GetPixel(17, 17), Is.EqualTo(SKColor.Parse("#ff0000")));
            Assert.That(page.GetPixel(40, 40), Is.EqualTo(SKColor.Parse("#0000ff")));

            Save("z-index.pdf", pdf);
        }

        [Test]
        public void AlignContentInjectedIntoText()
        {
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(220, 60);
                page.Margin(5);
                page.PageColor("#ffffff");
                page.DefaultTextStyle(style => style.FontSize(24));

                page.Content().Text(text =>
                {
                    text.Span("X");
                    text.Element(TextInjectedElementAlignment.Top).Width(8).Height(8).Background("#ff0000");
                    text.Element(TextInjectedElementAlignment.Middle).Width(8).Height(8).Background("#00ff00");
                    text.Element(TextInjectedElementAlignment.Bottom).Width(8).Height(8).Background("#0000ff");
                    text.Element(TextInjectedElementAlignment.AboveBaseline).Width(8).Height(8).Background("#ffff00");
                });
            }));

            var pdf = document.GeneratePdf();
            using var page = SKBitmap.Decode(document.GenerateImages().Single());

            Assert.That(CountColor(page, "#ff0000"), Is.GreaterThan(0));
            Assert.That(CountColor(page, "#00ff00"), Is.GreaterThan(0));
            Assert.That(CountColor(page, "#0000ff"), Is.GreaterThan(0));
            Assert.That(CountColor(page, "#ffff00"), Is.GreaterThan(0));

            Save("inline-alignment.pdf", pdf);
        }

        private static int CountColor(SKBitmap bitmap, string color)
        {
            var target = SKColor.Parse(color);
            var count = 0;

            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red == target.Red && pixel.Green == target.Green && pixel.Blue == target.Blue)
                    count++;
            }

            return count;
        }

        private static void Save(string fileName, byte[] pdf)
        {
            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "layout-helper-example");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, fileName), pdf);
        }
    }
}
