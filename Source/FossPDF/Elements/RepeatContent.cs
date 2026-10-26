using FossPDF.Drawing;
using FossPDF.Helpers;
using FossPDF.Infrastructure;

namespace FossPDF.Elements
{
    /// <summary>
    /// Repeats its content on every page where the surrounding layout is still being rendered.
    /// A typical use is a label or key that should stay visible while an adjacent cell continues.
    /// </summary>
    internal sealed class RepeatContent : ContainerElement
    {
        internal override void Draw(Size availableSpace)
        {
            if (Child == null)
                return;

            var childMeasurement = Child.Measure(availableSpace);

            base.Draw(availableSpace);

            // Once the repeated content has fully rendered, restore its internal state so the
            // next page can draw it again. Measurement alone never consumes the content.
            if (childMeasurement.Type == SpacePlanType.FullRender)
                Child.VisitChildren(x => (x as IStateResettable)?.ResetState());
        }
    }
}
