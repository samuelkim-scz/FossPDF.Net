using System;
using FossPDF.Drawing;
using FossPDF.Helpers;
using SkiaSharp;
using Svg.Skia.TypefaceProviders;

namespace FossPDF.Svg
{
    /// <summary>
    /// Resolves SVG <c>font-family</c> values through the fonts registered with FossPDF.
    /// Svg.Skia calls a provider twice for every text run: first with the family requested by the
    /// document and then with the family name of the resolved typeface. Returning the registered
    /// typeface for both names is what keeps fonts loaded from streams (which are not installed on
    /// the system) available to the SVG text renderer.
    /// </summary>
    internal sealed class FossPdfTypefaceProvider : ITypefaceProvider
    {
        private const string DefaultFamily = Fonts.Lato;

        public SKTypeface FromFamilyName(
            string familyName,
            SKFontStyleWeight fontWeight,
            SKFontStyleWidth fontWidth,
            SKFontStyleSlant fontStyle)
        {
            using var requestedStyle = new SKFontStyle(fontWeight, fontWidth, fontStyle);

            foreach (var family in EnumerateFamilies(familyName))
            {
                var candidate = ResolveGenericFamily(family);

                if (FontManager.TryResolveRegisteredTypeface(candidate, requestedStyle, out var typeface))
                    return typeface!;
            }

            // Svg.Skia treats a null result as "not provided" and continues with its own system
            // font lookup, which keeps installed fonts (Arial, Segoe UI, ...) working.
            return null!;
        }

        private static string[] EnumerateFamilies(string familyName)
        {
            if (string.IsNullOrWhiteSpace(familyName))
                return new[] { DefaultFamily };

            // Svg.Skia usually asks for one family at a time, but it also probes the raw,
            // comma-separated value. Both forms are accepted here.
            return familyName.Split(',');
        }

        private static string ResolveGenericFamily(string family)
        {
            var name = family.Trim().Trim('"', '\'');

            if (name.Length == 0)
                return DefaultFamily;

            return name.ToLowerInvariant() switch
            {
                "sans-serif" => DefaultFamily,
                "system-ui" => DefaultFamily,
                "ui-sans-serif" => DefaultFamily,
                "inherit" => DefaultFamily,
                "initial" => DefaultFamily,
                "unset" => DefaultFamily,
                "revert" => DefaultFamily,
                _ => name
            };
        }
    }
}
