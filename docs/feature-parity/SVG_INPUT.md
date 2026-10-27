# SVG input and preloading

This guide covers [FP-201](https://github.com/samuelkim-scz/FossPDF.Net/issues/15) and supersedes
the earlier example that used `Svg.Skia` directly. SVG input lives in the optional
**`FossPDF.Svg`** companion package (target framework `net9.0`), following
[ADR-0001](decisions/ADR-0001-native-backends.md): the core `FossPDF` package never references the
SVG parser.

## Package and dependency cohort

| Item | Value |
| --- | --- |
| Package | `FossPDF.Svg` (net9.0, `IsPackable`) |
| SVG adapter | Svg.Skia **4.9.1** (exact version range, MIT) |
| Transitive parser | Svg.Custom 4.9.1 (**MS-PL**, not MIT) |
| Skia cohort | SkiaSharp 3.119.4 / HarfBuzzSharp 8.3.1.5, as required by [NATIVE_DEPENDENCIES.md](NATIVE_DEPENDENCIES.md) |

`THIRD-PARTY-NOTICES.md` ships with the package, and the copied Svg.Skia (MIT) and Svg.Custom
(MS-PL) licence texts are packed under `licenses/`. Svg.Custom is the only MS-PL component in the
graph; it is never re-licensed or copied into FossPDF source.

## API surface

```csharp
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.Svg;

// Parse once. The instance owns the parsed picture and can be reused on any number of pages.
using var logo = SvgImage.FromFile("logo.svg");
using var badge = SvgImage.FromText("<svg …>…</svg>");
using var fromBytes = SvgImage.FromBytes(svgBytes);
using var fromStream = SvgImage.FromStream(stream);   // read from the current position, left open

logo.Size;                                            // intrinsic size in points

// Vector replay (default): SVG paths are replayed into the page canvas.
page.Content().Width(120).Svg(logo);
page.Content().Svg(logo, ImageScaling.FitHeight);

// Convenience overloads parse on the fly and leave the picture to the garbage collector,
// matching the .Image(byte[]) overloads. Prefer SvgImage when the same SVG repeats.
page.Content().Svg(svgText);
page.Content().Svg(svgBytes, ImageScaling.FitArea);
page.Content().Svg(stream);

// Explicit raster fallback, only for effects the vector path cannot preserve.
page.Content().Width(120).SvgRasterized(logo, new SvgRasterizationOptions
{
    Dpi = 144,
    Background = "#ffffff",
    Scaling = ImageScaling.FitWidth
});
```

Fit modes match the raster image APIs: `FitWidth` (default), `FitHeight`, `FitArea` (the largest
uniform scale that fits inside both axes) and `Resize` (stretch to the available box). SVG user
units are treated as points; one source pixel therefore occupies one point, as with images.

## Intrinsic size

An embedded SVG has no viewport to resolve percentages against, so every document must declare a
positive intrinsic size:

- a `viewBox` with positive width and height, or
- absolute `width` and `height` attributes (unitless or `px`/`pt`/`pc`/`mm`/`cm`/`in`).

`width="100%"`/`height="100%"` without a `viewBox` is rejected with `SvgParseException` rather
than silently rendering at Svg.Skia's normalized 1x1 size. `<svg>` without any size is rejected for
the same reason.

## Input policy

SVG input is treated as **untrusted data**. `SvgInputValidator` parses with `DtdProcessing.Prohibit`,
no `XmlResolver`, no entity expansion and a 32 MiB character budget, then rejects:

| Construct | Reason |
| --- | --- |
| `<!DOCTYPE>` / entity declarations | Prevents external entity resolution and entity expansion |
| Non-`<svg>` root or wrong namespace | Rejects HTML and other XML documents early |
| `<script>` elements | No script execution |
| External `href` / `src` | Only empty, `#fragment` and `data:` references are accepted |
| External `url(…)` | Only local `url(#id)` references are accepted |
| `@import`, `javascript:`, `expression(…)` | CSS-level external loading and script vectors |
| `xml-stylesheet` processing instruction | External stylesheets |

All rejected cases throw `SvgParseException`; malformed XML and parser failures are wrapped in the
same type. The exception never contains a stack-based retry or partial document. Inputs are read
fully into memory (from the stream's current position) so parsing cannot re-read the caller's
stream, and the caller's stream is never disposed — including on failure.

## Fonts

SVG `font-family` values resolve through FossPDF's font registry before Svg.Skia's system
fallback:

- Fonts registered with `FontManager.RegisterFont`/`RegisterFontWithCustomName` are available by
  their alias and by their internal family name.
- `sans-serif`, `system-ui`, `ui-sans-serif`, `inherit`, `initial`, `unset`, `revert` and a missing
  `font-family` resolve to the FossPDF default (`Lato`).
- `serif`, `monospace` and other families fall through to Svg.Skia's normal resolution, so a
  missing family degrades to the platform fallback instead of failing.

Resolution happens while the SVG model is built, so the registered typeface must exist before
`SvgImage.FromText`/`FromBytes`/`FromStream`/`FromFile` runs.

## Output

- **Vector (default).** Paths are replayed through the public `IContainer.Canvas` API inside a
  saved/restored canvas state and clipped to the destination box. PDF output contains path paint
  operators (`re`, `m`, `c`, …) and no image XObject.
- **Raster (`SvgRasterized`).** The picture is rendered to an RGBA surface at `Dpi / 72` times the
  layout size and embedded as an image XObject. `Background` defaults to transparent
  (`#00000000`); pass an opaque colour when the SVG does not paint its own background.

## Ownership and reuse

`SvgImage` owns the `SKSvg` document and its picture. It is safe to draw the same instance from
multiple pages and multiple threads; drawing serializes access only for the dispose check and
leaves the picture immutable. `Dispose` is idempotent and later draws throw
`ObjectDisposedException`. The convenience overloads that accept text/bytes/streams parse a new
image per call and leave it to the garbage collector, mirroring the raster image overloads.

## Framework scope

The companion package and its fixtures target `net9.0`. The parity fixture `IMG-201` declares
`"frameworks": ["net9.0"]`; the `net8.0` test run reports it as skipped and the catalog
consistency check accounts for the scope instead of requiring a runner that does not exist on that
framework.

## Verification

- `Source/FossPDF.UnitTests/SvgImageTests.cs` — parsing, sizes, stream ownership, concurrency,
  unsupported input, fonts, fit modes, vector/raster output.
- `Source/FossPDF.UnitTests/Parity/ParityAcceptanceTests.Svg.cs` — `IMG-201` acceptance on net9.0.
- `Source/FossPDF.Examples/SvgImageExample.cs` — runnable vector, raster and reuse examples.
- Evidence: [evidence/fp-201-windows.md](evidence/fp-201-windows.md).

```powershell
# From Source
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --filter "FullyQualifiedName~SvgImageTests"
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --filter "TestCategory=FeatureParity"
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --filter "FullyQualifiedName~SvgImageExamples"
```

## Limitations

- No animation, scripting, external resources, SVG fonts or XML stylesheets. These are rejected or
  disabled by design.
- CSS coverage comes from Svg.Custom; unsupported effects (filters, some blend modes) may need the
  explicit raster fallback.
- Text shaping inside SVG uses Svg.Skia's own text engine. It shares the resolved typefaces but is
  not the FossPDF text pipeline, so complex-script qualification from
  [TEXT_PARAGRAPH_LAYOUT.md](TEXT_PARAGRAPH_LAYOUT.md) and FP-109 does not extend to SVG text.
- The package is `net9.0`-only; `netstandard2.1`/`net8.0` consumers keep using the core package
  without SVG input.
