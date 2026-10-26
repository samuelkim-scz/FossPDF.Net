# Quality baselines

[FP-003 / issue #4](https://github.com/samuelkim-scz/FossPDF.Net/issues/4) establishes reproducible
visual, semantic and performance evidence before parallel feature work changes layout or native
dependencies. The baseline is pinned to FossPDF commit `0339255` and QuestPDF 2026.9.1.

The runnable baseline tool is `Source/FossPDF.Quality`. It targets .NET 9 only, is not packed or
published, and does not change the library's `netstandard2.1`/`net9.0` API surface. It is deliberately
separate from the unit test suite: normal builds stay fast, while the quality job records
environment, rendering metrics, performance and conformance evidence on every CI platform.

## Run the checks

From `Source`:

```powershell
dotnet run --project FossPDF.Quality/FossPDF.Quality.csproj -c Release -- --mode environment
dotnet run --project FossPDF.Quality/FossPDF.Quality.csproj -c Release -- --mode visual
dotnet run --project FossPDF.Quality/FossPDF.Quality.csproj -c Release -- --mode performance
dotnet run --project FossPDF.Quality/FossPDF.Quality.csproj -c Release -- --mode conformance
dotnet run --project FossPDF.Quality/FossPDF.Quality.csproj -c Release -- --mode all --output ../artifacts/quality
```

| Option | Behavior |
| --- | --- |
| `--mode environment` | Records the runtime, native library versions and embedded font hashes. |
| `--mode visual` | Renders the fixed scenarios, writes PDFs/PNGs, and compares semantic raster metrics with the committed baseline. |
| `--mode performance` | Measures report, image-heavy, large-table and concurrent generation, then compares them with the committed budgets. |
| `--mode conformance` | Records the current conformance claims and runs pinned external validators when available. |
| `--output <dir>` | Artifact directory; default `artifacts/quality`. |
| `--baseline-dir <dir>` | Baseline directory; default `docs/feature-parity/baselines`. |
| `--update-visual-baseline` | Records the current run. On the reference platform it updates the default metrics; on another platform it adds or replaces `platforms[<rid>]`. |
| `--update-performance-budgets` | Rewrites budgets from the current measurements using the documented multipliers. |
| `--no-enforce-budgets` | Records budget comparisons but does not fail the process. |

Exit codes: `0` pass, `1` baseline/budget/structural failure, `2` invalid arguments.

## Environment record

Each run writes `environment.json` (and embeds the same object in the other result files) with:

- operating system, process architecture and runtime identifier (`win-x64`, `linux-x64`, `osx-arm64`);
- .NET framework and FossPDF/SkiaSharp/HarfBuzzSharp package versions;
- SHA-256 hashes of the ten embedded Lato font files;
- the pinned FossPDF baseline commit.

Visual comparison fails when the embedded font hashes change, forcing a reviewed baseline update
instead of silently accepting different metrics.

## Visual and semantic scenarios

The fixed scenarios are built entirely from public FossPDF APIs and deterministic inputs:

| Scenario | Coverage |
| --- | --- |
| `report` | Header, footer, page numbers, table header, alternating cells and paragraphs. |
| `text` | Bold/italic/underline/color/superscript/subscript spans, multiple sizes and wrapped paragraphs. |
| `graphics` | Solid backgrounds, bordered panel, horizontal line and a shaded grid. |
| `image` | Two copies of a deterministic 200x150 PNG quadrant image plus text. |

Every page is rendered both to PDF (for page-count evidence) and to a 72-DPI PNG. For each page the
tool records the page dimensions, ink-pixel count, ink bounding box, and counts for the `red`,
`green`, `blue`, `yellow`, `dark`, `gray` and `other` colour buckets. These are semantic metrics,
not byte-identical goldens: they tolerate platform rasterisation differences while still catching
missing content, changed layout, shifted regions and material colour changes.

Default tolerances in `visual-baselines.json`:

| Metric | Tolerance |
| --- | --- |
| Page count | Exact |
| Page dimensions | Exact |
| Ink bounding box | ±4 pixels on x, y, width and height |
| Ink pixel count | ±10% or 100 pixels, whichever is larger |
| Each colour bucket | ±20% or 100 pixels, whichever is larger |

If a platform differs beyond these values, add a reviewed override instead of widening the global
tolerance:

```json
{
  "platforms": {
    "linux-x64": {
      "measuredOn": "linux-x64",
      "tolerances": { "boundsPixels": 6, "inkPixelsRelative": 0.15, "colorPixelsRelative": 0.25, "minimumAbsolutePixels": 100 },
      "scenarios": { "...": "the platform-specific metrics" }
    }
  }
}
```

Running `--update-visual-baseline` on a non-reference platform creates that override automatically.
The default metrics remain measured on the reference platform (`win-x64` at the time of writing).

## Performance measurements

The tool records four scenarios:

| Scenario | Documents | Parallelism | Purpose |
| --- | --- | --- | --- |
| `report` | 8 | 1 | Representative report path. |
| `image-heavy` | 5 | 1 | Shared-image placement and encoding. |
| `large-table` | 2 | 1 | 500 x 8 table with paging. |
| `concurrent` | 8 | min(4, CPU count) | Independent simultaneous generation. |

Each result records median elapsed time per document, total wall time, pages per second, total
allocated bytes per document, sampled peak managed heap, peak working set, working-set delta and
average PDF size. Working set is used as a portable proxy for native memory; managed heap is the
value sampled after each completed generation.

`performance-budgets.json` is generated from the measurements with these reviewable multipliers:

- median elapsed: **8x** the measured baseline (CI runners vary substantially);
- allocated bytes per document: **2x** the measured baseline;
- peak working set: the larger of **1 GiB** or **2x** the measured peak.

These are CI sanity ceilings, not service-level objectives. They catch order-of-magnitude
regressions while leaving room for runner variability. Tighten a budget only with reviewed evidence
from multiple runs and platforms.

## Conformance

The current library does **not** claim PDF/A or PDF/UA conformance. `PdfAConformance.None` is the
default; selectable PDF/A-2/PDF/A-3 profiles require FP-402, and PDF/UA-1 requires FP-401/FP-403.
The legacy `DocumentMetadata.PdfA` flag is not a validated profile.

The conformance run writes `conformance.json` and records:

- `qpdf --check` when qpdf is available, as a structural PDF check (exit code 0 or warnings-only 3
  passes; structural errors fail);
- `verapdf` output when available, without treating it as a conformance claim;
- the pinned minimum tool versions from `conformance-tools.json`;
- explicit notes about the unimplemented profiles.

The Linux CI runner installs qpdf. verapdf is optional because it requires a Java runtime; when it
is present, its report is retained in the artifact for future profile work.

## Baselines and artifacts

Committed baselines live in `docs/feature-parity/baselines/`:

- `visual-baselines.json` — reference metrics, tolerances, font hashes and optional platform overrides;
- `performance-budgets.json` — per-scenario reviewable ceilings;
- `conformance-tools.json` — pinned validator expectations and current claims.

Run artifacts are written beneath `artifacts/quality/` and uploaded by CI as
`quality-evidence-<OS runner>`:

```text
artifacts/quality/
  environment.json
  visual/
    metrics.json
    result.json
    report/document.pdf, page-1.png
    text/...
    graphics/...
    image/...
  performance/performance.json
  conformance/conformance.json, document.pdf
```

## Updating a baseline

1. Run the affected mode with `--update-visual-baseline` or `--update-performance-budgets`.
2. Inspect the PNGs/PDFs and the numeric diff; do not accept a change you cannot explain.
3. Keep tolerance and budget changes separate from implementation changes, and describe the reason.
4. Commit the updated JSON with the evidence; CI will re-run the comparison on Windows, Linux and macOS.

For a new scenario, add it to `DocumentScenarios` and to the definitions in the visual/performance
runners, update the baseline, review the artifacts, and document the coverage in this file.

## Reference evidence

The initial Windows x64 run is recorded in
[evidence/fp-003-windows.md](evidence/fp-003-windows.md). It uses .NET SDK 9.0.305, SkiaSharp
3.119.4, HarfBuzzSharp 8.3.1.5 and the bundled Lato fonts. The committed visual metrics and
performance budgets originate from that run; CI on Linux and macOS verifies or adds platform
overrides.

## Limitations

- Visual checks are semantic metrics rather than pixel-perfect goldens. They are intentionally
  tolerant of native rasterisation differences and are not a substitute for reviewing artefacts when
  changing layout or fonts.
- Working set is a process-wide proxy for native memory; it includes the runtime and native
  libraries, so compare trends rather than treating it as exact per-document usage.
- Performance budgets are CI sanity ceilings. Dedicated benchmark hardware and longer runs are
  required before publishing throughput or memory claims.
- qpdf validates PDF structure, not PDF/A or PDF/UA conformance. Those claims remain gated by FP-402
  and FP-403 and their validator reports.
