using FossPDF.Drawing;
using FossPDF.Helpers;
using FossPDF.Infrastructure;
using SkiaSharp;

namespace FossPDF.Elements
{
    public delegate void DrawOnCanvas(SKCanvas canvas, Size availableSpace);
    
    internal class Canvas : Element, ICacheable
    {
        private SemanticNode? _semanticNode;
        public DrawOnCanvas? Handler { get; set; }
        
        internal override SpacePlan Measure(Size availableSpace)
        {
            return availableSpace.IsNegative() 
                ? SpacePlan.Wrap() 
                : SpacePlan.FullRender(availableSpace);
        }

        internal override void Draw(Size availableSpace)
        {
            var backend = AutomaticSemanticScope.Unwrap(Canvas) as Drawing.SkiaCanvasBase;
            var skiaCanvas = backend?.Canvas;
            
            if (Handler == null || skiaCanvas == null)
                return;

            using var semantic = new AutomaticSemanticScope(Canvas, ref _semanticNode, "Figure");
            skiaCanvas = backend!.Canvas!;
            backend.NotifyPaintOperation();
            var saved = skiaCanvas.Save();
            try { Handler.Invoke(skiaCanvas, availableSpace); }
            finally { skiaCanvas.RestoreToCount(saved); }
        }
    }
}
