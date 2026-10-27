using System.Numerics;
using FossPDF.Drawing;
using FossPDF.Infrastructure;

namespace FossPDF.Elements
{
    internal sealed class ElementPositionLocator : ContainerElement
    {
        public string Id { get; set; } = string.Empty;

        internal override void Draw(Size availableSpace)
        {
            if (PageContext.IsInitialRenderingPhase)
            {
                var transform = Canvas.Transform;
                var scaleX = new Vector2(transform.M11, transform.M12).Length();
                var scaleY = new Vector2(transform.M21, transform.M22).Length();
                var actualPosition = Vector2.Transform(Vector2.Zero, transform);

                PageContext.CaptureContentPosition(new PageElementLocation
                {
                    Id = Id,
                    PageNumber = PageContext.CurrentPage,
                    Width = availableSpace.Width * scaleX,
                    Height = availableSpace.Height * scaleY,
                    X = actualPosition.X,
                    Y = actualPosition.Y,
                    Transform = transform
                });
            }

            base.Draw(availableSpace);
        }
    }
}
