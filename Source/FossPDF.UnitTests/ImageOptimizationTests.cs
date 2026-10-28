using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FossPDF.Drawing;
using FossPDF.Elements;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.UnitTests.TestEngine;
using NUnit.Framework;
using SkiaSharp;
using ElementImage = FossPDF.Elements.Image;
using ElementDynamicImage = FossPDF.Elements.DynamicImage;

namespace FossPDF.UnitTests
{
    [TestFixture]
    [NonParallelizable]
    public class ImageOptimizationTests
    {
        [Test]
        public void PerImageDpi_ResamplesToTheDisplayResolution()
        {
            using var source = CreateSourceImage(800, 400);

            Assert.That(DrawElement(source, 80, 40, 72, null).Width, Is.EqualTo(80));
            Assert.That(DrawElement(source, 80, 40, 72, null).Height, Is.EqualTo(40));
            Assert.That(DrawElement(source, 80, 40, 144, null).Width, Is.EqualTo(160));
            Assert.That(DrawElement(source, 80, 40, 144, null).Height, Is.EqualTo(80));
        }

        [Test]
        public void PerImageDpi_NeverUpscalesTheSource()
        {
            using var source = CreateSourceImage(100, 50);

            var result = DrawElement(source, 200, 100, 144, null);

            Assert.That(result.Width, Is.EqualTo(100));
            Assert.That(result.Height, Is.EqualTo(50));
        }

        [Test]
        public void UseOriginalImage_ReturnsTheSourceResolution()
        {
            using var source = CreateSourceImage(800, 400);

            var result = DrawElement(source, 80, 40, 72, 40, useOriginalImage: true);

            Assert.That(result.Width, Is.EqualTo(800));
            Assert.That(result.Height, Is.EqualTo(400));
        }

        [Test]
        public void CompressionQuality_ChangesLossyOutput()
        {
            using var source = CreateSourceImage(800, 400);

            var low = DrawElement(source, 80, 40, 72, 40);
            var high = DrawElement(source, 80, 40, 72, 90);

            Assert.That(low.Width, Is.EqualTo(high.Width));
            Assert.That(low.Height, Is.EqualTo(high.Height));
            Assert.That(low.Png.SequenceEqual(high.Png), Is.False,
                "Different JPEG qualities must produce different decoded pixels.");
        }

        [Test]
        public void PerImageProcessing_DoesNotMutateTheSharedSourceImage()
        {
            using var source = CreateSourceImage(800, 400);
            var originalHandle = source.Handle;

            var result = DrawElement(source, 80, 40, 72, 40);

            Assert.That(source.Width, Is.EqualTo(800));
            Assert.That(source.Height, Is.EqualTo(400));
            Assert.That(source.Handle, Is.EqualTo(originalHandle));
            Assert.That(result.Width, Is.EqualTo(80));
        }

        [Test]
        public void CompressionQuality_PreservesTransparency()
        {
            using var source = CreateTransparentSourceImage();
            var result = DrawElement(source, 100, 50, 72, 90);

            using var decoded = SKBitmap.Decode(result.Png);
            Assert.That(decoded.GetPixel(10, 25).Alpha, Is.EqualTo(0),
                "A transparent pixel must remain transparent after lossy encoding.");
            Assert.That(decoded.GetPixel(90, 25).Alpha, Is.EqualTo(255));
        }

