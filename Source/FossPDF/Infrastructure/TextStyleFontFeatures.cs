using System;
using System.Collections.Generic;
using System.Linq;

namespace FossPDF.Infrastructure
{
    internal readonly struct TextStyleFontFeature : IEquatable<TextStyleFontFeature>
    {
        public string Name { get; }
        public bool Enabled { get; }

        public TextStyleFontFeature(string name, bool enabled)
        {
            Name = name;
            Enabled = enabled;
        }

        public bool Equals(TextStyleFontFeature other) =>
            string.Equals(Name, other.Name, StringComparison.Ordinal) && Enabled == other.Enabled;

        public override bool Equals(object? obj) =>
            obj is TextStyleFontFeature other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Name?.GetHashCode() ?? 0) * 397) ^ Enabled.GetHashCode();
            }
        }

        public override string ToString() => $"{Name}={(Enabled ? "1" : "0")}";
    }

    /// <summary>
    /// Immutable value-equal feature set so TextStyle records can be used as font/shaping cache keys.
    /// Earlier entries take precedence for duplicate OpenType tags.
    /// </summary>
    internal sealed class TextStyleFontFeatures : IEquatable<TextStyleFontFeatures>
    {
        public static TextStyleFontFeatures Empty { get; } =
            new TextStyleFontFeatures(Array.Empty<TextStyleFontFeature>());

        public IReadOnlyList<TextStyleFontFeature> Items { get; }

        private TextStyleFontFeatures(IReadOnlyList<TextStyleFontFeature> items)
        {
            Items = items;
        }

        public static TextStyleFontFeatures From(string name, bool enabled) =>
            new TextStyleFontFeatures(new[] { new TextStyleFontFeature(name, enabled) });

        public static TextStyleFontFeatures Merge(
            IEnumerable<TextStyleFontFeature> preferred,
            IEnumerable<TextStyleFontFeature> fallback)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<TextStyleFontFeature>();

            foreach (var feature in preferred.Concat(fallback))
            {
                if (names.Add(feature.Name))
                    result.Add(feature);
            }

            return new TextStyleFontFeatures(result);
        }

        public bool Equals(TextStyleFontFeatures? other) =>
            other != null && Items.SequenceEqual(other.Items);

        public override bool Equals(object? obj) =>
            obj is TextStyleFontFeatures other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                foreach (var feature in Items)
                    hash = hash * 31 + feature.GetHashCode();

                return hash;
            }
        }
    }
}
