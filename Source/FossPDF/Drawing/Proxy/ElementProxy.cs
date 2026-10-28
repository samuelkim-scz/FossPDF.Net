using FossPDF.Infrastructure;

namespace FossPDF.Drawing.Proxy
{
    /// <summary>
    /// Transparent wrapper around another element, used for caching and debugging. It forwards
    /// layout state snapshots to its child so that speculative layout, which wraps elements in
    /// observers after proxies have been applied, can still rewind the real element.
    /// </summary>
    internal class ElementProxy : ContainerElement, IStateSnapshot
    {
        object? IStateSnapshot.CaptureState()
        {
            return (Child as IStateSnapshot)?.CaptureState();
        }

        void IStateSnapshot.RestoreState(object? state)
        {
            (Child as IStateSnapshot)?.RestoreState(state);
        }
    }
}
