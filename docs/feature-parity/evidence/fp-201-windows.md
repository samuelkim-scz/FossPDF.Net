# FP-201 Windows validation

Recorded October 27, 2026, for [FP-201 / issue #15](https://github.com/samuelkim-scz/FossPDF.Net/issues/15),
based on the `feature/fp-109-complex-scripts` stack tip plus the `feature/fp-201-svg-input` branch.

Environment: Windows 11 Pro 10.0.26200, win-x64, .NET SDK 9.0.305, .NET 8.0 and .NET 9.0 runtimes,
SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.5, Svg.Skia 4.9.1 with Svg.Custom 4.9.1 (MS-PL). The
reviewed cohort is the one pinned by [NATIVE_DEPENDENCIES.md](../NATIVE_DEPENDENCIES.md).

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Zero warnings, zero errors |
| Complete .NET 8 suite | 321 passed, 0 failed, 22 planned/skipped |
| Complete .NET 9 suite | 358 passed, 0 failed, 21 planned/skipped |
| New `SvgImageTests` (net9.0 only) | 35 passed |
| Parity category, .NET 8 | 19 passed, 0 failed, 22 skipped |
| Parity category, .NET 9 | 20 passed, 0 failed, 21 skipped |
| Shared fixture | `IMG-201` active and passing on net9.0; framework-scoped skip on net8.0 |
| .NET 9 SVG examples | 2 passed |
| Quality tool, `--mode all` | visual and performance passed; conformance skipped (validators unavailable) |

The activation of `IMG-201` reduces the net9.0 planned/skipped parity count from 22 to 21 and
keeps `Catalog_AccountsForEveryFeatureWorkItemAndExecutableFixture` green. The net8.0 run keeps 22
skips because the fixture declares `frameworks: ["net9.0"]` and its runner is compiled out. The
quality tool's visual ink counts (`report` 108288 px, `text` 37067 px, `graphics` 104260 px, `image`
67815 px) are unchanged from the FP-104/FP-109 baseline: the core layout and drawing stack is
untouched.

The packaged `FossPDF.Svg.2026.5.1.nupkg` contains `lib/net9.0/FossPDF.Svg.dll`, the XML docs,
`PackageReadme.md`, `THIRD-PARTY-NOTICES.md` and `licenses/Svg.Skia-4.9.1-LICENSE.txt` plus
`licenses/Svg.Custom-4.9.1-LICENSE.txt`. Its nuspec declares `FossPDF 2026.5.1` and the exact
`Svg.Skia [4.9.1]` range.

## Implemented behavior

- `FossPDF.Svg` is a new optional `net9.0` companion package. Core `FossPDF` references it only
  through `InternalsVisibleTo`, and the companion is never referenced by the core package.
- `SvgImage.FromText/FromBytes/FromStream/FromFile` validate and parse once; `Size` reports the
  intrinsic size. The parsed picture is reused across pages and threads.
- Fluent `Svg(...)` overloads accept a preloaded image or text/bytes/stream and support
  `FitWidth`, `FitHeight`, `FitArea` and `Resize`. The vector path replays through the public
  `IContainer.Canvas` API with saved/restored state and a destination clip.
- `SvgRasterized(...)` is the explicit opt-in raster fallback; it renders at `Dpi / 72` scale with
  a configurable background before drawing.
- `SvgInputValidator` rejects DTDs, non-SVG roots, scripts, external `href`/`src`/`url(...)`,
  `@import`, `javascript:`, `expression(...)`, `xml-stylesheet` and documents without a positive
  `viewBox` or absolute `width`/`height`. Rejections throw `SvgParseException`.
- `FossPdfTypefaceProvider` resolves SVG font families through FossPDF's registered fonts, so
  `RegisterFont`/`RegisterFontWithCustomName` fonts render inside SVG and the default family is
  Lato.
- Framework-aware parity: `ParityFixture` now carries an optional `frameworks` list;
  `Fixtures()` and the catalog consistency check honour the current framework, and
  `ParityAcceptanceTests` is partial so the net9.0-only `IMG-201` runner lives in its own file.

### Deliberate constraints

- External resource loading stays disabled, so a document that depends on remote images, styles or
  fonts is rejected instead of silently rendering differently.
- Percent-only root sizes are rejected; an embedded SVG needs a `viewBox` to have an intrinsic
  size. Svg.Skia would otherwise normalize the document to 1x1.
- SVG text uses Svg.Skia's own text engine. It shares resolved typefaces but is not the FossPDF
  shaping pipeline, so the FP-109 complex-script qualification does not extend to SVG text.
- Animation, scripting, SVG fonts and XML stylesheets are out of scope by design.

## Fixture evidence

`IMG-201` uses a 160x80 page with no margin and an 80x40 viewBox document split into a red and a
blue half. The runner asserts:

| Check | Evidence |
| --- | --- |
| Intrinsic size | `SvgImage.Size` equals the declared 80x40. |
| Source equality | Text, byte-array and stream inputs produce identical PNG pages; the stream stays open and is consumed from its current position. |
| Fit modes | `FitWidth` (300x200 → 300x150), `FitHeight` (500x200 → 400x200), `FitArea` (300x200 → 300x150) and `Resize` (300x200 → 300x200) match exact measurement results. |
| Preloaded reuse | The same instance renders three pages with byte-identical PNGs; red at the left quarter and blue at the right quarter. |
| Vector output | The PDF has no `/Subtype /Image`; decoded content streams contain path `re` and fill `rg` operators. |
| Raster fallback | A 144-DPI raster of the 160x80 page embeds `/Width 320` and `/Height 160`; the rendered page keeps the red/blue halves. |
| Rejection | Malformed XML, `<script>`, external `href`, missing intrinsic size and percent-only size all throw `SvgParseException`; the rejected stream remains readable. |
| Ownership | Dispose is idempotent and a later `Draw` throws `ObjectDisposedException`. |

Artifacts (`scenario-*/document.pdf`, `scenario-*/page-*.png`) are written under
`bin/Release/net9.0/parity-artifacts/IMG-201/` and attached to the test result. They are generated
output and are not committed.

## Example coverage

`SvgImageExamples` (net9.0) renders two pages and writes PDF/PNG pairs under `svg-example/` in the
example test output directory:

- `PreloadedSvgImageIsReusedAcrossThePage` draws the bundled `pdf-icon.svg` three times in a row
  and once at a larger size from one `SvgImage`, and asserts the PDF contains no image XObject.
- `InlineSvgSupportsVectorOutputAndExplicitRasterFallback` draws the same inline SVG as vectors,
  as a 144-DPI raster fallback and from a byte array, and asserts the PDF embeds an image.

## Limits

- Windows x64 only in this report; CI covers Linux and macOS for the test suite.
- The SVG parser's CSS, filter and text support is bounded by Svg.Skia/Svg.Custom 4.9.1. Effects
  outside that model need `SvgRasterized`, which trades vector output for fidelity.
- Inputs are capped at 32 MiB (bytes and XML characters) and parsed into memory.
- No conformance validator (qpdf, veraPDF) is involved or required: this work item produces
  ordinary PDF content streams with vector paths or an embedded image.

## Reproduce

From `Source`:

~~~powershell
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~SvgImageTests
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~SvgImageExamples
dotnet run --project FossPDF.Quality/FossPDF.Quality.csproj -c Release -- --mode all --output ../artifacts/quality
~~~
