# FP-107 Windows validation

Recorded October 26, 2026, for [FP-107 / issue #12](https://github.com/samuelkim-scz/FossPDF.Net/issues/12), based on main commit 6579f28 plus the `feature/fp-107-layout-helpers` branch.

Environment: Windows x64; .NET SDK 9.0.305; .NET 8.0.x and .NET 9.0.x runtimes. No package, native dependency or target-framework changes.

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Passed; zero warnings/errors |
| Complete .NET 8 suite | 267 passed, 0 failed, 24 planned/skipped |
| Complete .NET 9 suite | 268 passed, 0 failed, 24 planned/skipped |
| New `LayoutHelperTests` per framework | 11 passed |
| Parity category per framework | 17 passed, 0 failed, 24 planned/skipped |
| Shared layout fixture | `LAY-107` active and passing |
| .NET 9 layout-helper examples | 3 passed |

## Fixture evidence

`LAY-107` emits three pages beneath `parity-artifacts/LAY-107/` for both target frameworks:

| Page | Check |
| --- | --- |
| `page-1-shrink.png` | A 20x10 intrinsic child in a 100x80 area becomes 20x80 with `ShrinkHorizontal`, 100x10 with `ShrinkVertical` and 20x10 with `Shrink`; pixel bounds match those targets exactly. |
| `page-2-z-index.png` | Overlapping markers with depths 2/0/1 show depth 2 above depth 0 and depth 1 above depth 0 at their sample points; the composition-order marker remains on top within the same depth (covered by `LayoutHelperTests`). |
| `page-3-inline-alignment.png` | Top, middle, bottom and above-baseline 8x8 markers retain span order. The bottom marker's lower edge meets the top marker's upper edge at the font top, and the above-baseline marker ends on the rendered text baseline. |

`LayoutHelperTests` additionally verifies right-to-left shrink placement, equal depths, nested layers, PDF/SVG/previewer composition, exact alignment offsets using deterministic text metrics and mixed-height inline elements. The examples emit `shrink.pdf`, `z-index.pdf` and `inline-alignment.pdf` under `layout-helper-example/`.

Z-index composition uses the shared Skia page path for PDF, XPS, image, SVG and previewer output. The renderer pre-scans the composed tree for ZIndex wrappers; content created later by dynamic/lazy composition should keep the wrapper outside the dynamic element. Injected text elements must fit within one line, as documented.

## Reproduce

From `Source`:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --logger trx --results-directory ../artifacts/fp107/full
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity --logger trx --results-directory ../artifacts/fp107/parity
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~LayoutHelperExamples --logger trx --results-directory ../artifacts/fp107/examples
```

Local TRX logs and generated PDFs/PNGs are ignored artifacts. CI provides platform-specific results and downloadable parity evidence. The remaining 24 planned feature contracts stay skipped.

See [the layout helpers guide](../LAYOUT_HELPERS.md) for API behavior, stacking rules, alignment references and limitations.
