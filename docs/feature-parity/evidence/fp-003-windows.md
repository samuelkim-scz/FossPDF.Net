# FP-003 Windows validation

Recorded October 26, 2026, for [FP-003 / issue #4](https://github.com/samuelkim-scz/FossPDF.Net/issues/4), on the `feature/fp-003-quality-baselines` branch based on main commit `9ac13dd`. The pinned comparison baseline remains FossPDF commit `0339255`.

Environment: Windows x64, .NET SDK 9.0.305, .NET 9.0.21 runtime, SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.5, ten bundled Lato font files. The exact environment and font hashes are in `artifacts/quality/environment.json` after a run.

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Zero warnings, zero errors |
| Complete .NET 8 suite | 255 passed, 0 failed, 25 planned/skipped |
| Complete .NET 9 suite | 256 passed, 0 failed, 25 planned/skipped |
| `--mode all` quality run | Passed; exit code 0 |
| Visual scenarios | 4/4 passed against the committed baseline |
| Performance scenarios | 4/4 within the committed budgets |
| External conformance tools | qpdf and verapdf not installed locally; recorded as unavailable, not treated as a pass |

## Visual/semantic metrics

Each scenario rendered one A4 page at 72 DPI. Metrics are the baseline stored in
`docs/feature-parity/baselines/visual-baselines.json`.

| Scenario | Ink pixels | Ink bounds (x,y,w,h) | Notable colour buckets |
| --- | --- | --- | --- |
| `report` | 108,263 | 39, 40, 554, 757 | blue 17,468; gray 74,889; dark 5,341 |
| `text` | 37,072 | 39, 48, 512, 437 | dark 14,372; gray 22,557; red 132 |
| `graphics` | 104,261 | 39, 46, 517, 262 | red/green/blue/yellow ~5,000 each; gray 53,635 |
| `image` | 67,799 | 39, 46, 512, 273 | red/green/blue/yellow 15,000 each |

Tolerances are page count and dimensions exact, ink bounds ±4 pixels, ink pixels ±10% or 100,
and each colour bucket ±20% or 100. The PNGs and PDFs for every scenario are emitted under
`artifacts/quality/visual/<scenario>/`.

## Performance measurements and budgets

One local run; budgets are 8x elapsed, 2x allocated bytes and the larger of 1 GiB or 2x peak working
set. These are sanity ceilings for CI, not service-level objectives.

| Scenario | Documents | Median elapsed | Allocated/doc | Peak managed | Peak working set | Elapsed budget |
| --- | --- | --- | --- | --- | --- | --- |
| `report` | 8 | 17.0 ms | 2.0 MiB | 1.0 MiB | 55 MiB | 138 ms |
| `image-heavy` | 5 | 10.4 ms | 0.5 MiB | 0.0 MiB | 55 MiB | 70 ms |
| `large-table` (500x8) | 2 | 411.7 ms | 88.9 MiB | 5.0 MiB | 68 MiB | 2,608 ms |
| `concurrent` (4-way) | 8 | 5.9 ms/doc | 2.0 MiB | 2.0 MiB | 68 MiB | 50 ms |

Peak working set is process-wide and includes the runtime and native libraries; use it as a
trend/proxy for native memory. Allocated bytes are cumulative managed allocations per completed
document. Values vary between runs; the budgets absorb that variance.

## Conformance status

No PDF/A or PDF/UA profile is claimed. The quality run writes `conformance.json` and records the
tool versions and output when available. `qpdf` is installed on the Linux CI runner; `verapdf` is
optional. The pinned expectations and current claims are in
`docs/feature-parity/baselines/conformance-tools.json`. Selectable PDF/A and PDF/UA remain gated by
FP-402 and FP-401/FP-403.

## Reproduce

From `Source`:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
dotnet run --project FossPDF.Quality/FossPDF.Quality.csproj -c Release --no-build -- --mode all --output ../artifacts/quality
```

To regenerate a reviewed baseline or budget set, add `--update-visual-baseline` or
`--update-performance-budgets`, inspect the emitted PDFs/PNGs and JSON diff, and commit the change
with the evidence. CI runs the same quality command on Windows, Linux and macOS and uploads
`quality-evidence-<OS runner>` artifacts.

## Limitations

- The visual checks are semantic metrics, not pixel-identical goldens. They tolerate native
  rasterisation differences and should be supplemented by reviewing the emitted artefacts when
  changing layout or fonts.
- The default visual metrics were recorded on Windows x64. Linux/macOS runs compare against those
  values within the declared tolerances; if a platform differs beyond them, add a reviewed
  `platforms[<rid>]` override with its own metrics and tolerances.
- Performance figures are single-machine sanity measurements. Dedicated hardware and longer runs
  are required before publishing throughput or memory claims.
- qpdf is a structural validator, not a PDF/A or PDF/UA conformance validator.
