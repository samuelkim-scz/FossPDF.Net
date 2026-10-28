# OpenType features and text decorations

[FP-103](https://github.com/samuelkim-scz/FossPDF.Net/issues/8) adds per-span OpenType feature toggles and richer underline/overline/strikethrough styling. The APIs are available in both core targets (netstandard2.1 and net9.0).

## Enable or disable an OpenType feature

~~~csharp
page.Content().Text(text =>
{
    text.Span("office").EnableFontFeature("liga");
    text.Span("office").DisableFontFeature("liga");
});
~~~

EnableFontFeature/DisableFontFeature are available on TextStyle and TextSpanDescriptor. The tag is the four-character OpenType feature name (`liga`, `kern`, `smcp`, ...); one- to four-character names are accepted and whitespace-only names are rejected. The latest call for the same tag wins. Feature settings are part of the inherited and global text style, so a fallback style and a page/default text style can carry their own settings.

Features are passed to HarfBuzz for shaping every span. The shaped glyph sequence is used for measurement, line breaking, drawing and font subsetting, and the text-style value participates in the shaping/font caches. Enabling and disabling `liga` therefore changes both the glyph sequence and the cached shaping result. Styles that do not set a feature inherit it from an enclosing style; a child can override the same tag without removing unrelated features.

## Decoration controls

~~~csharp
page.Content().Text(text =>
{
    text.Span("Dashed").Underline()
        .DecorationDashed()
        .DecorationThickness(1.5f)
        .DecorationColor("#c62828");
});
~~~

| Method | Behavior |
| --- | --- |
| Underline(bool = true) | Existing underline toggle. |
| Overline(bool = true) | Adds a line above the font ascent. |
| Strikethrough(bool = true) | Existing strike-through toggle. |
| DecorationColor(string) | Color for all decoration lines on the span. FontColor also sets this value; an explicit call overrides it. |
| DecorationThickness(float factor) | Positive multiplier for the font's decoration thickness. |
| DecorationSolid() | One continuous line. |
| DecorationDouble() | Two thin parallel lines. |
| DecorationDotted() | Square dots. |
| DecorationDashed() | Dashes with a gap. |
| DecorationWavy() | Segmented wave whose amplitude and period scale with the decoration thickness. |

Underline, overline and strike-through can be combined. The style applies to all enabled lines on the span. Superscript/subscript vertical offsets are included when positioning underline, overline and strike-through; mixed font sizes keep their own metrics. Wrapped spans draw a decoration on every line fragment. Decorations are painted rectangles sized to the measured glyph run, so they do not change text advance, wrapping, page breaks or font subsetting.

The dashed/dotted patterns use spacing derived from the effective thickness; custom dash/gap lengths are not exposed yet. Wavy output is assembled from small rectangles through the public canvas contract and is an approximation rather than a native curve.

## Interaction with existing styles

- Underline and Strikethrough calls keep their existing booleans. DecorationStyle/DecorationColor/DecorationThickness default to solid, the text color and a factor of 1.
- `FontColor` now updates the decoration color as well. Calling `DecorationColor` afterwards wins.
- OpenType features are independent of font family/weight/position. Fallback spans receive the feature settings through the normal style inheritance chain.
- Decoration properties do not affect measured text width or line height; enabling them cannot reflow text.

## Example and acceptance evidence

From Source:

~~~powershell
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --filter FullyQualifiedName~TextFeatureExamples
~~~

[TextFeatureExamples.cs](../../Source/FossPDF.Examples/TextFeatureExamples.cs) writes `font-features.pdf` and `decorations.pdf` under `text-feature-example/`.

TXT-103 is active in the [acceptance catalog](acceptance-fixtures.json). It shapes `office ffi AV 0123456789` with Lato and `liga=0`/`kern=0`, then renders a dashed `#cc0000` underline at thickness 1.5. The runner checks that liga enabled/disabled produces one/separate glyphs, kern changes the AV advance, decoration does not change shaping or text bounds, and the dashed decoration contains multiple painted runs with the requested color. TextFeatureTests covers style mutation, last-feature-wins, all five decoration styles, thickness scaling, wrapped lines and superscript/subscript spans. See [the Windows evidence report](evidence/fp-103-windows.md).
