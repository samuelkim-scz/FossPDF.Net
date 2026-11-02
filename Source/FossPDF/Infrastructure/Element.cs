using System;
using System.Collections;
using System.Collections.Generic;
using FossPDF.Drawing;

namespace FossPDF.Infrastructure
{
    // Layout diagnostics enumerate public configuration properties on concrete elements.
#if NET9_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
#endif
    internal abstract class Element : IElement
    {
        // DocumentGenerator injects these dependencies before measuring or drawing.
        internal IPageContext PageContext { get; set; } = null!;
        internal ICanvas Canvas { get; set; } = null!;
        
        internal virtual IEnumerable<Element?> GetChildren()
        {
            yield break;
        }

        internal virtual void CreateProxy(Func<Element?, Element?> create)
        {
            
        }
        
        internal abstract SpacePlan Measure(Size availableSpace);
        internal abstract void Draw(Size availableSpace);
    }
}
