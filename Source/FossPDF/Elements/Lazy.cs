using System;
using System.Collections.Generic;
using FossPDF.Drawing;
using FossPDF.Helpers;
using FossPDF.Infrastructure;

namespace FossPDF.Elements
{
    internal sealed class Lazy : ContainerElement, IStateResettable, IStateSnapshot, IContentDirectionAware
    {
        public Action<IContainer> ContentSource { get; set; } = _ => { };
        public bool IsCacheable { get; set; }
        public TextStyle TextStyle { get; set; } = TextStyle.Default;
        public ContentDirection ContentDirection { get; set; }

        private bool IsRendered { get; set; }

        private sealed record LayoutState(bool IsRendered, Element? Child,
            List<(IStateSnapshot Element, object? State)> Descendants);

        object? IStateSnapshot.CaptureState()
        {
            // Deferred descendants did not exist when the multi-column observers were installed.
            // Capture them here so speculative drawing can rewind both progress and tree release.
            var descendants = new List<(IStateSnapshot Element, object? State)>();
            Capture(Child);
            return new LayoutState(IsRendered, Child, descendants);

            void Capture(Element? element)
            {
                if (element == null)
                    return;

                // Nested Lazy elements capture their own deferred descendants.
                if (element is not Lazy)
                    foreach (var child in element.GetChildren())
                        Capture(child);

                if (element is IStateSnapshot snapshot)
                    descendants.Add((snapshot, snapshot.CaptureState()));
            }
        }

        void IStateSnapshot.RestoreState(object? state)
        {
            if (state is not LayoutState layout)
                return;

            IsRendered = layout.IsRendered;
            Child = layout.Child;
            foreach (var descendant in layout.Descendants)
                descendant.Element.RestoreState(descendant.State);
        }

        public void ResetState()
        {
            IsRendered = false;

            // Repeat can reset a section after every page. Keep the initial-pass tree
            // available until glyph collection, even when its layout state is reset.
            if (!IsCacheable && PageContext?.IsInitialRenderingPhase != true)
            {
                Child = Empty.Instance;
                return;
            }

            Child?.VisitChildren(x => (x as IStateResettable)?.ResetState());
        }

        internal override SpacePlan Measure(Size availableSpace)
        {
            if (IsRendered)
                return SpacePlan.FullRender(0, 0);

            EnsureContent();
            return base.Measure(availableSpace);
        }

        internal override void Draw(Size availableSpace)
        {
            if (IsRendered)
                return;

            EnsureContent();

            var measurement = base.Measure(availableSpace);
            base.Draw(availableSpace);

            if (measurement.Type != SpacePlanType.FullRender)
                return;

            IsRendered = true;

            // Keep content through the first pass so glyph subsetting can inspect it. Later passes
            // may release it immediately; a cacheable Lazy keeps the composed tree for reuse.
            if (!IsCacheable && PageContext?.IsInitialRenderingPhase == false)
                Child = Empty.Instance;
        }

        private void EnsureContent()
        {
            if (Child != null && Child != Empty.Instance)
                return;

            var container = new Container();
            Child = container;

            try
            {
                ContentSource(container);

                container.ApplyInheritedAndGlobalTexStyle(TextStyle);
                container.ApplyContentDirection(ContentDirection);
                container.InjectDependencies(PageContext, Canvas);
                container.VisitChildren(x => (x as IStateResettable)?.ResetState());
            }
            catch
            {
                Child = Empty.Instance;
                throw;
            }
        }
    }
}
