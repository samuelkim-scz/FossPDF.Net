using FossPDF.Drawing;
using FossPDF.Infrastructure;

namespace FossPDF.Elements
{
    /// <summary>
    /// Keeps its content together on the first page where it appears. If the content does not fit
    /// entirely, it starts on the next page. Content that is longer than a page continues normally.
    /// </summary>
    internal sealed class PreventPageBreak : ContainerElement, IStateResettable, IStateSnapshot
    {
        private bool IsFirstPageRendered { get; set; }

        public void ResetState()
        {
            IsFirstPageRendered = false;
        }

        object? IStateSnapshot.CaptureState()
        {
            return IsFirstPageRendered;
        }

        void IStateSnapshot.RestoreState(object? state)
        {
            if (state is bool isFirstPageRendered)
                IsFirstPageRendered = isFirstPageRendered;
        }

        internal override SpacePlan Measure(Size availableSpace)
        {
            var measurement = base.Measure(availableSpace);

            if (IsFirstPageRendered)
                return measurement;

            if (measurement.Type == SpacePlanType.FullRender)
                return measurement;

            return SpacePlan.PartialRender(Size.Zero);
        }

        internal override void Draw(Size availableSpace)
        {
            if (IsFirstPageRendered)
            {
                base.Draw(availableSpace);
                return;
            }

            var measurement = base.Measure(availableSpace);

            if (measurement.Type == SpacePlanType.FullRender)
                base.Draw(availableSpace);

            IsFirstPageRendered = true;
        }
    }
}
