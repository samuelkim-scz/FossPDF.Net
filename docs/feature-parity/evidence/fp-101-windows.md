# FP-101 Windows validation

Recorded October 27, 2026, for [FP-101 / issue #6](https://github.com/samuelkim-scz/FossPDF.Net/issues/6), based on main commit `6579f28` plus the `feature/fp-101-paragraph-style` branch.

Environment: Windows 11 Pro 10.0.26200, win-x64, .NET SDK 9.0.305, .NET 8.0.22 and .NET 9.0.9 runtimes, SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.5, bundled Lato fonts. No package or native dependency changed.

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Zero warnings, zero errors |
| Complete .NET 8 suite | 277 passed, 0 failed, 24 planned/skipped |
| Complete .NET 9 suite | 278 passed, 0 failed, 24 planned/skipped |
| New `TextParagraphLayoutTests` + `TextWordSpacingTests` per framework | 21 passed |
| Parity category per framework | 17 passed, 0 failed, 24 planned/skipped |
| Shared paragraph fixture | `TXT-101` active and passing (5 scenarios) |
| .NET 9 paragraph examples | 2 passed |
| Quality tool, `--mode all` | visual and performance passed; conformance skipped (validators unavailable) |

## Implemented behavior

- `TextHorizontalAlignment` adds `Start`, `End` and `Justify` alongside the physical `Left`, `Center`
  and `Right` values. `Start`/`End` resolve against the paragraph's `ContentDirection`.
- `TextDescriptor.AlignStart()`, `AlignEnd()`, `Justify()` and
  `ParagraphFirstLineIndentation(float, Unit = Unit.Point)` configure paragraphs. Negative,
  `NaN` and infinite indentation is rejected.
- `WordSpacing(float)` on `TextStyle`, `TextSpanDescriptor` and the style extensions adds
  `value x font size` to every space glyph. It affects measurement, wrapping, drawing, links,
  decorations and backgrounds, and follows inheritance, fallback and default resolution.
- Each line is laid out from one origin. Justification distributes
  `(box width - content width) / space count` across the word gaps of every line except the final
  line of a paragraph; space-free lines and final lines resolve to the logical start.
- The first line of each paragraph is indented from the logical start edge. Wrapped lines and a
  paragraph that continues on the next page are not re-indented.

### Defects fixed along the way

- Multi-span centered and right-aligned lines previously offset every span independently, so spans
  could overlap or extend past the content edge. A right-aligned multi-span footer in the quality
  `report` scenario reached x = 592 with a content edge of 555; it now ends at the edge.
- `TextShaper.PositionText` normalized right-to-left glyph positions by slicing the visual (reversed)
  glyph array with logical indices, which could push a continuation line outside its content area.
  It now normalizes from the logical glyph sequence.
- Leading-space trimming compared a UTF-16 space code with a glyph id; it now uses the shaped glyph's
  space flag.

## Fixture evidence

`TXT-101` renders five scenarios on 200x160 pages at 72 DPI, emitting PDFs and PNGs beneath
`parity-artifacts/TXT-101/`:

| Scenario | Check |
| --- | --- |
| `ltr_justify` | 12-point first-line indent; continuation lines at the margin; non-final lines fill the box exactly; final line not stretched; pen origins and raster ink agree. |
| `rtl_start` | Right-to-left content aligned to logical start ends at the right content edge; raster ink matches. |
| `narrow` | Half-width text wraps into more lines, every line stays inside the narrower box, and pen origins are zero. |
| `word_spacing` | Seven gaps gain exactly 7 x 2 x 12 = 168 points, so the text wraps earlier than at default spacing. |
| `multipage` | At least two pages; page 1 first line starts at margin + indent, page 2 starts at the margin. |

Layout geometry is asserted exactly (0.01 point) from measured lines and resolved alignment.
Horizontal raster ink is derived from measured glyph bearings and rendered width; vertical ink uses
the Skia text-blob bounds. The tolerance is 2.5 pixels at 72 DPI, covering conservative descender
bounds and antialiasing fringes. No text golden images are used, and the fixture makes no bidi,
shaping or Unicode-extraction conformance claim.

## Quality baseline change

FP-101 changes rendered alignment, so `baselines/visual-baselines.json` was re-measured on `win-x64`
and reviewed:

| Scenario | Before | After |
| --- | --- | --- |
| `report` | ink 108263 px, bounds (39,40,554,757) | ink 108288 px, bounds (40,40,516,757) |
| `text` | ink 37072 px, bounds (39,48,512,437) | ink 37067 px, bounds (40,48,511,437) |
| `image` | ink 67799 px, bounds (39,46,512,273) | ink 67815 px, bounds (40,46,511,273) |
| `graphics` | gray 53635 px | gray 53634 px |

The corrected right-aligned footer explains the horizontal change; the `report` PNG was visually
inspected before accepting it. Global tolerances, page counts, page dimensions and font hashes are
unchanged.

The `linux-x64` and `osx-arm64` overrides were updated to the same layout-derived ink bounds only.
Their ink and colour counts remain the October 26 platform measurements and were **not**
re-measured here. The quality CI job must record a fresh run on each platform and review it; until
then those overrides are layout references, not fresh platform evidence.

## Reproduce

From `Source`:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~ParagraphLayoutExamples
dotnet run --project FossPDF.Quality/FossPDF.Quality.csproj -c Release -- --mode all --output ../artifacts/quality
```

Local TRX logs, PDFs and PNGs are ignored artifacts; CI uploads platform-specific parity and quality
evidence. The remaining 24 planned feature contracts stay skipped.

## Limitations

- Justification expands word gaps only. There is no glyph stretching, automatic hyphenation or CJK
  inter-character justification; those belong to FP-109.
- The final line of a paragraph is never justified.
- Alignment now uses the line-box origin instead of shifting by the first glyph's left side bearing,
  so single-span text ink can move within roughly one point of the previous output.
- Visual evidence is Windows x64 only. Linux and macOS quality baselines need the CI re-measurement
  described above.
- qpdf and verapdf are not installed on this machine, so the conformance mode recorded its skip
  instead of a structural or profile report.
