using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FossPDF.Drawing;
using FossPDF.Elements;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.UnitTests
{
    [TestFixture]
    [NonParallelizable]
    public class ContentPositionTests
    {
        private const string Id = "invoice-lines";
        private const int ItemCount = 12;
        private const int ItemHeight = 20;
        private const int PageWidth = 120;
        private const int PageHeight = 100;
        private const int Margin = 10;

        [Test]
        public void CapturePositions_ReportsEveryOccurrenceWithPageAndGeometry()
        {
            var document = CreateCaptureDocument();

            document.GeneratePdf(out var pageCount);
            var positions = GetPositions(document);

            Assert.That(pageCount, Is.EqualTo(3));
            Assert.That(positions, Has.Length.EqualTo(ItemCount));

            for (var index = 0; index < ItemCount; index++)
            {
                var expectedPage = index / 4 + 1;
                var expectedY = Margin + index % 4 * ItemHeight;
                var position = positions[index];

                Assert.That(position.PageNumber, Is.EqualTo(expectedPage), $"page of item {index}");
                Assert.That(position.X, Is.EqualTo(Margin).Within(0.25), $"X of item {index}");
                Assert.That(position.Y, Is.EqualTo(expectedY).Within(0.25), $"Y of item {index}");
                Assert.That(position.Width, Is.EqualTo(PageWidth - 2 * Margin).Within(0.25), $"width of item {index}");
                Assert.That(position.Height, Is.EqualTo(ItemHeight).Within(0.25), $"height of item {index}");
            }
        }

        [Test]
        public void CapturePositions_AreStableAcrossRepeatedGeneration()
        {
            var document = CreateCaptureDocument();

            document.GeneratePdf();
            var first = GetPositions(document)
                .Select(x => (x.PageNumber, x.X, x.Y, x.Width, x.Height))
                .ToArray();

            document.GeneratePdf();
            var second = GetPositions(document)
                .Select(x => (x.PageNumber, x.X, x.Y, x.Width, x.Height))
                .ToArray();

            Assert.That(second.Length, Is.EqualTo(ItemCount));
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void CapturePositions_AreAvailableFromDynamicContext()
        {
            var probe = new CaptureProbe();

            var document = Document.Create(container => container.Page(page =>
            {
                ConfigurePage(page);
                page.Content().Column(column =>
                {
                    AddCapturedItems(column);
                    column.Item().Dynamic(probe);
                });
            }));

            document.GeneratePdf();

            Assert.That(probe.Captured, Has.Count.EqualTo(ItemCount));
            Assert.That(probe.Captured.Select(x => x.PageNumber), Is.EquivalentTo(new[] { 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3 }));
        }

        [Test]
        public void CapturePositions_RejectEmptyIdentifier()
        {
            var document = Document.Create(container => container.Page(page =>
                page.Content().CaptureContentPosition(" ")));

            Assert.Throws<ArgumentException>(() => document.GeneratePdf());
        }

        [Test]
        public void FreeCanvas_TracksTranslationScaleAndRotation()
        {
            var canvas = new FreeCanvas();
            canvas.BeginPage(new Size(PageWidth, PageHeight));
            canvas.Translate(new Position(Margin, Margin));
            canvas.Scale(2, 3);
            canvas.Rotate(90);

            var transform = canvas.Transform;
            var position = Vector2.Transform(Vector2.Zero, transform);

            using var bitmap = new SKBitmap(10, 10);
            using var skiaCanvas = new SKCanvas(bitmap);
            skiaCanvas.Translate(Margin, Margin);
            skiaCanvas.Scale(2, 3);
            skiaCanvas.RotateDegrees(90);
            var skiaPosition = skiaCanvas.TotalMatrix.MapPoint(0, 0);

            Assert.That(position.X, Is.EqualTo(skiaPosition.X).Within(0.001));
            Assert.That(position.Y, Is.EqualTo(skiaPosition.Y).Within(0.001));
            Assert.That(
                new[]
                {
                    new Vector2(transform.M11, transform.M12).Length(),
                    new Vector2(transform.M21, transform.M22).Length()
                },
                Is.EquivalentTo(new[] { 2f, 3f }));
        }

        private static Document CreateCaptureDocument()
        {
            return Document.Create(container => container.Page(page =>
            {
                ConfigurePage(page);
                page.Content().Column(AddCapturedItems);
            }));
        }

        private static void ConfigurePage(PageDescriptor page)
        {
            page.Size(PageWidth, PageHeight);
            page.Margin(Margin);
            page.PageColor("#ffffff");
        }

        private static void AddCapturedItems(ColumnDescriptor column)
        {
            for (var index = 0; index < ItemCount; index++)
            {
                column.Item()
                    .CaptureContentPosition(Id)
                    .Height(ItemHeight)
                    .Background(index % 2 == 0 ? "#eeeeee" : "#cccccc");
            }
        }

        private static PageElementLocation[] GetPositions(Document document)
        {
            return document.ContentPositions
                .Where(x => x.Id == Id)
                .OrderBy(x => x.PageNumber)
                .ThenBy(x => x.Y)
                .ToArray();
        }

        private sealed class CaptureProbe : IDynamicComponent<CaptureProbe.ProbeState>
        {
            public struct ProbeState
            {
            }

            public ProbeState State { get; set; }
            public List<PageElementLocation> Captured { get; } = new();

            public DynamicComponentComposeResult Compose(DynamicContext context)
            {
                var positions = context.GetContentCapturedPositions(Id);

                if (positions.Count > 0)
                {
                    Captured.Clear();
                    Captured.AddRange(positions);
                }

                return new DynamicComponentComposeResult
                {
                    Content = context.CreateElement(_ => { }),
                    HasMoreContent = false
                };
            }
        }
    }
}
