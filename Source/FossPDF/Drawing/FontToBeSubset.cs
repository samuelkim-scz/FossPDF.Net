using System.Collections.Generic;
using HarfBuzzSharp;
using SkiaSharp;

namespace FossPDF.Drawing;

public record FontToBeSubset
{
    // DocumentGenerator supplies the font objects before invoking the subset callback.
    public SKTypeface Typeface { get; init; } = null!;
    public Font ShaperFont { get; init; } = null!;
    public HashSet<uint> Glyphs { get; init; } = new();
}