        [Test]
        public void PreviewerPictures_KeepProcessedImagesUsable()
        {
            using var bitmap = CreateSourceBitmap(800, 400);
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            using var shared = Infrastructure.Image.FromBinaryData(encoded.ToArray());

            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(80, 40);
                page.Content().Image(shared).WithRasterDpi(72).WithCompressionQuality(40);
            }));

            var pictures = DocumentGenerator.GeneratePreviewerPictures(document);
            try
            {
                using var target = new SKBitmap(80, 40);
                using (var canvas = new SKCanvas(target))
                    canvas.DrawPicture(pictures.Single().Picture);

                Assert.That(target.GetPixel(10, 20).Alpha, Is.EqualTo(255),
                    "The preview picture must retain the processed image after drawing completes.");
            }
            finally
            {
                foreach (var picture in pictures)
                    picture.Picture.Dispose();
            }
        }

        [Test]
        public void ImageDescriptors_ValidateSettings()
        {
            var image = new ElementImage();
            var descriptor = new ImageDescriptor(image);

            Assert.Throws<ArgumentOutOfRangeException>(() => descriptor.WithRasterDpi(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => descriptor.WithRasterDpi(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => descriptor.WithCompressionQuality(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => descriptor.WithCompressionQuality(101));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                descriptor.WithCompressionQuality((ImageCompressionQuality) 99));

            var dynamicImage = new ElementDynamicImage();
            var dynamicDescriptor = new DynamicImageDescriptor(dynamicImage);
            Assert.Throws<ArgumentOutOfRangeException>(() => dynamicDescriptor.WithRasterDpi(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => dynamicDescriptor.WithCompressionQuality(101));
        }

        [Test]
        public void ExplicitDocumentSettings_AreUsedAsPerImageDefaults()
        {
            using var bitmap = CreateSourceBitmap(800, 400);
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(120, 60);
                page.PageColor("#ffffff");
                page.Content().Image(encoded.ToArray());
            })).WithSettings(new DocumentSettings
            {
                ImageRasterDpi = 144,
                ImageCompressionQuality = 90
            });

            var canvas = new RecordingCanvas();
            DocumentGenerator.RenderDocument(canvas, document, GenerationConfiguration.Resolve(document));

            Assert.That(canvas.Width, Is.EqualTo(240));
            Assert.That(canvas.Height, Is.EqualTo(120));
        }

        [Test]
        public void PerImageValuesOverrideDocumentDefaults()
        {
            var png = CreatePng(800, 400);

            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(120, 60);
                page.PageColor("#ffffff");
                page.Content().Width(80).Image(png).WithRasterDpi(72);
            })).WithSettings(new DocumentSettings
            {
                ImageRasterDpi = 144,
                ImageCompressionQuality = 90
            });

            var canvas = new RecordingCanvas();
            DocumentGenerator.RenderDocument(canvas, document, GenerationConfiguration.Resolve(document));

            Assert.That(canvas.Width, Is.EqualTo(80));
            Assert.That(canvas.Height, Is.EqualTo(40));
        }

        [Test]
        public void ExplicitDocumentSettings_ApplyToDynamicImages()
        {
            var png = CreatePng(800, 400);

            var document = Document.Create(container => container.Page(page =>
            {
                page.Size(120, 40);
                page.PageColor("#ffffff");
                page.Content().Width(80).Image(_ => png);
            })).WithSettings(new DocumentSettings
            {
                ImageRasterDpi = 144,
                ImageCompressionQuality = 90
            });

            var canvas = new RecordingCanvas();
            DocumentGenerator.RenderDocument(canvas, document, GenerationConfiguration.Resolve(document));

            Assert.That(canvas.Width, Is.EqualTo(160));
            Assert.That(canvas.Height, Is.EqualTo(80));
        }

        private static byte[] CreatePng(int width, int height)
        {
            using var bitmap = CreateSourceBitmap(width, height);
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            return encoded.ToArray();
        }

        private static DrawResult DrawElement(
            SKImage source,
            int displayWidth,
            int displayHeight,
            int? dpi,
            int? quality,
            bool useOriginalImage = false)
        {
            var element = new ElementImage
            {
                InternalImage = source,
                TargetDpi = dpi,
                CompressionQuality = quality,
                UseOriginalImage = useOriginalImage
            };

            var width = 0;
            var height = 0;
            byte[] png = null;

            var canvas = new MockCanvas
            {
                DrawImageFunc = (image, _, _) =>
                {
                    width = image.Width;
                    height = image.Height;
                    using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                    png = encoded.ToArray();
                }
            };

            element.Canvas = canvas;
            element.Draw(new Size(displayWidth, displayHeight));

            return new DrawResult(width, height, png ?? Array.Empty<byte>());
        }

        private static SKImage CreateSourceImage(int width, int height)
        {
            using var bitmap = CreateSourceBitmap(width, height);
            return SKImage.FromBitmap(bitmap);
        }

        private static SKImage CreateTransparentSourceImage()
        {
            using var bitmap = new SKBitmap(100, 50);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = SKColors.Red, IsAntialias = false };
            canvas.DrawRect(50, 0, 50, 50, paint);
            return SKImage.FromBitmap(bitmap);
        }

        private static SKBitmap CreateSourceBitmap(int width, int height)
        {
            var bitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint { IsAntialias = false };

            for (var y = 0; y < height; y++)
            {
                paint.Color = new SKColor(
                    (byte) (y * 255 / height),
                    (byte) (255 - y * 255 / height),
                    (byte) ((y * 37) % 256));

                canvas.DrawRect(0, y, width, 1, paint);
            }

            return bitmap;
        }

        private readonly struct DrawResult
        {
            public int Width { get; }
            public int Height { get; }
            public byte[] Png { get; }

            public DrawResult(int width, int height, byte[] png)
            {
                Width = width;
                Height = height;
                Png = png;
            }
        }

        private sealed class RecordingCanvas : ICanvas, IRenderingCanvas
        {
            public int Width { get; private set; }
            public int Height { get; private set; }

            public void BeginDocument() { }
            public void EndDocument() { }
            public void BeginPage(Size size) { }
            public void EndPage() { }

            public Matrix4x4 Transform => Matrix4x4.Identity;

            public void Translate(Position vector) { }
            public void DrawRectangle(Position vector, Size size, string color) { }
            public void DrawRectangle(Position vector, Size size, RectangleStyle style) { }
            public void DrawLine(Position start, Position end, LineStyle style) { }
            public void DrawShadow(Position vector, Size size, BorderRadius cornerRadius, BoxShadowStyle shadow) { }
            public void SaveState() { }
            public void RestoreState() { }
            public void ClipRectangle(Position vector, Size size, BorderRadius cornerRadius) { }
            public void DrawText(SKTextBlob skTextBlob, Position position, TextStyle style,
                DocumentSpecificFontManager fontManager) { }
            public void DrawImage(SKImage image, Position position, Size size)
            {
                Width = image.Width;
                Height = image.Height;
            }

            public void DrawHyperlink(string url, Size size) { }
            public void DrawSectionLink(string sectionName, Size size) { }
            public void DrawSection(string sectionName) { }
            public void Rotate(float angle) { }
            public void Scale(float scaleX, float scaleY) { }
        }
    }
}
