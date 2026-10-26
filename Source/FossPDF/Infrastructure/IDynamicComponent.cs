using System;
using FossPDF.Elements;
using FossPDF.Helpers;

namespace FossPDF.Infrastructure
{
    internal class DynamicComponentProxy
    {
        internal Action<object> SetState { get; }
        internal Func<object> GetState { get; }
        internal Func<DynamicContext, DynamicComponentComposeResult> Compose { get; }

        private DynamicComponentProxy(Action<object> setState, Func<object> getState,
            Func<DynamicContext, DynamicComponentComposeResult> compose)
        {
            SetState = setState;
            GetState = getState;
            Compose = compose;
        }
        
        internal static DynamicComponentProxy CreateFrom<TState>(IDynamicComponent<TState> component) where TState : struct
        {
            return new DynamicComponentProxy(x => component.State = (TState)x,
                () => component.State, component.Compose);
        }
    }

    public class DynamicComponentComposeResult
    {
        public IElement? Content { get; set; }
        public bool HasMoreContent { get; set; }
    }
    
    public interface IDynamicComponent<TState> where TState : struct
    {
        TState State { get; set; }
        DynamicComponentComposeResult Compose(DynamicContext context);
    }
}
