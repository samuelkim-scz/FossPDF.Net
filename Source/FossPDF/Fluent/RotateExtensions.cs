using System;
using FossPDF.Elements;
using FossPDF.Infrastructure;

namespace FossPDF.Fluent
{
    public static class RotateExtensions
    {
        private static IContainer SimpleRotate(this IContainer element, Action<SimpleRotate> handler)
        {
            var scale = element as SimpleRotate ?? new SimpleRotate();
            handler(scale);
            
            return element.Element(scale);
        }
        
        public static IContainer RotateLeft(this IContainer element)
        {
            return element.SimpleRotate(x => x.TurnCount--);
        }
        
        public static IContainer RotateRight(this IContainer element)
        {
            return element.SimpleRotate(x => x.TurnCount++);
        }
        
        /// <summary>Rotates the element content by the specified angle.</summary>
        /// <param name="element">The container to rotate.</param>
        /// <param name="angle">Rotation angle in degrees.</param>
        public static IContainer Rotate(this IContainer element, float angle)
        {
            return element.Element(new Rotate
            {
                Angle = angle
            });
        }
    }
}
