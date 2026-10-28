using System;
using System.Collections.Generic;
using System.Linq;
using FossPDF.Drawing;
using FossPDF.Helpers;
using FossPDF.Infrastructure;

namespace FossPDF.Elements
{
    /// <summary>
    /// Wraps a stateful element and remembers the state it had before it was first drawn. The
    /// multi-column layout draws content speculatively while measuring, then rewinds every
    /// observer so the real drawing pass starts from exactly the same state.
    /// </summary>
    internal sealed class MultiColumnChildDrawingObserver : ContainerElement
    {
        internal bool HasBeenDrawn { get; private set; }

        private object? StateBeforeDrawing { get; set; }

        internal override void Draw(Size availableSpace)
        {
            if (!HasBeenDrawn)
            {
                StateBeforeDrawing = (Child as IStateSnapshot)?.CaptureState();
                HasBeenDrawn = true;
            }

            Child?.Draw(availableSpace);
        }

        internal void RestoreState()
        {
            (Child as IStateSnapshot)?.RestoreState(StateBeforeDrawing);
        }

        internal void ResetDrawingState()
        {
            HasBeenDrawn = false;
            StateBeforeDrawing = null;
        }
    }

    /// <summary>
    /// Flows a single content stream through a fixed number of vertical columns. Content fills a
    /// column top to bottom, then continues at the top of the next column; when the current page's
    /// columns are full, the element reports a partial render so the document continues on the next
    /// page.
    /// <para>
    /// Measuring uses speculative drawing: the content is drawn to a free canvas for every column
    /// and rewound through <see cref="IStateSnapshot"/>. This makes balancing possible without
    /// consuming dynamic content or emitting duplicate output.
    /// </para>
    /// </summary>
    internal class MultiColumn : Element, IContentDirectionAware
    {
        internal Element Content { get; set; } = Empty.Instance;
        internal Element Spacer { get; set; } = Empty.Instance;

        internal int ColumnCount { get; set; } = 2;
        internal bool BalanceHeight { get; set; }
        internal float Spacing { get; set; }

        public ContentDirection ContentDirection { get; set; }

        private ProxyCanvas ContentCanvas { get; } = new();
        private FreeCanvas MeasurementCanvas { get; } = new();

        private MultiColumnChildDrawingObserver[]? Observers { get; set; }

        internal override IEnumerable<Element?> GetChildren()
        {
            yield return Content;
            yield return Spacer;
        }

        internal override void CreateProxy(Func<Element?, Element?> create)
        {
            Content = create(Content) ?? Empty.Instance;
            Spacer = create(Spacer) ?? Empty.Instance;
        }

        private void BuildObserverState()
        {
            if (Observers != null)
                return;

            this.VisitChildren(element =>
            {
                element.CreateProxy(child => child is IStateSnapshot
                    ? new MultiColumnChildDrawingObserver { Child = child }
                    : child);
            });

            Observers = CollectObservers(this).ToArray();
        }

        private static IEnumerable<MultiColumnChildDrawingObserver> CollectObservers(Element root)
        {
            var stack = new Stack<Element>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var element = stack.Pop();

                if (element is MultiColumnChildDrawingObserver observer)
                {
                    yield return observer;

                    if (observer.Child != null)
                        stack.Push(observer.Child);

                    continue;
                }

                foreach (var child in element.GetChildren())
                {
                    if (child != null)
                        stack.Push(child);
                }
            }
        }

        private void ResetObserverState(bool restoreChildState)
        {
            if (Observers == null)
                return;

            foreach (var observer in Observers)
            {
                if (!observer.HasBeenDrawn)
                    continue;

                if (restoreChildState)
                    observer.RestoreState();

                observer.ResetDrawingState();
            }
        }

        private void EnsureContentCanvas()
        {
            if (ReferenceEquals(Content.Canvas, ContentCanvas))
                return;

            Content.InjectDependencies(PageContext, ContentCanvas);
        }

