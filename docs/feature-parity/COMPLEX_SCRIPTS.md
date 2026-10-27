# Complex scripts, mixed direction and CJK line breaking

[FP-109 / issue #14](https://github.com/samuelkim-scz/FossPDF.Net/issues/14) qualifies and
completes the QuestPDF 2026.9.1 text-parity target for non-Latin scripts. The work is additive: the
existing shaping and fallback APIs are unchanged, and the new line-breaking behavior applies to
text that no existing rule could wrap.

The qualification produced the `TXT-109` fixture, the `ComplexScriptTests` corpus, the
`ComplexScriptExamples` samples and the [Windows evidence report](evidence/fp-109-windows.md).
Defects found while building the corpus were fixed; unsupported combinations are listed below and
asserted by the fixture so they cannot silently regress into claims.

## Supported behavior

| Area | Behavior |
| --- | --- |
| Shaping | HarfBuzz shapes each run at the style's font size with OpenType features preserved. Arabic contextual forms and lam-alef ligatures, Hebrew niqqud, Devanagari reordering and conjuncts, CJK forms and emoji ZWJ/VS sequences are produced by the font. |
| Logical order | HarfBuzz receives UTF-16 input, so glyph clusters are UTF-16 character offsets. Shaped runs are always addressed in logical order; line reconstruction maps glyph indices back to source characters. |
| Direction | `TextStyle.DirectionAuto()` (the default) lets each run resolve direction from its first strong character. `DirectionFromLeftToRight()` and `DirectionFromRightToLeft()` force the shaping direction of a run. `ContentDirection` on the text block controls the order in which runs and lines are placed. |
| Font fallback | `TextStyle.Fallback(...)` builds an explicit chain. Text is split into one run per covering font, per Unicode code point, without splitting surrogate pairs. The first font in the chain that `ContainsGlyph` covers the code point wins. |
| Missing glyphs | With `CheckIfAllTextGlyphsAreAvailable` enabled, an uncovered code point throws `DocumentDrawingException` naming the character, its code point and installed fonts that could cover it. With the check disabled, the primary font is used and missing glyphs may render as notdef. |
| Grapheme safety | Wrapping never separates glyphs of the same cluster: combining marks stay with their base character and emoji variation selectors, skin-tone modifiers and ZWJ sequences stay attached. |
| CJK wrapping | Text without spaces wraps between CJK characters at reviewed break opportunities instead of splitting at an arbitrary glyph. |

## CJK line breaking

CJK text has no spaces, and measurement alone produces breaks that violate Japanese/Chinese line
composition rules (kinsoku). `Drawing/CjkLineBreak.cs` implements the relevant subset of
[UAX #14](https://www.unicode.org/reports/tr14/):

- A break is allowed only when at least one side of the boundary is a CJK code point (Han,
  Hiragana, Katakana, Hangul, CJK punctuation, full-width forms, kana extensions and the
  supplementary ideographic planes).
- It is prohibited before characters that may not start a line: closing brackets and quotation
  marks (`)]}»”’`), CJK closing punctuation (`。、，．・：；？！」』】〕》〉`), small kana
  (`ぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮヵヶ`), iteration and prolongation marks
  (`ー〜～々〻ゝゞヽヾ`).
- It is prohibited after characters that may not end a line: opening brackets and quotation marks
  (`([{«“‘`), CJK opening punctuation (`「『【〔《〈`), full-width openings and currency prefixes
  (`¥￥＄$£€`).
- It is prohibited before combining marks, variation selectors, emoji modifiers, ZWJ/ZWNJ and
  after a no-break space.
- Glyphs sharing a cluster are never separated.

The backward search from the measured end position naturally moves the preceding character to the
next line when punctuation would otherwise start it. That is why
`あいうえ。」かきくけこ` breaks after `あいう` rather than after the closing punctuation, and why
`「あいうえお」` never leaves `「` at the end of a line.

This is a subset of UAX #14, not a full implementation: scripts outside the CJK ranges and the
complete pair table are out of scope. Latin and other space-separated scripts keep the existing
space-based wrapping.

## Example

```csharp
// Fallback chains are nested: each style owns the next option in the chain.
var emoji = TextStyle.Default.FontFamily("Noto Emoji");
var cjk = TextStyle.Default.FontFamily("Noto Sans JP").Fallback(emoji);
var arabic = TextStyle.Default.FontFamily("Noto Sans Arabic").Fallback(cjk);

var style = TextStyle.Default
    .FontFamily("Noto Sans")
    .FontSize(12)
    .DirectionAuto()
    .Fallback(arabic)
    .ApplyGlobalStyle();

document.Page(page =>
{
    page.Content().Text(text => text.Span("Invoice رقم 123 (USD)").Style(style));
});
```

Run the runnable samples with:

```powershell
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --filter FullyQualifiedName~ComplexScriptExamples
```

The examples write PDF and PNG artifacts to `complex-script-example/` under the test output
directory.

## Fixture fonts and licensing

The corpus does not rely on fonts installed on the machine. It uses fixed subsets of Noto fonts,
instantiated at weight 400 and width 100, registered under stable `FossPDF Test ...` family names:

| Family | Source | License |
| --- | --- | --- |
| Latin | Noto Sans | SIL OFL 1.1 |
| Arabic | Noto Sans Arabic | SIL OFL 1.1 |
| Hebrew | Noto Sans Hebrew | SIL OFL 1.1 |
| Devanagari | Noto Sans Devanagari | SIL OFL 1.1 |
| CJK | Noto Sans JP | SIL OFL 1.1 |
| Emoji | Noto Emoji | SIL OFL 1.1 |

Sources, subsetting commands and the retained license notices are documented in
`Source/FossPDF.UnitTests/Resources/ComplexScripts/README.md`. The fonts are embedded in the test
and example assemblies only; they are not part of the published package. The emoji subset is
monochrome because color tables (COLR/CPAL) are dropped to keep the fixture small.

## Known limitations

These combinations are declared in the `TXT-109` fixture's `unsupported` list and are not claimed
as parity:

- **No Unicode bidi reordering (UAX #9).** Direction is resolved per run by HarfBuzz, and
  `ContentDirection` places runs in logical order. Nested directional levels inside a single run
  (for example an English phrase embedded in an Arabic sentence with matching parentheses) are not
  reordered as the bidi algorithm would.
- **No automatic system font fallback.** Only the explicit `TextStyle.Fallback` chain is consulted.
  Add the fonts a document needs to the chain, or enable `CheckIfAllTextGlyphsAreAvailable` to fail
  fast.
- **Monochrome emoji only.** The licensed fixture font has no color tables, so the corpus verifies
  shaping, not color glyph rendering.
- **CJK breaks are a UAX #14 subset** limited to the rules listed above.
