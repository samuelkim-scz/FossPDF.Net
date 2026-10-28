# FP-109 Windows validation

Recorded October 27, 2026, for [FP-109 / issue #14](https://github.com/samuelkim-scz/FossPDF.Net/issues/14),
based on the `feature/fp-104-multi-column` stack tip plus the `feature/fp-109-complex-scripts`
branch.

Environment: Windows 11 Pro 10.0.26200, win-x64, 12th Gen Intel Core i7-12700T, .NET SDK 9.0.305,
.NET 8.0.22 and .NET 9.0.9 runtimes, SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.5. The corpus uses
embedded fixed subsets of Noto Sans, Noto Sans Arabic, Noto Sans Hebrew, Noto Sans Devanagari, Noto
Sans JP and Noto Emoji (SIL OFL 1.1); no system font is consulted, so results do not depend on the
installed fonts. No package or native dependency changed.

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Zero warnings, zero errors |
| Complete .NET 8 suite | 321 passed, 0 failed, 22 planned/skipped |
| Complete .NET 9 suite | 322 passed, 0 failed, 22 planned/skipped |
| New `ComplexScriptTests` per framework | 23 passed |
| Parity category per framework | 19 passed, 0 failed, 22 planned/skipped |
| Shared fixture | `TXT-109` active and passing |
| .NET 9 complex-script examples | 3 passed |
| Quality tool, `--mode all` | visual and performance passed; conformance skipped (validators unavailable) |

The activation of `TXT-109` reduced the planned/skipped parity count from 23 to 22 and keeps
`Catalog_AccountsForEveryFeatureWorkItemAndExecutableFixture` green. The quality tool's visual ink
counts (`report` 108288 px, `text` 37067 px, `graphics` 104260 px, `image` 67815 px) are unchanged
from the FP-104 baseline, so no visual baseline was re-measured: existing scenarios do not use the
new line-breaking path and the rendering stack is unchanged.

## Implemented behavior

- HarfBuzz now receives UTF-16 input, so each shaped glyph carries the UTF-16 offset of the source
  cluster that produced it. Line breaking can inspect the original characters instead of inferring
  them from glyph ids.
- `Drawing/CjkLineBreak.cs` implements the relevant UAX #14 subset: a break is allowed only when
  either side is a CJK code point, and is prohibited before closing punctuation, small kana and
  iteration marks, and after opening punctuation and currency prefixes. Combining marks,
  variation selectors, emoji modifiers and ZWJ/ZWNJ also prohibit a break.
- `TextBlockSpan.WrapText` uses the new rule for left-to-right text without spaces. The backward
  search pushes the preceding character to the next line when punctuation would otherwise start
  it. Right-to-left text keeps the existing behavior.
- Safe-mode wrapping extends to the end of the current grapheme cluster, so combining marks and
  emoji sequences are never split across lines.
- Space detection uses the source character, not the glyph id. Noto Emoji maps U+FE0F (variation
  selector) to the space glyph, which previously caused the wrapper to consume it as trailing
  whitespace.

### Defects reproduced and fixed

| Observation | Fix |
| --- | --- |
| CJK text had no break opportunities (`TODO` in `TextBlockSpan.WrapText`): words were chopped mid-glyph only in safe mode, and closing punctuation could start a line. | UAX #14 subset plus review-driven fixture expectations. |
| Emoji variation selectors were classified as spaces because the font maps them to the space glyph; the wrapper skipped them when advancing to the next line. | Source-character space test in `TextShaper`, backed by the UTF-16 cluster offset. |
| Safe-mode wrapping could separate a combining mark or a ZWJ sequence from its base character. | Forward cluster extension in `WrapText`; asserted by the combining-mark and emoji corpus tests. |
| Fallback and line reconstruction used glyph indices as character indices, which is wrong for surrogate pairs and ligatures. | `GetCharacterIndex`/`GetGlyphCount` map glyph indices to UTF-16 source offsets for tests and line reconstruction. |

### Deliberately not fixed

These are declared in the fixture's `unsupported` list and documented in
[COMPLEX_SCRIPTS.md](../COMPLEX_SCRIPTS.md). They are not parity claims:

- Unicode bidi reordering (UAX #9 levels) across style runs. Direction is resolved per run by
  HarfBuzz and runs are placed in logical order according to `ContentDirection`.
- Automatic system font fallback. Only the explicit `TextStyle.Fallback` chain is consulted.
- Colour emoji. The licensed fixture font is monochrome.

## Fixture evidence

`TXT-109` uses 12-point text and the following checks. The runner executes with
`CheckIfAllTextGlyphsAreAvailable` enabled, so every supported glyph is verified to resolve.

| Scenario | Check |
| --- | --- |
| Three mixed samples, each in LTR and RTL paragraph direction | Font fallback preserves the logical character sequence; each run's shaped clusters are non-decreasing in UTF-16 order; every line contains text and appears in source order; drawing produces ink; runs advance rightwards for LTR and leftwards for RTL; repeated generation yields identical run count and positions within 0.01 point. |
| Font policy | Each configured family resolves and contains a representative code point for its script (U+0065, U+0627, U+05D0, U+0915, U+3042, U+1F600). |
| CJK break expectation | `あいうえおかきくけこさしすせそ` at 60 points produces exactly `あいうえお`, `かきくけこ`, `さしすせそ`. |
| CJK corpus | `あいうえ。」かきくけこ` and `「あいうえお」かきくけこ` never start a continuation line with `、。，．・：；？！）」』】〕` and never end a non-final line with `（「『【〔`. |
| Combining corpus | Decomposed `é` sequences at 30 points keep every mark attached and remove no character. |
| Script corpus | Arabic, Hebrew, Devanagari, CJK, emoji and combining samples shape with at least one glyph, no notdef glyph and a positive width. |
| Unsupported list | The fixture declares exactly three unsupported combinations; a Latin-only style with emoji throws `DocumentDrawingException`. |

The runner uses the same licensed subset fonts as `ComplexScriptTests` and the examples, and
writes no text goldens. The examples emit PDFs and PNGs beneath `complex-script-example/` in the
example test output directory.

## Example coverage

`ComplexScriptExamples` renders three pages at 72 DPI:

- `complex-script-mixed`: six lines covering Latin/Arabic, Arabic/Latin, Hebrew, Devanagari, CJK
  and emoji in one page; ink stays inside the 16-point margin; repeated generation produces the
  same ink pixel count.
- `complex-script-cjk`: the 14-character CJK sample at a declared 72-point width with line height
  1.8 wraps across at least three ink bands, and no ink exceeds the declared width.
- `complex-script-corpus`: Arabic diacritics, Hebrew niqqud, Devanagari conjuncts, CJK
  punctuation, a skin-tone emoji and a decomposed combining sequence all draw ink.

## Limits

- The fixture and examples cover a fixed corpus; they do not enumerate every script or every
  UAX #14 pair. Scripts outside the corpus rely on the same shaping and fallback code.
- Bidi levels, automatic fallback and colour emoji are out of scope, as listed above.
- CJK line breaking targets the reviewed punctuation and kana rules; it is not a complete UAX #14
  implementation.
- The evidence is Windows x64 only. CI covers Linux and macOS for the test suite.
- Conformance validators (qpdf, veraPDF) are unavailable here and are not needed by this work
  item.

## Reproduce

From `Source`:

~~~powershell
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~ComplexScriptTests
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~ComplexScriptExamples
dotnet run --project FossPDF.Quality/FossPDF.Quality.csproj -c Release -- --mode all --output ../artifacts/quality
~~~
