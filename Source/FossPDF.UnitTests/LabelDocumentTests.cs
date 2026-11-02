using System;
using System.Collections.Generic;
using System.Linq;
using FossPDF.Drawing.Exceptions;
using FossPDF.Fluent;
using FossPDF.Helpers;
using FossPDF.Infrastructure;
using NUnit.Framework;
using SkiaSharp;

namespace FossPDF.UnitTests
{
    [TestFixture]
    public class LabelDocumentTests
    {
        [Test]
        public void Avery5160_SkipFive_PreservesGeometryAndStartsNextSheetAtFirstSlot()
        {
            var document = Capture(27, new LabelOptions { SkipLabels = 5 });
            document.GeneratePdf(out var pages);
            Assert.That(pages, Is.EqualTo(2));
            var positions = document.ContentPositions.ToArray();
            Assert.That(positions, Has.Length.EqualTo(27));
            for (var index = 0; index < positions.Length; index++)
            {
                var slot = index < 25 ? index + 5 : index - 25;
                var location = positions[index];
                Assert.That(location.Id, Is.EqualTo($"item-{index}"));
                Assert.That(location.PageNumber, Is.EqualTo(index < 25 ? 1 : 2));
                Assert.That(location.X, Is.EqualTo(13.5f + slot % 3 * 198).Within(0.001));
                Assert.That(location.Y, Is.EqualTo(36 + slot / 3 * 72).Within(0.001));
                Assert.That(location.Width, Is.EqualTo(189).Within(0.001));
                Assert.That(location.Height, Is.EqualTo(72).Within(0.001));
            }
        }

        [TestCase(0, 1, 1)]
        [TestCase(5, 2, 3)]
        [TestCase(29, 10, 3)]
        public void StartingRowAndColumn_MatchSkipCount(int skip, int row, int column)
        {
            var skipped = Capture(3, new LabelOptions { SkipLabels = skip });
            var positioned = Capture(3, new LabelOptions { StartRow = row, StartColumn = column });
            skipped.GeneratePdf();
            positioned.GeneratePdf();
            Assert.That(Geometry(positioned), Is.EqualTo(Geometry(skipped)));
        }

        [TestCase(0, 0, 0)]
        [TestCase(1, 0, 1)]
        [TestCase(30, 0, 1)]
        [TestCase(31, 0, 2)]
        [TestCase(25, 5, 1)]
        [TestCase(26, 5, 2)]
        [TestCase(1, 29, 1)]
        [TestCase(2, 29, 2)]
        public void Pagination_UsesOnlyOccupiedSheets(int count, int skip, int expectedPages)
        {
            var document = Capture(count, new LabelOptions { SkipLabels = skip });
            var bytes = document.GeneratePdf(out var pages);
            Assert.That(pages, Is.EqualTo(expectedPages));
            if (count == 0)
            {
                Assert.That(bytes, Is.Empty);
                Assert.That(document.GenerateImages(), Is.Empty);
            }
            Assert.That(document.ContentPositions, Has.Count.EqualTo(count));
            Assert.That(document.GenerateSvg().Count(), Is.EqualTo(expectedPages));
        }

        [Test]
        public void Copies_CanCrossTheFirstSheetBoundaryWithoutLosingOrdering()
        {
            var document = Capture(2, new LabelOptions { SkipLabels = 29, CopiesPerItem = 2 });
            document.GeneratePdf(out var pages);
            Assert.That(pages, Is.EqualTo(2));
            var positions = document.ContentPositions.ToArray();
            Assert.That(positions.Select(x => x.Id), Is.EqualTo(new[] { "item-0", "item-0", "item-1", "item-1" }));
            Assert.That(positions.Select(x => x.PageNumber), Is.EqualTo(new[] { 1, 2, 2, 2 }));
            Assert.That(positions[1].X, Is.EqualTo(13.5).Within(0.001));
            Assert.That(positions[1].Y, Is.EqualTo(36).Within(0.001));
        }

        [Test]
        public void Generation_IsRepeatableAndSnapshotsTheInputOnce()
        {
            var enumerations = 0;
            IEnumerable<int> Items()
            {
                enumerations++;
                for (var index = 0; index < 27; index++) yield return index;
            }
            var document = LabelDocument.Create(Items(), LabelStocks.Avery5160,
                (label, item) => label.CaptureContentPosition($"item-{item}"),
                new LabelOptions { SkipLabels = 5 });
            document.GeneratePdf();
            var first = Geometry(document);
            document.GeneratePdf();
            Assert.That(Geometry(document), Is.EqualTo(first));
            Assert.That(enumerations, Is.EqualTo(1));
        }

