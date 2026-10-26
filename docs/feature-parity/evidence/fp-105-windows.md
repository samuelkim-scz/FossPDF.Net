# FP-105 Windows validation

Recorded October 26, 2026, for [FP-105 / issue #10](https://github.com/samuelkim-scz/FossPDF.Net/issues/10), based on main commit 8a7c158 plus the `feature/fp-105-page-controls` branch.

Environment: Windows x64, .NET SDK 9.0.305, .NET 8.0.x and .NET 9.0.x runtimes. No package or native dependencies changed.

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Zero warnings, zero errors |
| Complete .NET 8 suite | 222 passed, 0 failed, 26 planned/skipped |
| Complete .NET 9 suite | 223 passed, 0 failed, 26 planned/skipped |
| New `PageControlTests` per framework | 9 passed |
| Parity category per framework | 15 passed, 0 failed, 26 planned/skipped |
| Shared page-control fixture | `PAG-105` active and passing |
| .NET 9 page-control examples | 2 passed |
| Existing settings consumer | 1 passed |

## Implemented behavior

- `PreventPageBreak()` keeps its content together on the first page where it appears. A group that
  does not fit moves intact to the next page; content longer than a page continues normally.
- `Repeat()` re-renders its content on every page where the surrounding layout is still rendering.
  The element restores child state only after a full render, so measurement passes do not consume
  or duplicate content.
- `ShowIf(Predicate<ShowIfContext>)` exposes `PageNumber` and `TotalPages`. `TotalPages` is `null`
  during the initial layout pass and holds the resolved document length in later passes.

The implementation ports the QuestPDF 2026.9.1 element semantics to FossPDF's existing
`IStateResettable` and three-pass renderer. `DocumentGenerator.RenderPass` now returns the page
count of the pass, and `PageContext` exposes `IsInitialRenderingPhase` and `DocumentLength`.

## Fixture evidence

`PAG-105` runs three independent scenarios and emits artifacts beneath
`parity-artifacts/PAG-105/`:

- `prevent-page-break/` — two pages; the 30-point group starts at the top of page 2.
- `repeat/` and `repeat-second-generation/` — the marker appears once per page while the adjacent
  content continues; regenerating produces identical per-page pixel counts.
- `show-if/` — conditional content appears only on even pages, and recorded predicate contexts
  include `(page 2, total pages null)` from the initial pass and `(page 2, total pages 2)` from the
  final pass.

The checks use PDF-reported page counts and independently decoded raster pages. They do not claim
PDF semantic or tagged-structure conformance; those remain with FP-401/FP-403.

## Reproduce

From `Source`:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PageControlExamples
```

Local TRX logs and generated PDFs/PNGs are ignored artifacts. CI provides platform-specific results
and downloadable parity evidence. The remaining 26 planned feature contracts stay skipped.
