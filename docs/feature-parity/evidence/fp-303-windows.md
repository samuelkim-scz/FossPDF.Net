# FP-303 Windows validation

Recorded October 28, 2026, for [FP-303 / issue #22](https://github.com/samuelkim-scz/FossPDF.Net/issues/22), on `feature/fp-303-pdf-overlays` based on main commit `20756f4`.

Environment: Windows x64, .NET SDK 9.0.305, .NET 9.0.9 runtime, SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.5, PDFsharp 6.2.4 (MIT, exact pin).

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Zero warnings, zero errors |
| Complete .NET 8 suite | 463 passed, 0 failed, 13 skipped (10 planned contracts + 3 net9.0-scoped fixtures) |
| Complete .NET 9 suite | 541 passed, 0 failed, 10 skipped (planned contracts) |
| Parity category | 28 passed / 13 skipped on .NET 8; 31 passed / 10 skipped on .NET 9 |
| `PDF-303` fixture | Active and passing on .NET 9; skipped on .NET 8 by declared framework scope |
| Overlay/underlay unit tests | 20 passed on .NET 9 |
| PDF operation and layer examples | 4 passed |
| `FossPDF.Quality --mode all` | All requested checks passed; qpdf and veraPDF not installed, so those conformance checks were skipped |
| Companion package contents | `lib/net9.0`, `PackageReadme.md`, `THIRD-PARTY-NOTICES.md`, `licenses/PDFsharp-6.2.4-LICENSE.txt` |

## Verified behavior

- `UnderlayFile` and `OverlayFile` accept a `DocumentOperation.LayerConfiguration` with `FilePath`, `TargetPages`, `SourcePages` and `RepeatSourcePages`. The file is read when the call is made, and the configuration values are copied, so later mutation does not change the plan.
- `TargetPages` selects output pages after the recorded selection/merge plan; unselected pages keep their exact content-stream signature. `SourcePages` maps layer pages in target order, stops when exhausted, and `RepeatSourcePages` cycles its own selector after the initial list. `RepeatSourcePages` without `SourcePages` starts repeating at the first target page, matching the pinned qpdf behavior.
- Underlays are inserted as separate content streams before the original streams, in registration order; overlays are appended after them, in registration order. The original page content stream remains byte-identical. Per-page form names (`/L0`, `/L1`, ...) are added to a cloned resource dictionary, so an untargeted page does not inherit layer XObjects.
- Layer pages are imported as form XObjects. Their resources are copied, including fonts, `/ExtGState` transparency values and `/Group` transparency groups; no page content is rasterized.
- Placement fits the layer uniformly and centers it in the destination's effective trim box (TrimBox, then CropBox, then MediaBox), preserves the destination page boxes, shows the source as its own `/Rotate` makes it appear, and compensates the destination rotation so the layer stays upright. A same-size, unrotated layer with no trim offset uses the identity matrix.
- Layer configuration failures are explicit: null configuration (`ArgumentNullException`), blank path or selectors (`ArgumentException`), missing file (`FileNotFoundException` when the layer is added), and malformed/encrypted layer files or out-of-range selectors (`PdfOperationException` when saving). A failed operation writes nothing to the output stream.
- The primary input byte array and the layer file bytes are not modified. The primary document's own annotations and links are preserved.
- `PDF-303` applies a full-page yellow underlay and a 20x20 green overlay to page 1 of the generated PAG-001 document. The result keeps two 120x100 pages; page 1 is `[underlay form stream, unchanged original stream, overlay form stream]`; both forms carry the declared color and rectangle; page 2's content-stream signature is unchanged; page rotation is unchanged; and both layers use `1 0 0 1 0 0 cm`.

Artifacts for `PDF-303` are written beneath
`Source/FossPDF.UnitTests/bin/Release/net9.0/parity-artifacts/PDF-303/`: `source.pdf`,
`sources/underlay.pdf`, `sources/overlay.pdf` and `layered.pdf`.

The layer examples write beneath
`Source/FossPDF.Examples/bin/Release/net9.0/pdf-operations-layers/` and
`Source/FossPDF.Examples/bin/Release/net9.0/pdf-operations-layer-repeat/`.

## Reproduce

From `Source`:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore -f net9.0 --filter FullyQualifiedName~PdfLayerTests
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PdfLayerExamples
dotnet run --project FossPDF.Quality/FossPDF.Quality.csproj -c Release -- --mode all --output ../artifacts/quality
```

## Boundaries

- The companion package targets `net9.0` only and the core package does not reference it, per ADR-0001.
- Annotations, form fields, outlines and JavaScript on **layer** pages are not copied; only page content and resources are placed. qpdf also copies layer-page annotations, so this is an intentional boundary of this implementation, documented in [PDF_LAYERS.md](../PDF_LAYERS.md). The primary document's annotations are preserved.
- `/UserUnit` is not applied when placing layers.
- Existing PDF pages and layers are not rasterized or rendered for comparison. Validation inspects content streams, form dictionaries, page boxes and rotation with PDFsharp rather than comparing pixels.
- qpdf and veraPDF were unavailable, so no independent PDF conformance claim is made. The feature contract is covered by object-level assertions in `PDF-303` and the 20 overlay/underlay unit tests.
