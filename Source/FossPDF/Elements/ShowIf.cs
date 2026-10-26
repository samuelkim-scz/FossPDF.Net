using System;
using FossPDF.Drawing;
using FossPDF.Infrastructure;

namespace FossPDF.Elements
{
    /// <summary>
    /// Page information available to a conditional rendering predicate.
    /// </summary>
    public sealed class ShowIfContext
    {
        /// <summary>The one-based number of the page currently being rendered.</summary>
        public int PageNumber { get; internal set; }

        /// <summary>
        /// The total number of pages in the document, or <c>null</c> during the initial layout
        /// pass when the total is not known yet. Predicates must handle the null case.
        /// </summary>
        public int? TotalPages { get; internal set; }
    }

    internal sealed class ShowIf : ContainerElement
    {
        public Predicate<ShowIfContext> VisibilityPredicate { get; set; } = null!;

        internal override SpacePlan Measure(Size availableSpace)
        {
            if (!IsVisible())
                return SpacePlan.FullRender(Size.Zero);

            return base.Measure(availableSpace);
        }

        internal override void Draw(Size availableSpace)
        {
            if (IsVisible())
                base.Draw(availableSpace);
        }

        private bool IsVisible()
        {
            var context = new ShowIfContext
            {
                PageNumber = PageContext.CurrentPage,
                TotalPages = PageContext.IsInitialRenderingPhase ? null : PageContext.DocumentLength
            };

            return VisibilityPredicate(context);
        }
    }
}
