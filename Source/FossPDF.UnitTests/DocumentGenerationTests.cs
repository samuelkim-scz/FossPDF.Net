using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Helpers;
using NUnit.Framework;
using SkiaSharp;
using SharedImage = FossPDF.Infrastructure.Image;

namespace FossPDF.UnitTests
{
    [NonParallelizable]
    public class DocumentGenerationTests
    {
        [Test]
        public void GeneratePdf_RendersTextAndReportsPageCount()
        {
            var document = Document.Create(container => container.Page(page =>
                page.Content().Text("FossPDF document generation")));

            var bytes = document.GeneratePdf(out var totalPages);

            Assert.That(totalPages, Is.EqualTo(1));
            Assert.That(Encoding.ASCII.GetString(bytes, 0, 5), Is.EqualTo("%PDF-"));
        }

        [Test]
        public void GenerateImages_RendersEveryPage()
        {
            var images = CreateTwoPageDocument().GenerateImages().ToArray();

            Assert.That(images.Length, Is.EqualTo(2));
            foreach (var bytes in images)
            {
                using var image = SKImage.FromEncodedData(bytes);
                Assert.That(image.Width, Is.GreaterThan(0));
                Assert.That(image.Height, Is.GreaterThan(0));
            }
        }

        [Test]
        public void GenerateSvg_RendersEveryPage()
        {
            var pages = CreateTwoPageDocument().GenerateSvg().ToArray();

            Assert.That(pages.Length, Is.EqualTo(2));
            foreach (var page in pages)
                Assert.That(XDocument.Parse(page).Root.Name.LocalName, Is.EqualTo("svg"));
        }

        [Test]
        public void GeneratePreviewerPictures_TransfersUsablePicturesToTheCaller()
        {
            var pictures = DocumentGenerator.GeneratePreviewerPictures(CreateTwoPageDocument());
            try
            {
                Assert.That(pictures.Count, Is.EqualTo(2));
                foreach (var picture in pictures)
                {
                    Assert.That(picture.Picture.Handle, Is.Not.EqualTo(IntPtr.Zero));
                    using var data = picture.Picture.Serialize();
                    Assert.That(data.Size, Is.GreaterThan(0));
                }
            }
            finally
            {
                foreach (var picture in pictures)
                    picture.Picture.Dispose();
            }
        }

        [Test]
        public void GeneratePdf_KeepsSharedImagesAliveBetweenDocuments()
        {
            using var image = SharedImage.FromBinaryData(Placeholders.Image(20, 20));
            RenderSharedImage(image);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.That(image.SkImage.Handle, Is.Not.EqualTo(IntPtr.Zero));
            Assert.DoesNotThrow(() => RenderSharedImage(image));
        }

        [Test]
        public void GeneratePdf_ReleasesDocumentFontsWhenSubsettingFails()
        {
            DocumentSpecificFontManager fontManager = null;
            try
            {
                FontManager.RegisterSubsetCallback((manager, _) =>
                {
                    fontManager = manager;
                    throw new InvalidOperationException("Subsetting failed.");
                });

                Assert.Throws<InvalidOperationException>(() => CreateTwoPageDocument().GeneratePdf());

                Assert.That(fontManager, Is.Not.Null);
                Assert.That(fontManager.SafeToDisposeAtSubsetTime.IsEmpty, Is.True);
                Assert.That(fontManager.Fonts.IsEmpty, Is.True);
            }
            finally
            {
                FontManager.RegisterSubsetCallback(null);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RenderSharedImage(SharedImage image)
        {
            Document.Create(container => container.Page(page =>
                page.Content().Width(20).Height(20).Image(image))).GeneratePdf();
        }

        private static Document CreateTwoPageDocument()
        {
            return Document.Create(container => container.Page(page => page.Content().Column(column =>
            {
                column.Item().Text("First page");
                column.Item().PageBreak();
                column.Item().Text("Second page");
            })));
        }
    }
}