        internal override SpacePlan Measure(Size availableSpace)
        {
            BuildObserverState();
            EnsureContentCanvas();

            if (ColumnCount < 1)
                return SpacePlan.Wrap();

            var measurements = MeasureColumns(availableSpace);
            var firstMeasurement = measurements[0];

            if (firstMeasurement.Type == SpacePlanType.Wrap)
                return SpacePlan.Wrap();

            var contentFits = measurements.Last().Type == SpacePlanType.FullRender;

            if (!contentFits)
                return SpacePlan.PartialRender(availableSpace.Width, measurements.Max(x => x.Height));

            var height = measurements.Max(x => x.Height);

            if (!BalanceHeight || height <= 0)
                return SpacePlan.FullRender(availableSpace.Width, height);

            // Binary-search the smallest column height that still fits all content. The upper
            // bound is known to fit because the unbounded measurement above succeeded.
            var minimumHeight = 0f;
            var maximumHeight = availableSpace.Height;

            foreach (var _ in Enumerable.Range(0, 8))
            {
                var middleHeight = (minimumHeight + maximumHeight) / 2;
                var middleMeasurements = MeasureColumns(new Size(availableSpace.Width, middleHeight));

                if (middleMeasurements.Last().Type == SpacePlanType.FullRender)
                    maximumHeight = middleHeight;
                else
                    minimumHeight = middleHeight;
            }

            return SpacePlan.FullRender(availableSpace.Width, maximumHeight);
        }

        /// <summary>
        /// Measures and speculatively draws one column at a time, mirroring the operations of
        /// <see cref="Draw(Size)"/>. The observer state is restored before returning.
        /// </summary>
        private List<SpacePlan> MeasureColumns(Size availableSpace)
        {
            var columnSpace = GetColumnAvailableSpace(availableSpace);
            var measurements = new List<SpacePlan>(ColumnCount);

            ContentCanvas.Target = MeasurementCanvas;

            try
            {
                for (var index = 0; index < ColumnCount; index++)
                {
                    var measurement = Content.Measure(columnSpace);
                    measurements.Add(measurement);

                    // Draw with the same size the real drawing pass uses, so state advances
                    // identically in both passes.
                    Content.Draw(new Size(columnSpace.Width, measurement.Height));

                    if (measurement.Type == SpacePlanType.FullRender)
                        break;
                }
            }
            finally
            {
                ResetObserverState(restoreChildState: true);
            }

            return measurements;
        }

        private Size GetColumnAvailableSpace(Size availableSpace)
        {
            var spacingWidth = Spacing * (ColumnCount - 1);
            var columnWidth = (availableSpace.Width - spacingWidth) / ColumnCount;

            return new Size(Math.Max(0, columnWidth), availableSpace.Height);
        }

        internal override void Draw(Size availableSpace)
        {
            var columnSpace = GetColumnAvailableSpace(availableSpace);
            var spacerSpace = new Size(Spacing, availableSpace.Height);

            var offset = 0f;
            ContentCanvas.Target = Canvas;

            for (var index = 1; index <= ColumnCount; index++)
            {
                var measurement = Content.Measure(columnSpace);
                var targetSize = new Size(columnSpace.Width, measurement.Height);

                var contentOffset = GetOffset(targetSize.Width);

                Canvas.Translate(contentOffset);
                Content.Draw(targetSize);
                Canvas.Translate(contentOffset.Reverse());

                offset += columnSpace.Width;

                if (measurement.Type == SpacePlanType.FullRender)
                    break;

                var spacerMeasurement = Spacer.Measure(spacerSpace);

                if (index == ColumnCount || spacerMeasurement.Type == SpacePlanType.Wrap)
                    continue;

                var spacerOffset = GetOffset(Spacing);

                Canvas.Translate(spacerOffset);
                Spacer.Draw(spacerSpace);
                Canvas.Translate(spacerOffset.Reverse());

                offset += Spacing;
            }

            ResetObserverState(restoreChildState: false);

            Position GetOffset(float width)
            {
                return ContentDirection == ContentDirection.LeftToRight
                    ? new Position(offset, 0)
                    : new Position(availableSpace.Width - offset - width, 0);
            }
        }
    }
}
