using System;
using System.Collections.Generic;
using FossPDF.Drawing;
using FossPDF.Drawing.Exceptions;
using FossPDF.Infrastructure;

namespace FossPDF.Elements
{
    internal sealed class LabelSlot : ContainerElement
    {
        internal int Number { get; init; }
        internal Position Offset { get; init; }
    }

    // Measures every label before drawing the sheet, so content cannot flow between slots.
    internal sealed class LabelSheet : Element
    {
        internal Size SheetSize { get; init; }
        internal Size LabelSize { get; init; }
        internal List<LabelSlot> Slots { get; } = new();

        internal override IEnumerable<Element?> GetChildren() => Slots;

        internal override void CreateProxy(Func<Element?, Element?> create)
        {
            foreach (var slot in Slots)
                slot.Child = create(slot.Child);
        }

        internal override SpacePlan Measure(Size availableSpace)
        {
            if (availableSpace.Width + Size.Epsilon < SheetSize.Width ||
                availableSpace.Height + Size.Epsilon < SheetSize.Height)
                return SpacePlan.Wrap();

            foreach (var slot in Slots)
            {
                var measurement = slot.Measure(LabelSize);
                if (measurement.Type != SpacePlanType.FullRender ||
                    measurement.Width > LabelSize.Width + Size.Epsilon ||
                    measurement.Height > LabelSize.Height + Size.Epsilon)
                    throw new DocumentLayoutException(
                        $"Label {slot.Number} does not fit its {LabelSize.Width:0.###} x {LabelSize.Height:0.###} point slot. " +
                        "Reduce the content or use larger label stock. Labels cannot split across slots or pages.");
            }
            return SpacePlan.FullRender(SheetSize);
        }

        internal override void Draw(Size availableSpace)
        {
            foreach (var slot in Slots)
            {
                Canvas.Translate(slot.Offset);
                Canvas.SaveState();
                try
                {
                    Canvas.ClipRectangle(Position.Zero, LabelSize, BorderRadius.Zero);
                    slot.Draw(LabelSize);
                }
                finally
                {
                    Canvas.RestoreState();
                    Canvas.Translate(slot.Offset.Reverse());
                }
            }
        }
    }
}
