using FossPDF.Drawing;
using FossPDF.Infrastructure;

namespace FossPDF.Elements
{
    internal class SkipOnce : ContainerElement, IStateResettable, IStateSnapshot
    {
        private bool FirstPageWasSkipped { get; set; }

        public void ResetState()
        {
            FirstPageWasSkipped = false;
        }

        object? IStateSnapshot.CaptureState()
        {
            return FirstPageWasSkipped;
        }

        void IStateSnapshot.RestoreState(object? state)
        {
            if (state is bool wasSkipped)
                FirstPageWasSkipped = wasSkipped;
        }

        internal override SpacePlan Measure(Size availableSpace)
        {
            if (Child == null || !FirstPageWasSkipped)
                return SpacePlan.FullRender(Size.Zero);

            return Child.Measure(availableSpace);
        }

        internal override void Draw(Size availableSpace)
        {
            if (Child == null)
                return;

            if (FirstPageWasSkipped)
                Child.Draw(availableSpace);

            FirstPageWasSkipped = true;
        }
    }
}
