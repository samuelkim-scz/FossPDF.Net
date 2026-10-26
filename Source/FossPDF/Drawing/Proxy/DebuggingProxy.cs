using FossPDF.Infrastructure;

namespace FossPDF.Drawing.Proxy
{
    internal class DebuggingProxy : ElementProxy
    {
        private DebuggingState DebuggingState { get; }

        public DebuggingProxy(DebuggingState debuggingState, Element child)
        {
            DebuggingState = debuggingState;
            Child = child;
        }
        
        internal override SpacePlan Measure(Size availableSpace)
        {
            var child = Child;
            if (child == null)
                return base.Measure(availableSpace);

            DebuggingState.RegisterMeasure(child, availableSpace);
            var spacePlan = base.Measure(availableSpace);
            DebuggingState.RegisterMeasureResult(child, spacePlan);

            return spacePlan;
        }
    }
}
