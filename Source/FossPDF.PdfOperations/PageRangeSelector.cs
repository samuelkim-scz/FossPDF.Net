using System;
using System.Collections.Generic;
using System.Globalization;

namespace FossPDF.PdfOperations
{
    /// <summary>
    /// Parses page selectors using the same compact syntax as the pinned target:
    /// <c>1</c>, <c>z</c> (last), <c>r2</c> (second to last), <c>1,6,4</c>,
    /// <c>3-7</c>, <c>7-3</c>, <c>1-z</c>, <c>1-20:even</c>, <c>1-10,x3-4</c>.
    /// </summary>
    internal static class PageRangeSelector
    {
        private enum Parity
        {
            Odd,
            Even
        }

        /// <summary>
        /// Resolves a selector into zero-based page indexes in selection order.
        /// </summary>
        /// <remarks>
        /// Items are applied left to right: a positive item appends its pages, an <c>x</c> item
        /// removes every matching page from the pages accumulated so far. A page may therefore
        /// appear more than once when it is selected by more than one positive item.
        /// </remarks>
        public static IReadOnlyList<int> Resolve(string selector, int pageCount, string sourceName)
        {
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));

            if (string.IsNullOrWhiteSpace(selector))
                throw new ArgumentException("A page selector must not be empty.", nameof(selector));

            if (pageCount <= 0)
                throw new PdfOperationException($"'{sourceName}' contains no pages.");

            var result = new List<int>();

            foreach (var rawItem in selector.Split(','))
            {
                var item = rawItem.Trim();

                if (item.Length == 0)
                    throw new ArgumentException($"Page selector '{selector}' contains an empty item.", nameof(selector));

                var exclude = item[0] == 'x' || item[0] == 'X';

                if (exclude)
                {
                    item = item.Substring(1).Trim();

                    if (item.Length == 0)
                        throw new ArgumentException($"Page selector '{selector}' contains an exclusion without a range.", nameof(selector));
                }

                var (range, parity) = SplitParity(item, selector);
                var pages = ExpandRange(range, pageCount, selector, sourceName);

                if (parity.HasValue)
                    pages = FilterParity(pages, parity.Value);

                if (exclude)
                    result.RemoveAll(page => pages.Contains(page));
                else
                    result.AddRange(pages);
            }

            return result;
        }

        private static (string Range, Parity? Parity) SplitParity(string item, string selector)
        {
            var separator = item.IndexOf(':');

            if (separator < 0)
                return (item, null);

            if (item.IndexOf(':', separator + 1) >= 0)
                throw new ArgumentException($"Page selector '{selector}' contains more than one filter.", nameof(selector));

            var range = item.Substring(0, separator).Trim();
            var filter = item.Substring(separator + 1).Trim();

            if (range.Length == 0)
                throw new ArgumentException($"Page selector '{selector}' contains a filter without a range.", nameof(selector));

            if (string.Equals(filter, "odd", StringComparison.OrdinalIgnoreCase))
                return (range, Parity.Odd);

            if (string.Equals(filter, "even", StringComparison.OrdinalIgnoreCase))
                return (range, Parity.Even);

            throw new ArgumentException($"Page selector '{selector}' uses an unknown filter '{filter}'. Use 'odd' or 'even'.", nameof(selector));
        }

        private static List<int> FilterParity(List<int> pages, Parity parity)
        {
            var filtered = new List<int>(pages.Count);

            foreach (var page in pages)
            {
                var isOdd = (page + 1) % 2 != 0;

                if (isOdd == (parity == Parity.Odd))
                    filtered.Add(page);
            }

            return filtered;
        }

        private static List<int> ExpandRange(string range, int pageCount, string selector, string sourceName)
        {
            var separator = range.IndexOf('-');

            if (separator < 0)
                return new List<int> { ParsePage(range, pageCount, selector, sourceName) };

            if (range.IndexOf('-', separator + 1) >= 0)
                throw new ArgumentException($"Page selector '{selector}' uses an invalid range '{range}'.", nameof(selector));

            var start = ParsePage(range.Substring(0, separator).Trim(), pageCount, selector, sourceName);
            var end = ParsePage(range.Substring(separator + 1).Trim(), pageCount, selector, sourceName);

            var step = start <= end ? 1 : -1;
            var pages = new List<int>(Math.Abs(end - start) + 1);

            for (var page = start;; page += step)
            {
                pages.Add(page);

                if (page == end)
                    break;
            }

            return pages;
        }

        private static int ParsePage(string token, int pageCount, string selector, string sourceName)
        {
            if (token.Length == 0)
                throw new ArgumentException($"Page selector '{selector}' contains an empty page number.", nameof(selector));

            int page;

            if (string.Equals(token, "z", StringComparison.OrdinalIgnoreCase))
            {
                page = pageCount;
            }
            else if (token[0] == 'r' || token[0] == 'R')
            {
                var offset = ParseNumber(token.Substring(1), selector);

                if (offset < 1)
                    throw new PdfOperationException($"Page selector '{selector}' references '{token}', but page offsets start at r1.");

                page = pageCount - offset + 1;

                if (page < 1)
                    throw new PdfOperationException($"Page selector '{selector}' references '{token}', but '{sourceName}' has {pageCount} pages.");
            }
            else
            {
                page = ParseNumber(token, selector);
            }

            if (page < 1 || page > pageCount)
                throw new PdfOperationException($"Page selector '{selector}' references page {page}, but '{sourceName}' has {pageCount} pages.");

            return page - 1;
        }

        private static int ParseNumber(string token, string selector)
        {
            if (token.Length == 0 || !int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                throw new ArgumentException($"Page selector '{selector}' contains an invalid page token '{token}'.", nameof(selector));

            return value;
        }
    }
}
