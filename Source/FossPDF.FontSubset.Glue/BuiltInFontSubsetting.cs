using FossPDF.Drawing;

namespace FossPDF.FontSubset.Glue;

public static class BuiltInFontSubsetting
{
    public static Action<DocumentSpecificFontManager, IEnumerable<FontToBeSubset>> Callback =>
        (fontManager, subsets) =>
        {
            var tasks = subsets.Select(fontToBeSubset => Task.Run(() =>
            {
                var subset = fontToBeSubset.Glyphs;
                var builder = new FontSubsetBuilder();
                builder.AddGlyphs(subset);
                builder.SetFont(fontToBeSubset.ShaperFont);

                return builder.Build();
            }));
            var newFonts = Task.WhenAll(tasks).GetAwaiter().GetResult();

            fontManager.ClearCacheReadyForSubsets();

            foreach (var newFont in newFonts)
            {
                using var ms = new MemoryStream(newFont);
                fontManager.RegisterFont(ms);
            }
        };
}
