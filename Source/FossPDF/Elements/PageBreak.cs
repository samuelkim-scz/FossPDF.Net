using FossPDF.Drawing;
using FossPDF.Helpers;
using FossPDF.Infrastructure;

namespace FossPDF.Elements
{
    internal class PageBreak : Element, IStateResettable, IStateSnapshot
    {
        private bool IsRendered { get; set; }

        public void ResetState()
        {
            IsRendered = false;
        }

        object? IStateSnapshot.CaptureState()
        {
            return IsRendered;
        }

        void IStateSnapshot.RestoreState(object? state)
        {
            if (state is bool isRendered)
                IsRendered = isRendered;
        }

        internal override SpacePlan Measure(Size availableSpace)
        {
            if (availableSpace.IsNegative())
                return SpacePlan.Wrap();
            
            if (IsRendered)
                return SpacePlan.FullRender(0, 0);

            return SpacePlan.PartialRender(Size.Zero);
        }

        internal override void Draw(Size availableSpace)
        {
            IsRendered = true;
        }
    }
}
