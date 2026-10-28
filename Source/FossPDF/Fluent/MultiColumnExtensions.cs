using System;
using FossPDF.Drawing.Exceptions;
using FossPDF.Elements;
using FossPDF.Infrastructure;

namespace FossPDF.Fluent
{
    public class MultiColumnDescriptor
    {
        internal MultiColumn MultiColumn { get; } = new();

        /// <summary>
        /// Sets the number of vertical columns. Defaults to 2.
        /// </summary>
        public void Columns(int value = 2)
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(value), "The number of columns must be at least 1.");

            MultiColumn.ColumnCount = value;
        }

        /// <summary>
        /// Sets the horizontal space between columns. Negative values overlap adjacent columns.
        /// </summary>
        public void Spacing(float value, Unit unit = Unit.Point)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Spacing must be a finite number.");

            MultiColumn.Spacing = value.ToPoints(unit);
        }

        /// <summary>
        /// Distributes content so that every used column has approximately the same height.
        /// Without balancing, each column fills top to bottom and the layout reports the height it
        /// used; when a column is partially filled, that is the available height.
        /// </summary>
        public void BalanceHeight(bool enable = true)
        {
            MultiColumn.BalanceHeight = enable;
        }

        /// <summary>
        /// Defines the content that flows through the columns. Supports all layout elements.
        /// </summary>
        public IContainer Content()
        {
            if (MultiColumn.Content is not Empty)
                throw new DocumentComposeException("The 'MultiColumn.Content' layer has already been defined. Please call this method only once.");

            var container = new Container();
            MultiColumn.Content = container;
            return container;
        }

        /// <summary>
        /// Defines the element drawn in the gap between columns. It is repeated for every gap and
        /// receives a slot as wide as the configured spacing.
        /// </summary>
        public IContainer Spacer()
        {
            if (MultiColumn.Spacer is not Empty)
                throw new DocumentComposeException("The 'MultiColumn.Spacer' layer has already been defined. Please call this method only once.");

            var container = new RepeatContent();
            MultiColumn.Spacer = new DefaultArtifactElement { Child = container };
            return container;
        }
    }

    public static class MultiColumnExtensions
    {
        /// <summary>
        /// Flows the content through the requested number of vertical columns, continuing on the
        /// next page when the current page's columns are full.
        /// </summary>
        public static void MultiColumn(this IContainer element, Action<MultiColumnDescriptor> handler)
        {
            var descriptor = new MultiColumnDescriptor();
            handler(descriptor);

            // A height-balanced layout knows how much space it really needs, so it must not expand
            // to the full height the parent offers. The shrink layer applies the measured height
            // during drawing as well.
            var target = descriptor.MultiColumn.BalanceHeight
                ? element.Element(new Shrink { Vertical = true })
                : element;

            target.Element(descriptor.MultiColumn);
        }
    }
}
