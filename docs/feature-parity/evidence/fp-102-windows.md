# FP-102 Windows validation

Recorded October 26, 2026, for [FP-102 / issue #7](https://github.com/samuelkim-scz/FossPDF.Net/issues/7), on `feature/fp-102-text-line-clamping` based on main commit `6579f28`.

Environment: Windows x64, .NET SDK 9.0.305, .NET 9.0.21 runtime, SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.5.

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Zero warnings, zero errors |
| Complete .NET 8 suite | 263 passed, 0 failed, 24 planned/skipped |
| Complete .NET 9 suite | 264 passed, 0 failed, 24 planned/skipped |
| Parity category | 17 passed, 0 failed, 24 planned/skipped on both frameworks |
| `TXT-102` fixture | Active and passing |
| New line-clamp unit tests | 7 passed per framework |
| Clamp example | 1 passed |

## Verified behavior

- A long paragraph at Lato 12 on an 80-point-wide content area renders at most two lines.
- The final visible line contains the custom ellipsis `[more]` and its total width stays within the
  80-point content width.
- The default ellipsis is `…`; an empty ellipsis truncates without a replacement; a null ellipsis
  uses the default.
- A short paragraph that fits within the limit renders no ellipsis.
- `ClampLines(0)` and negative limits throw `ArgumentOutOfRangeException`.
- The line budget is counted across pages; a paragraph that reaches the limit on a later page gets
  the ellipsis on that page and no further content.
- Rich-text span styling is preserved on the visible content and the ellipsis inherits the last
  visible span's style.
- Ellipsis glyphs are added to the font-subsetting input for the styles used by the clamped block.
- Rendering the same clamped document repeatedly produces the same result.

Artifacts are written beneath `parity-artifacts/TXT-102/`: `clamped.pdf`, `clamped.png` and
`unclamped.png`. The example writes `text-clamp-example/document.pdf` and `page-1.png`.

## Compatibility note

The text stack now treats a missing `TextStyle.FontPosition` as `Normal` when resolving font scale
and glyph offset. Previously an unset value could throw in those paths. The change only affects
styles that did not specify a position; explicit normal/subscript/superscript styles are unchanged.

## Reproduce

From `Source`:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~TextClampExamples
```

## Boundaries

- Grapheme-cluster, bidi and complex-script qualification remains with FP-109; line clamping uses the
  existing shaping and wrapping boundaries rather than a separate Unicode segmentation path.
- The ellipsis is measured with the last visible span's style. Extremely narrow widths may clip the
  ellipsis; ordinary widths reserve its full measured width.
- Empty text is unchanged. A non-positive line limit is rejected during composition.
- No new dependencies or target frameworks are introduced; the API is in the core
  `netstandard2.1`/`net9.0` package.
