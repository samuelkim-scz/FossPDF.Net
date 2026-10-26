using System.IO;
using System.Linq;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.Examples
{
    public class DocumentSettingsExamples
    {
        [Test]
        public void ExportPageImagesInAllFormats()
        {
            var document = Document.Create(container =>
            {
                foreach (var color in new[] { "#ff0000", "#0000ff" })
                    container.Page(page =>
                    {
                        page.Size(120, 80);
                        page.Margin(10);
                        page.Content().Height(20).Background(color);
                    });
            });
            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "settings-example", "image-exports");
            Directory.CreateDirectory(directory);
            foreach (var format in new[] { ImageOutputFormat.Png, ImageOutputFormat.Jpeg, ImageOutputFormat.Webp })
            {
                var options = new ImageGenerationSettings { Format = format, RasterDpi = 144, CompressionQuality = 90 };
                var extension = format.ToString().ToLowerInvariant();
                document.GenerateImages(index => Path.Combine(directory, $"page-{index + 1}.{extension}"), options);

                using var first = new MemoryStream();
                using var second = new MemoryStream();
                document.GenerateImagesToStreams(index => index == 0 ? first : second, options);
                Assert.That(first.CanWrite && second.CanWrite, Is.True);
                using var image = SKBitmap.Decode(first.ToArray());
                Assert.That(image.Width, Is.EqualTo(240));
                Assert.That(image.Height, Is.EqualTo(160));
            }
        }

        [Test]
        public void PerDocumentSettings()
        {
            var metadata = new DocumentMetadata { Title = "Settings example", RasterDpi = 72 };
            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(120, 80);
                page.Margin(10);
                page.PageColor("#ffffff");
                page.Content().Text("Per-document settings").FontSize(10);
            }))
            .WithMetadata(metadata)
            .WithSettings(new DocumentSettings
            {
                ImageRasterDpi = 144,
                ContentDirection = ContentDirection.LeftToRight,
                DocumentLayoutExceptionThreshold = 1,
                EnableCaching = true
            });

            // Metadata remains the caller's configuration; generation uses a private snapshot.
            using var output = new MemoryStream();
            document.GeneratePdf(output);
            Assert.That(output.CanWrite, Is.True);
            Assert.That(metadata.RasterDpi, Is.EqualTo(72));

            // A thumbnail override is local to this export.
            var thumbnail = document.GenerateImages(new ImageGenerationSettings
            {
                RasterDpi = 72,
                Format = ImageOutputFormat.Png
            }).Single();
            var fullSize = document.GenerateImages().Single();

            using var decodedThumbnail = SKBitmap.Decode(thumbnail);
            using var decodedFullSize = SKBitmap.Decode(fullSize);
            Assert.That(decodedThumbnail.Width, Is.EqualTo(120));
            Assert.That(decodedFullSize.Width, Is.EqualTo(240));

            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "settings-example");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "document.pdf"), output.ToArray());
            File.WriteAllBytes(Path.Combine(directory, "thumbnail.png"), thumbnail);
            File.WriteAllBytes(Path.Combine(directory, "full-size.png"), fullSize);
        }
    }
}
