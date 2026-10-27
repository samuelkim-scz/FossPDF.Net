using PdfSharp.Fonts;

namespace FossPDF.NativePrototypes;

internal sealed class LatoFontResolver : IFontResolver
{
    private readonly byte[] _regular;
    private readonly byte[] _bold;

    public LatoFontResolver(string regularPath, string boldPath)
    {
        _regular = File.ReadAllBytes(regularPath);
        _bold = File.ReadAllBytes(boldPath);
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        return new FontResolverInfo(isBold ? "Lato-Bold" : "Lato-Regular");
    }

    public byte[] GetFont(string faceName)
    {
        return faceName == "Lato-Bold" ? _bold : _regular;
    }
}
