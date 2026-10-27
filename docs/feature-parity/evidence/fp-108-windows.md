# FP-108 Windows validation

Recorded October 26, 2026, for [FP-108 / issue #13](https://github.com/samuelkim-scz/FossPDF.Net/issues/13), on `feature/fp-108-content-position-capture` based on main commit `6579f28`.

Environment: Windows x64, .NET SDK 9.0.305, .NET 9.0.21 runtime, SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.5.

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Zero warnings, zero errors |
| Complete .NET 8 suite | 261 passed, 0 failed, 24 planned/skipped |
| Complete .NET 9 suite | 262 passed, 0 failed, 24 planned/skipped |
| Parity category | 17 passed, 0 failed, 24 planned/skipped on both frameworks |
| `LAY-108` fixture | Active and passing |
| New content-position tests | 5 passed per framework |
| Content-position example | 1 passed |

## Verified behavior

- Twelve 20-point rows on 120x100 pages produce three pages and twelve captured occurrences.
- Every capture reports the expected page number, X=10, Y=10/30/50/70, width=100 and height=20
  within 0.25 point.
- Repeating generation replaces the capture set rather than appending to it; values are stable.
- The built-in `Document.ContentPositions` exposes the complete set after generation.
- A dynamic component reads the same captures through
  `DynamicContext.GetContentCapturedPositions` and sees all twelve occurrences.
- The internal free-canvas transform tracking matches Skia's matrix mapping for translation,
  scale and rotation; `PageElementLocation.Transform` retains the matrix.
- Empty identifiers are rejected with an argument exception.

Artifacts are written beneath `parity-artifacts/LAY-108/` and the example writes
`content-position-example/document.pdf`, `positions.json` and page PNGs.

## Reproduce

From `Source`:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~ContentPositionExamples
```

## Boundaries

- Captures are recorded during the initial layout pass. Dynamic components must handle an empty or
  incomplete first-pass collection; the built-in document exposes the complete set after generation.
- Captures are a reporting feature, not a layout input. Conditionally changing the same content from
  its captured geometry can produce unstable layouts.
- Coordinates are in output canvas space: points for PDF/SVG/previewer, pixels for raster export at
  the selected DPI. Rotated geometry is represented by the capture's `Transform`.
- Previewer transport and interactive inspection remain FP-501/FP-502.
- No new dependencies or target frameworks are introduced.
