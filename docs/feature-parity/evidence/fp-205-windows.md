# FP-205 Windows validation

Recorded October 27, 2026, for [FP-205 / issue #19](https://github.com/samuelkim-scz/FossPDF.Net/issues/19),
based on the `feature/fp-204-container-styling` stack tip plus the
`feature/fp-205-line-styles-shadows` branch.

Environment: Windows 11 Pro 10.0.26200, win-x64, .NET SDK 9.0.305, .NET 8.0 and .NET 9.0 runtimes,
SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.5. Fonts are the 10 embedded Lato faces.

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Zero warnings, zero errors |
| Complete .NET 8 suite | 388 passed, 0 failed, 20 skipped |
| Complete .NET 9 suite | 425 passed, 0 failed, 19 skipped |
| New `LineAndShadowTests` | 38 passed on both frameworks |
| Parity category, .NET 8 | 21 passed, 0 failed, 20 skipped |
| Parity category, .NET 9 | 22 passed, 0 failed, 19 skipped |
| New fixture | `GFX-205` active and passing on both frameworks |
| `LineAndShadowExamples` (.NET 9) | 4 passed |
| Quality tool, `--mode all` | visual and performance passed; conformance skipped (validators unavailable) |

Activating `GFX-205` reduces the planned/skipped parity count from 20 to 19 on .NET 9 and from 21
to 20 on .NET 8 (the .NET 8 skip includes the net9.0-scoped `IMG-201`). The catalog consistency
test `Catalog_AccountsForEveryFeatureWorkItemAndExecutableFixture` stays green with 17 active
fixtures. The quality tool's visual ink counts (`report` 108288 px, `text` 37067 px, `graphics`
104260 px, `image` 67815 px) are unchanged from the FP-201/FP-204 baselines: solid lines still
take the legacy rectangle path, so existing output is identical.

## Implemented behavior

- `LineGradient(params string[] colors)` accepts two or more validated colors and distributes the
  stops evenly from the line start to its end, for horizontal and vertical lines. The paint is an
  `SKShader.CreateLinearGradient`, which becomes a PDF shading and an SVG `<linearGradient>`.
- `LineDashPattern(float[] pattern, Unit)` and `LineDashPattern(float[] pattern, float phase, Unit)`
  accept dash/gap pairs, convert both lengths and the phase from the requested unit, and reject
  null, empty, odd-length, negative, non-finite, all-zero patterns and non-finite phases. Negative
  and oversized phases wrap modulo the pattern length.
- Dashes are rendered as filled quads along the line instead of an `SKPathEffect`, because Skia's
  SVG backend drops path effects. This keeps dashes vector in PDF (`l` operators) and SVG
  (`<path>`), makes dash lengths exactly the requested points, and makes the pattern follow canvas
  transforms (a 2x scale doubles dash, gap and phase in device space).
- `BoxShadowStyle` exposes `OffsetX`, `OffsetY`, `Spread`, `Blur` and `Color`, validates finite
  values and a non-negative blur, and is attached with `Shadow(...)` to the same decoration used by
  the FP-204 container styles. The shadow is painted before the background, border and content;
  it follows the corner radii and expands each radius by `Spread`, clamped at zero. Painting never
  changes layout.
- Blur uses a Gaussian mask with sigma = `Blur / 2`. PDF output cannot express a blurred vector
  shape through Skia's PDF backend, so a blurred shadow is rasterized at
  `DocumentMetadata.RasterDpi` and embedded as an image; SVG has no mask-filter support, so the
  fallback is a 72-DPI bitmap embedded as `<image>`. Sharp shadows, gradient lines and dashes never
  rasterize.
- `ICanvas.DrawLine`/`DrawShadow` are implemented by `SkiaCanvasBase`, `SvgCanvas` (the blur
  fallback), `FreeCanvas`, `ProxyCanvas`, `MockCanvas` and `OperationRecordingCanvas`.

### Deliberate constraints

- One shadow per container; inner and stacked shadows are not provided.
- Dash quads use butt ends; stroke caps and joins are not configurable.
- The SVG blurred-shadow fallback is a 72-DPI raster image and does not scale losslessly when the
  SVG is zoomed. The PDF fallback follows `DocumentSettings.ImageRasterDpi`, so raising it
  increases resolution and file size.
- Shadows paint outside layout bounds; an ancestor `Clip()` can crop them.
- Gradient stops interpolate unpremultiplied in sRGB, like the FP-204 container gradients. Use a
  transparent tint of the target color for a pure opacity fade.

## Fixture evidence

`GFX-205` renders a 160x120 page at 288 DPI (one pixel is 0.25 point) with a margin-free content
area. The catalog declares a 2-point line from (10,20) to (110,20) with a 6/3 dash and phase 2, a
red-to-blue gradient, and a 60x40 box at (30,50) with an 8-point radius, offset (3,4), spread 2,
blur 5 and color `#80000000`.

The runner asserts:

- the dash runs match an independent `ComputeDashIntervals` implementation within 0.5 point per
  endpoint, including the phase-2 first dash ending at +4 points; the PDF has no image and more
  than 20 line operators, and the SVG is a `<path>` without an image;
- the same pattern under a 2x canvas scale doubles its dash, gap and phase lengths and keeps the
  run count;
- the gradient reaches both declared ends and the (128,0,128) midpoint within eight sRGB levels,
  keeps a PDF `/ShadingType` and an SVG `linearGradient`, and embeds no image;
- the sharp shadow's ink bounds sit within 1.5 pixels of 31/52/95/96 points, its center is
  127 gray within four levels, outside is white, and a covering background hides it while the
  offset edge remains visible; PDF and SVG stay vector (SVG `<path>`);
- the 5-point blur (sigma 2.5) matches an independent Gaussian profile (0.5 x erfc) at the
  center, edge, and three outside falloff distances;
- the blurred shadow embeds a PDF image whose width ratio is 4.0 ± 6% between the default 288-DPI
  metadata and an explicit 72-DPI `DocumentSettings.ImageRasterDpi`, with height scaling too; the
  SVG contains `<image>` and no `<filter>`; 20 regenerations produce identical SVG, and repeated
  PDF generation produces identical bytes.

Artifacts are written to
`Source/FossPDF.UnitTests/bin/Release/<tfm>/parity-artifacts/GFX-205/`, including
`scenario-dash/`, `scenario-dash-scaled/`, `scenario-gradient/`, `scenario-shadow-sharp/`,
`scenario-shadow-behind-content/` and `scenario-shadow-blurred/` with PDF, PNG and (where
available) SVG files.

## Reproduce

From `Source`:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~LineAndShadowTests
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~LineAndShadowExamples
dotnet run --project FossPDF.Quality/FossPDF.Quality.csproj -c Release -- --mode all --output ../artifacts/quality
```

## Limitations

- Only Windows was exercised locally; Linux/macOS coverage comes from CI.
- qpdf and veraPDF are unavailable, so PDF conformance validation is skipped. The PDF checks here
  assert structure and rasterized appearance, not a conformance profile.
- Blur and gradient equivalence is asserted with declared feature-specific budgets (Gaussian
  profile, eight-level sRGB channels) rather than cross-platform golden images.
- The SVG fallback resolution (72 DPI) and the PDF raster DPI dependency are documented output
  behavior, not vector-quality claims.
