# FP-204 Windows validation

Recorded October 27, 2026, for [FP-204 / issue #18](https://github.com/samuelkim-scz/FossPDF.Net/issues/18),
based on the `feature/fp-201-svg-input` stack tip plus the `feature/fp-204-container-styling` branch.

Environment: Windows 11 Pro 10.0.26200, win-x64, .NET SDK 9.0.305, .NET 8.0 and .NET 9.0 runtimes,
SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.5. Fonts are the 10 embedded Lato faces.

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Zero warnings, zero errors |
| Complete .NET 8 suite | 349 passed, 0 failed, 21 planned/skipped |
| Complete .NET 9 suite | 386 passed, 0 failed, 20 planned/skipped |
| New `RoundedCornersTests` | 27 passed on both frameworks |
| Parity category, .NET 8 | 20 passed, 0 failed, 21 skipped |
| Parity category, .NET 9 | 21 passed, 0 failed, 20 skipped |
| New fixture | `GFX-204` active and passing on both frameworks |
| `ContainerStylingExamples` (.NET 9) | 3 passed |
| Quality tool, `--mode all` | visual and performance passed; conformance skipped (validators unavailable) |

Activating `GFX-204` reduces the planned/skipped parity count from 21 to 20 on .NET 9 and from 22
to 21 on .NET 8; `Catalog_AccountsForEveryFeatureWorkItemAndExecutableFixture` stays green with 16
active fixtures. The quality tool's visual ink counts (`report` 108288 px, `text` 37067 px,
`graphics` 104260 px, `image` 67815 px) are unchanged from the FP-201 baseline: the legacy
solid-background and square-border draw sequences are preserved, so existing output is identical.

## Implemented behavior

- `BorderRadius` is a validated public value type (finite, non-negative, per-corner); `Unit`
  conversion and `Zero`/`Uniform` helpers are available.
- `BorderAlignment.Inside/Center/Outside` controls where the border ring sits relative to the
  layout bounds. `Center` remains the default and keeps the historical four-rectangle output.
- Fluent `CornerRadius`, `CornerRadiusTopLeft/TopRight/BottomRight/BottomLeft`,
  `BorderAlignment`, `BackgroundGradient`, `BorderGradient` and `Clip` all retarget one decoration,
  so call order does not change the result.
- Backgrounds, borders and clips share one `RectangleStyle`. Rounded fills use `DrawRoundRect`;
  borders build an even-odd ring with outer radii expanded by the alignment and inner radii
  clamped at zero; gradients use `SKShader.CreateLinearGradient` with the four declared directions.
- `Clip` saves the canvas state, intersects a rounded clip, draws the child, and restores. The
  container's own fill and border are painted outside the clip.
- All four new `ICanvas` members are implemented by `SkiaCanvasBase`, `FreeCanvas`, `ProxyCanvas`,
  the test `MockCanvas` and `OperationRecordingCanvas`; PDF and SVG output keep vectors
  (`/ShadingType`, `<linearGradient>`, `W n`, `<clipPath>`).

### Deliberate constraints

- Two-stop linear gradients only; dashed borders, line gradients and shadows are FP-205.
- Gradients interpolate unpremultiplied in sRGB, so a transparent black stop also fades its RGB.
  The topic guide documents using a transparent tint of the target color for a pure opacity fade.
- Corner radii affect painting only, never layout or pagination. An `Outside` border paints
  outside the measured bounds, so callers must reserve space when overlap is unacceptable.
- Legacy sequences are preserved only for solid square fills and solid, square, center-aligned
  borders; adding a radius, gradient or non-center alignment switches to the style op.

## Fixture evidence

`GFX-204` renders a 160x120 page at 288 DPI with an 80x40 box at (20,20):

- border extents for inside/center/outside alignment within one pixel (0.25 point) of
  20/18/16 and 100/102/104 points;
- an 8-point inside ring with the corner cutout and inner hole empty;
- a red-to-blue horizontal gradient matching its ends and (128,0,128) midpoint within eight sRGB
  levels, with vector PDF shading and SVG `<linearGradient>`;
- a 2x scaled and 15-degree rotated child clipped to the rounded bounds, with a PDF clip operator,
  SVG `<clipPath>` and SVG transform.

Artifacts are written to
`Source/FossPDF.UnitTests/bin/Release/<tfm>/parity-artifacts/GFX-204/`, including
`scenario-border-<alignment>/`, `scenario-corner-cutout/`, `scenario-gradient-horizontal/`,
`scenario-clip-nested/` and `scenario-clip-transformed/`.

## Reproduce

From `Source`:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~RoundedCornersTests
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~ContainerStylingExamples
dotnet run --project FossPDF.Quality/FossPDF.Quality.csproj -c Release -- --mode all --output ../artifacts/quality
```

## Limitations

- Only Windows was exercised locally; Linux/macOS coverage comes from CI.
- qpdf and veraPDF are unavailable, so PDF conformance validation is skipped. The PDF checks here
  assert structure and rasterized appearance, not a conformance profile.
- Gradient color-space equivalence is asserted with an eight-level per-channel budget against the
  declared sRGB stops; no cross-platform gradient golden images are introduced.
