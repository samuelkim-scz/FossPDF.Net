using System;
using FossPDF.Elements;
using FossPDF.Infrastructure;

namespace FossPDF.Fluent
{
    public static class LazyExtensions
    {
        /// <summary>
        /// Defers composition of this section until layout reaches it and releases the composed
        /// content after it has fully rendered.
        /// </summary>
        public static void Lazy(this IContainer element, Action<IContainer> contentBuilder)
        {
            AddLazy(element, contentBuilder, cache: false);
        }

        /// <summary>
        /// Defers composition of this section and retains the composed tree for later render passes.
        /// </summary>
        public static void LazyWithCache(this IContainer element, Action<IContainer> contentBuilder)
        {
            AddLazy(element, contentBuilder, cache: true);
        }

        private static void AddLazy(IContainer element, Action<IContainer> contentBuilder, bool cache)
        {
            if (contentBuilder == null)
                throw new ArgumentNullException(nameof(contentBuilder));

            element.Element(new Lazy
            {
                ContentSource = contentBuilder,
                IsCacheable = cache
            });
        }
    }
}