        [Test]
        public void AlignmentOffsets_UsePhysicalUnitsAndDoNotChangeSizeOrSkipCount()
        {
            var document = Capture(27, new LabelOptions
            {
                SkipLabels = 5, OffsetX = -1.27f, OffsetY = 2.54f, Unit = Unit.Millimetre
            });
            document.GeneratePdf();
            var positions = document.ContentPositions.ToArray();
            Assert.That(positions[0].X, Is.EqualTo(409.5 - 3.6).Within(0.001));
            Assert.That(positions[0].Y, Is.EqualTo(108 + 7.2).Within(0.001));
            Assert.That(positions[25].X, Is.EqualTo(13.5 - 3.6).Within(0.001));
            Assert.That(positions[25].Y, Is.EqualTo(36 + 7.2).Within(0.001));
            Assert.That(positions.All(x => Math.Abs(x.Width - 189) < 0.001 && Math.Abs(x.Height - 72) < 0.001), Is.True);
        }

        [Test]
        public void CustomMillimetreStock_PreservesMarginsAndBothGaps()
        {
            var stock = new LabelStock
            {
                PageSize = new PageSize(100, 80, Unit.Millimetre),
                LabelWidth = 40, LabelHeight = 20, Columns = 2, Rows = 3,
                MarginLeft = 5, MarginTop = 5, HorizontalGap = 3, VerticalGap = 2,
                Unit = Unit.Millimetre
            };
            var document = LabelDocument.Create(Enumerable.Range(0, 6), stock,
                (label, item) => label.CaptureContentPosition($"item-{item}"));
            document.GeneratePdf(out var pages);
            Assert.That(pages, Is.EqualTo(1));
            var last = document.ContentPositions.Last();
            Assert.That(last.X, Is.EqualTo(48f / 25.4f * 72).Within(0.001));
            Assert.That(last.Y, Is.EqualTo(49f / 25.4f * 72).Within(0.001));
            Assert.That(last.Width, Is.EqualTo(40f / 25.4f * 72).Within(0.001));
        }

        [Test]
        public void RightToLeftTextDefaults_DoNotReverseThePhysicalSlotOrder()
        {
            var document = LabelDocument.Create(new[] { 0, 1 }, LabelStocks.Avery5160,
                (label, item) => label.Row(row => row.ConstantItem(10).Height(10)
                    .CaptureContentPosition($"item-{item}")))
                .WithSettings(new DocumentSettings { ContentDirection = ContentDirection.RightToLeft });
            document.GeneratePdf();
            var positions = document.ContentPositions.ToArray();
            // The ten-point item aligns to the right inside each left-to-right physical slot.
            Assert.That(positions[0].X, Is.EqualTo(13.5 + 189 - 10).Within(0.001));
            Assert.That(positions[1].X, Is.EqualTo(211.5 + 189 - 10).Within(0.001));
        }

        [Test]
        public void Thermal4x6_ProducesOneCorrectlySizedPagePerLabel()
        {
            var document = LabelDocument.Create(new[] { "one", "two" }, LabelStocks.Thermal4x6,
                (label, item) => label.Padding(10).Text(item))
                .WithSettings(new DocumentSettings { ImageRasterDpi = 72 });
            document.GeneratePdf(out var pages);
            Assert.That(pages, Is.EqualTo(2));
            foreach (var data in document.GenerateImages())
            {
                using var image = SKImage.FromEncodedData(data);
                Assert.That(image.Width, Is.EqualTo(288));
                Assert.That(image.Height, Is.EqualTo(432));
            }
        }

        [Test]
        public void SkippedAndUnusedSlots_AreActuallyBlankInTheRasterOutput()
        {
            var document = Capture(1, new LabelOptions { SkipLabels = 5 })
                .WithSettings(new DocumentSettings { ImageRasterDpi = 72 });
            using var bitmap = SKBitmap.Decode(document.GenerateImages().Single());
            Assert.That(bitmap.Width, Is.EqualTo(612));
            Assert.That(bitmap.Height, Is.EqualTo(792));
            for (var slot = 0; slot < 30; slot++)
            {
                var color = bitmap.GetPixel(30 + slot % 3 * 198, 50 + slot / 3 * 72);
                Assert.That(color, Is.EqualTo(slot == 5 ? SKColors.Red : SKColors.White), $"slot {slot + 1}");
            }
        }

