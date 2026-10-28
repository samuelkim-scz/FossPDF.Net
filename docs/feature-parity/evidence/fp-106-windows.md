# FP-106 Windows validation

Recorded October 26, 2026, for [FP-106 / issue #11](https://github.com/samuelkim-scz/FossPDF.Net/issues/11), based on main commit 6579f28 plus the `feature/fp-106-lazy-composition` branch.

Environment: Windows x64; .NET SDK 9.0.305; .NET 8.0.x and .NET 9.0.x runtimes. No package, native dependency or target-framework changes.

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Passed; zero warnings/errors |
| Complete .NET 8 suite | 261 passed, 0 failed, 24 planned/skipped |
| Complete .NET 9 suite | 262 passed, 0 failed, 24 planned/skipped |
| New `LazyTests` per framework | 5 passed |
| Parity category per framework | 17 passed, 0 failed, 24 planned/skipped |
| Shared composition fixture | `LAY-106` active and passing |
| .NET 9 lazy examples | 2 passed |

## Fixture evidence

`LAY-106` renders 1000 alternating-color 12-point items on a 240x180 page in eager, Lazy and LazyWithCache modes. For each mode it emits the PDF and first/last page rasters beneath `parity-artifacts/LAY-106/`.

- All three modes produced the same page count, page pixels and item ordering.
- The eager mode invoked no Lazy content builder.
- LazyWithCache invoked each section builder once per generation across the PDF and raster generations (2000 calls for 1000 sections); Lazy invoked builders again for each render pass, demonstrating deferred/repeated construction.
- A second generation of the cached document produced identical pages, confirming that cache state is not reused across generations and no stale or disposed content is retained.

`LazyTests` additionally verifies byte-identical eager/lazy raster output, multi-page sections, release of non-cached content after the final render, retention of cached content, and recovery after a construction exception. It observes content-builder call counts and the element's retained child rather than claiming a fixed memory ratio; actual peak/retained memory depends on the section tree and native font/image caches. The examples emit `lazy-sections.pdf` and `lazy-with-cache.pdf` under `lazy-example/`.

## Reproduce

From `Source`:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --logger trx --results-directory ../artifacts/fp106/full
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity --logger trx --results-directory ../artifacts/fp106/parity
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~LazyExamples --logger trx --results-directory ../artifacts/fp106/examples
```

Local TRX logs and generated PDFs/PNGs are ignored artifacts. CI provides platform-specific results and downloadable parity evidence. The remaining 24 planned feature contracts stay skipped.

See [the deferred composition guide](../LAZY_COMPOSITION.md) for lifecycle, cache trade-offs, failure rules and ownership.

## October 28 integration and lifetime validation

Integrated main through `71bc1f3`, retaining the reflection-based acceptance runner registry and both the deferred-composition and document-merge contracts. Two reproduced integration defects are fixed:

- Repeated uncached Lazy content previously contributed an empty glyph set at subsetting time. Its first-pass tree now survives per-page resets. Both cached and uncached variants match eager glyph requests on .NET 8 and .NET 9.
- Multi-column speculative drawing previously consumed deferred descendants and changed page pixels. Lazy now snapshots its child reference, completion flag and descendant layout state. All four cached/uncached and balanced/unbalanced combinations match eager output; balanced dynamic content also matches.

Release solution build: zero warnings/errors (`AVALONIA_TELEMETRY_OPTOUT=1` avoids the sandbox-restricted telemetry log). Full suites: .NET 8, 425 passed and 17 skipped; .NET 9, 462 passed and 16 skipped. The extra .NET 8 skip is the net9.0-only SVG contract. All 16 Lazy/lifetime cases pass per framework, and both .NET 9 examples pass. The four visual scenarios and all performance budgets pass; qpdf and veraPDF are unavailable locally and are explicitly skipped.

The cancellation probe paints text and a borrowed image before a drawing callback throws `OperationCanceledException`. The generator wraps it in `DocumentDrawingException`, releases all document-owned font handles, leaves the borrowed image usable, and permits a successful fresh generation.

The retained-memory probe uses 64 sections of 32 text rows (2048 rows), producing 90 pages in each mode. It deliberately holds section wrappers alive after generation and records the managed bytes reclaimed when those roots are released. Representative .NET 9 measurements from the full-suite run:

| Mode | Retained managed bytes | Retained elements | Process working set (bytes) | Document font resources after generation |
| --- | ---: | ---: | ---: | ---: |
| Eager | 5,725,936 | 12,416 | 136,257,536 | 0 |
| Lazy | 7,328 | 128 | 137,773,056 | 0 |
| LazyWithCache | 5,444,944 | 8,384 | 144,961,536 | 0 |

Working set includes managed memory, runtime/native caches and allocator history. It is a process-level native-memory proxy, not an isolated native-allocation measurement; differences between sequential modes are not attributed solely to caching. The deterministic assertions check page counts, retained tree size and native font cleanup. GC-byte and working-set observations are recorded without brittle fixed performance thresholds. No peak-memory reduction is claimed: first-pass trees are intentionally retained until glyph collection. JSON observations are attached to TRX and included in the existing parity CI artifacts.

Reproduce the lifecycle probe with `dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --filter FullyQualifiedName~Lazy`. Full-suite, example and quality commands are unchanged; this run stores local logs under `Source/artifacts/fp106-final/`.