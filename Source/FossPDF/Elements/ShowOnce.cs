using FossPDF.Drawing;
using FossPDF.Infrastructure;

namespace FossPDF.Elements
{
    internal class ShowOnce : ContainerElement, IStateResettable, IStateSnapshot, ICacheable
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
            if (Child == null || IsRendered)
                return SpacePlan.FullRender(0, 0);
            
            return base.Measure(availableSpace);
        }

        internal override void Draw(Size availableSpace)
        {
            if (Child == null || IsRendered)
                return;
            
            if (base.Measure(availableSpace).Type == SpacePlanType.FullRender)
                IsRendered = true;
            
            base.Draw(availableSpace);
        }
    }
}
