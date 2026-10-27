using System.IO;
using System.Text;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.Examples
{
    public class ImageOptimizationExamples
    {
        [Test]
        public void PerImageDpiAndQuality()
        {
            using var shared = Infrastructure.Image.FromBinaryData(CreateQuadrantJpeg(800, 400));

            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(190, 60);
                page.Margin(10);
                page.PageColor("#ffffff");

                page.Content().Row(row =>
                {
                    row.ConstantItem(80)
                        .Image(shared)
                        .WithRasterDpi(72)
                        .WithCompressionQuality(40);

                    row.ConstantItem(10);

                    row.ConstantItem(80)
                        .Image(shared)
                        .WithRasterDpi(144)
                        .WithCompressionQuality(90);
                });
            }));

            var pdf = document.GeneratePdf();
            var text = Encoding.Latin1.GetString(pdf);

            Assert.That(text, Does.Contain("/Width 80"));
            Assert.That(text, Does.Contain("/Width 160"));
            Assert.That(text, Does.Contain("/DCTDecode"));
            Assert.That(shared.SkImage.Width, Is.EqualTo(800));

            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "image-optimization-example");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "per-image.pdf"), pdf);
        }

        [Test]
        public void DocumentDefaultsApplyToImages()
        {
            var document = Document.Create(container => container.Page(page =>
                {
                    page.Size(200, 100);
                    page.Margin(10);
                    page.PageColor("#ffffff");
                    page.Content().Width(80).Image(CreateQuadrantJpeg(800, 400));
                }))
                .WithSettings(new DocumentSettings
                {
                    ImageRasterDpi = 96,
                    ImageCompressionQuality = 75
                });

            var pdf = document.GeneratePdf();
            var text = Encoding.Latin1.GetString(pdf);

            // 80 points * 96 DPI / 72 = 107 pixels wide, 40 * 96 / 72 = 54 pixels tall.
            Assert.That(text, Does.Contain("/Width 107"));
            Assert.That(text, Does.Contain("/Height 54"));
            Assert.That(text, Does.Contain("/DCTDecode"));

            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "image-optimization-example");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "document-defaults.pdf"), pdf);
        }

        private static byte[] CreateQuadrantJpeg(int width, int height)
        {
            using var bitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint { IsAntialias = false };

            void Fill(int x, int y, int w, int h, string color)
            {
                paint.Color = SKColor.Parse(color);
                canvas.DrawRect(x, y, w, h, paint);
            }

            Fill(0, 0, width / 2, height / 2, "#ff0000");
            Fill(width / 2, 0, width - width / 2, height / 2, "#00ff00");
            Fill(0, height / 2, width / 2, height - height / 2, "#0000ff");
            Fill(width / 2, height / 2, width - width / 2, height - height / 2, "#ffff00");

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        }
    }
}
