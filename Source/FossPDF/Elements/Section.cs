using FossPDF.Infrastructure;

namespace FossPDF.Elements
{
    internal class Section : ContainerElement, IStateResettable, IStateSnapshot
    {
        public string LocationName { get; set; } = string.Empty;
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
        
        internal override void Draw(Size availableSpace)
        {
            if (!IsRendered)
            {
                Canvas.DrawSection(LocationName);
                IsRendered = true;
            }
            
            PageContext.SetSectionPage(LocationName);
            base.Draw(availableSpace);
        }
    }
}
