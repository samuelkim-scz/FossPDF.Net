using System;
using System.IO;
using System.Linq;
using System.Text;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.Svg;
using NUnit.Framework;

namespace FossPDF.Examples
{
    /// <summary>
    /// FP-201: SVG input, preloading and reuse. SVG documents are parsed once into an
    /// <see cref="SvgImage"/> and drawn through the fluent layout. Vector replay is the default;
    /// the raster fallback is an explicit opt-in for content the vector path cannot preserve.
    /// </summary>
    public class SvgImageExamples
    {
        private const int PageWidth = 300;
        private const int PageHeight = 360;
        private const int PageMargin = 20;

        [Test]
        public void PreloadedSvgImageIsReusedAcrossThePage()
        {
            var iconPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "pdf-icon.svg");

            using var icon = SvgImage.FromFile(iconPath);

            var document = CreateDocument(page => page.Content().Column(column =>
            {
                column.Spacing(10);

                column.Item().Text("One parsed SVG drawn three times")
                    .SemiBold().FontSize(12);

                column.Item().Row(row =>
                {
                    row.Spacing(10);
                    row.ConstantItem(60).Height(80).Svg(icon);
                    row.ConstantItem(90).Height(80).Svg(icon, ImageScaling.FitHeight);
                    row.RelativeItem().Height(80).Svg(icon, ImageScaling.FitArea);
                });

                column.Item().Height(140).Svg(icon, ImageScaling.FitArea);

                column.Item().Text(
                        "The same SvgImage instance serves every placement, so the document " +
                        "is parsed once and the picture is replayed on demand.")
                    .FontSize(9);
            }));

            var artifacts = CreateArtifactDirectory();
            var pdf = document.GeneratePdf();
            File.WriteAllBytes(Path.Combine(artifacts, "svg-reuse.pdf"), pdf);

            var pages = document.GenerateImages(new ImageGenerationSettings { RasterDpi = 96 });
            Assert.That(pages, Has.Count.EqualTo(1));
            File.WriteAllBytes(Path.Combine(artifacts, "svg-reuse.png"), pages.Single());

            Assert.That(Encoding.ASCII.GetString(pdf, 0, 5), Is.EqualTo("%PDF-"));
            Assert.That(Encoding.Latin1.GetString(pdf), Does.Not.Contain("/Subtype /Image"),
                "Vector replay keeps the SVG as paths instead of an embedded image.");
        }

        [Test]
        public void InlineSvgSupportsVectorOutputAndExplicitRasterFallback()
        {
            const string badge =
                "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 120 60'>" +
                "<rect width='120' height='60' rx='10' fill='#1f6feb'/>" +
                "<circle cx='24' cy='30' r='12' fill='#ffd33d'/>" +
                "<text x='44' y='37' font-family='Lato' font-size='18' fill='#ffffff'>PDF</text>" +
                "</svg>";

            using var vector = SvgImage.FromText(badge);
            using var fromBytes = SvgImage.FromBytes(Encoding.UTF8.GetBytes(badge));

            var document = CreateDocument(page => page.Content().Column(column =>
            {
                column.Spacing(12);

                column.Item().Text("Vector replay (default)").SemiBold().FontSize(12);
                column.Item().Width(120).Svg(vector);

                column.Item().Text("Explicit raster fallback at 144 DPI").SemiBold().FontSize(12);
                column.Item().Width(120).SvgRasterized(vector, new SvgRasterizationOptions
                {
                    Dpi = 144,
                    Background = "#ffffff"
                });

                column.Item().Text("Bytes are parsed with the same pipeline").SemiBold().FontSize(12);
                column.Item().Width(120).Svg(fromBytes, ImageScaling.FitArea);
            }));

            var artifacts = CreateArtifactDirectory();
            var pdf = document.GeneratePdf();
            File.WriteAllBytes(Path.Combine(artifacts, "svg-vector-and-raster.pdf"), pdf);

            var pages = document.GenerateImages(new ImageGenerationSettings { RasterDpi = 96 });
            Assert.That(pages, Has.Count.EqualTo(1));
            File.WriteAllBytes(Path.Combine(artifacts, "svg-vector-and-raster.png"), pages.Single());

            Assert.That(Encoding.Latin1.GetString(pdf), Does.Contain("/Subtype /Image"),
                "The raster fallback embeds an image at the requested DPI.");
        }

        private static Document CreateDocument(Action<PageDescriptor> content)
        {
            return Document.Create(container => container.Page(page =>
            {
                page.Size(PageWidth, PageHeight);
                page.Margin(PageMargin);
                page.PageColor("#ffffff");
                page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(10));
                content(page);
            }));
        }

        private static string CreateArtifactDirectory()
        {
            var directory = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "svg-example");

            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