        [TestCase(-20, 0, false)]
        [TestCase(20, 0, false)]
        [TestCase(0, -20, false)]
        [TestCase(0, 20, false)]
        [TestCase(-20, 0, true)]
        [TestCase(20, 0, true)]
        public void TranslatedArtwork_CannotPaintIntoSkippedOrUnusedSlots(int offsetX, int offsetY, bool layered)
        {
            var stock = new LabelStock
            {
                PageSize = new PageSize(145, 145),
                LabelWidth = 40, LabelHeight = 40, Columns = 3, Rows = 3,
                MarginLeft = 5, MarginTop = 5, HorizontalGap = 5, VerticalGap = 5
            };
            var document = LabelDocument.Create(new[] { 1 }, stock,
                (label, _) => (layered ? label.ZIndex(1) : label).TranslateX(offsetX).TranslateY(offsetY)
                    .Width(40).Height(40).Background("#FF0000"),
                new LabelOptions { SkipLabels = 4 })
                .WithSettings(new DocumentSettings { ImageRasterDpi = 72 });

            using var bitmap = SKBitmap.Decode(document.GenerateImages().Single());
            Assert.That(bitmap.GetPixel(70 + offsetX / 2, 70 + offsetY / 2), Is.EqualTo(SKColors.Red));
            Assert.That(bitmap.GetPixel(70 + offsetX * 3 / 2, 70 + offsetY * 3 / 2), Is.EqualTo(SKColors.White));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SlotClipping_DoesNotClipOrShiftFollowingLabels(bool layered)
        {
            var document = LabelDocument.Create(new[] { 0, 1 }, LabelStocks.Avery5160,
                (label, item) => (layered ? label.ZIndex(1) : label)
                    .Background(item == 0 ? "#FF0000" : "#0000FF"),
                new LabelOptions { SkipLabels = 5 })
                .WithSettings(new DocumentSettings { ImageRasterDpi = 72 });

            using var bitmap = SKBitmap.Decode(document.GenerateImages().Single());
            Assert.That(bitmap.GetPixel(450, 140), Is.EqualTo(SKColors.Red));
            Assert.That(bitmap.GetPixel(50, 210), Is.EqualTo(SKColors.Blue));
            Assert.That(bitmap.GetPixel(50, 140), Is.EqualTo(SKColors.White));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OversizedContent_FailsWithTheLabelNumberInsteadOfSplitting(bool horizontal)
        {
            var document = LabelDocument.Create(new[] { 1 }, LabelStocks.Avery5160,
                (label, _) =>
                {
                    if (horizontal) label.Width(190).Text("Too wide");
                    else label.Column(column =>
                    {
                        column.Item().Height(50).Text("First half");
                        column.Item().Height(50).Text("Second half");
                    });
                }, new LabelOptions { SkipLabels = 5 });
            var error = Assert.Throws<DocumentLayoutException>(() => document.GeneratePdf());
            Assert.That(error.Message, Does.Contain("Label 1").And.Contain("cannot split"));
        }

        [Test]
        public void InvalidStartingPositionsAndCopies_AreRejectedBeforeRendering()
        {
            foreach (var options in new[]
            {
                new LabelOptions { SkipLabels = -1 }, new LabelOptions { SkipLabels = 30 },
                new LabelOptions { StartRow = 1 }, new LabelOptions { StartColumn = 1 },
                new LabelOptions { StartRow = 0, StartColumn = 1 },
                new LabelOptions { StartRow = 11, StartColumn = 1 },
                new LabelOptions { StartRow = 1, StartColumn = 4 },
                new LabelOptions { StartRow = 2, StartColumn = 3, SkipLabels = 4 },
                new LabelOptions { CopiesPerItem = 0 }, new LabelOptions { CopiesPerItem = -1 },
                new LabelOptions { OffsetX = float.NaN }, new LabelOptions { OffsetY = float.PositiveInfinity },
                new LabelOptions { OffsetX = 14 }, new LabelOptions { OffsetY = -37 }
            })
                Assert.That(() => Capture(1, options), Throws.InstanceOf<ArgumentException>(), options.ToString());

            Assert.DoesNotThrow(() => Capture(1, new LabelOptions { SkipLabels = 5, StartRow = 2, StartColumn = 3 }));
        }

        [Test]
        public void InvalidStock_IsRejectedBeforeRendering()
        {
            foreach (var stock in new[]
            {
                LabelStocks.Avery5160 with { LabelWidth = 0 },
                LabelStocks.Avery5160 with { LabelHeight = float.PositiveInfinity },
                LabelStocks.Avery5160 with { Rows = 0 },
                LabelStocks.Avery5160 with { Columns = int.MaxValue },
                LabelStocks.Avery5160 with { HorizontalGap = -1 },
                LabelStocks.Avery5160 with { VerticalGap = float.NaN },
                LabelStocks.Avery5160 with { MarginTop = -1 },
                LabelStocks.Avery5160 with { LabelWidth = 3 },
                LabelStocks.Avery5160 with { PageSize = new PageSize(float.NaN, 100) }
            })
                Assert.That(() => LabelDocument.Create(new[] { 1 }, stock, (label, _) => label.Text("x")),
                    Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void Calibration_RespectsTheStartingSlotAndProducesOneSheet()
        {
            var document = LabelDocument.CreateCalibration(LabelStocks.Avery5160, new LabelOptions { SkipLabels = 5 });
            document.GeneratePdf(out var pages);
            Assert.That(pages, Is.EqualTo(1));
            Assert.That(document.GenerateSvg().Count(), Is.EqualTo(1));
        }

        private static Document Capture(int count, LabelOptions options) =>
            LabelDocument.Create(Enumerable.Range(0, count), LabelStocks.Avery5160,
                (label, item) => label.CaptureContentPosition($"item-{item}").Background("#FF0000"), options);

        private static object[] Geometry(Document document) => document.ContentPositions
            .Select(x => (object)(x.Id, x.PageNumber, x.X, x.Y, x.Width, x.Height)).ToArray();
    }
}
