using System;
using System.Collections.Generic;
using System.Linq;
using FossPDF.Elements;
using FossPDF.Helpers;
using FossPDF.Infrastructure;

namespace FossPDF.Fluent
{
    /// <summary>Creates fixed-slot label documents using the standard FossPDF content API.</summary>
    public static class LabelDocument
    {
        /// <summary>
        /// Snapshots the input sequence once. Labels fill left to right, then top to bottom;
        /// skipped slots affect the first sheet only. Each item is repeated CopiesPerItem times.
        /// The content callback runs during composition and must fit within a single label.
        /// </summary>
        public static Document Create<T>(IEnumerable<T> items, LabelStock stock,
            Action<IContainer, T> content, LabelOptions? options = null)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (stock == null) throw new ArgumentNullException(nameof(stock));
            if (content == null) throw new ArgumentNullException(nameof(content));
            stock = stock.InPoints();
            options ??= new LabelOptions();
            var skip = options.GetSkipCount(stock);
            var offsets = options.GetOffsets(stock);
            var data = items.ToArray();
            var labelCount = (long)data.Length * options.CopiesPerItem;
            if (labelCount > int.MaxValue)
                throw new ArgumentException("The total number of labels must fit an Int32.", nameof(items));
            var capacity = stock.Rows * stock.Columns;

            return Document.Create(document =>
            {
                var labelIndex = 0;
                var firstSlot = skip;
                while (labelIndex < labelCount)
                {
                    var sheet = new LabelSheet
                    {
                        SheetSize = stock.PageSize,
                        LabelSize = new Size(stock.LabelWidth, stock.LabelHeight)
                    };
                    for (var slotIndex = firstSlot; slotIndex < capacity && labelIndex < labelCount; slotIndex++)
                    {
                        var slot = new LabelSlot
                        {
                            Number = labelIndex + 1,
                            Offset = new Position(
                                stock.MarginLeft + slotIndex % stock.Columns * (stock.LabelWidth + stock.HorizontalGap) + offsets.X,
                                stock.MarginTop + slotIndex / stock.Columns * (stock.LabelHeight + stock.VerticalGap) + offsets.Y)
                        };
                        content(slot, data[labelIndex / options.CopiesPerItem]);
                        sheet.Slots.Add(slot);
                        labelIndex++;
                    }
                    document.Page(page =>
                    {
                        page.Size(stock.PageSize);
                        page.Margin(0);
                        page.PageColor(Colors.White);
                        page.Content().Element(sheet);
                    });
                    firstSlot = 0;
                }
            });
        }

        /// <summary>One sheet with numbered outlines for checking stock and printer alignment.</summary>
        public static Document CreateCalibration(LabelStock stock, LabelOptions? options = null)
        {
            if (stock == null) throw new ArgumentNullException(nameof(stock));
            var normalized = stock.InPoints();
            options ??= new LabelOptions();
            var skip = options.GetSkipCount(normalized);
            return Create(Enumerable.Range(skip + 1, normalized.Rows * normalized.Columns - skip), stock,
                (label, slot) => label.Border(0.5f).BorderColor(Colors.Grey.Medium)
                    .AlignCenter().AlignMiddle().Text($"Slot {slot}").FontSize(9),
                options with { CopiesPerItem = 1 });
        }
    }
}
