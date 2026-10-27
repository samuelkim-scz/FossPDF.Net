# FP-301 Windows validation

Recorded October 26, 2026, for [FP-301 / issue #20](https://github.com/samuelkim-scz/FossPDF.Net/issues/20), on `feature/fp-301-document-merging` based on main commit `6579f28`.

Environment: Windows x64, .NET SDK 9.0.305, .NET 9.0.21 runtime, SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.5.

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Zero warnings, zero errors |
| Complete .NET 8 suite | 267 passed, 0 failed, 24 planned/skipped |
| Complete .NET 9 suite | 268 passed, 0 failed, 24 planned/skipped |
| Parity category | 17 passed, 0 failed, 24 planned/skipped on both frameworks |
| `PDF-301` fixture | Active and passing |
| Merge example | 1 passed |
| Merged-document unit tests | 11 passed per framework |

## Verified behavior

- Two two-page source documents merge into four pages with marker order red, blue, red, blue.
- `UseOriginalPageNumbers()` produces `1/2`, `2/2`, `1/2`, `2/2`.
- `UseContinuousPageNumbers()` produces `1/4`, `2/4`, `3/4`, `4/4`.
- Mixed page sizes are retained (`120x100` followed by `200x150`).
- The same `IDocument` instance can be merged twice.
- The merged `DocumentLayoutExceptionThreshold` applies to the combined page count.
- Merged metadata/settings take precedence; source metadata/settings are ignored.
- Section names are scoped per source, including in continuous mode.
- The same merged document renders through PDF, PNG, SVG and previewer canvases.
- Null, empty and null-containing inputs are rejected with argument exceptions.

Artifacts for `PDF-301` are written beneath:
`Source/FossPDF.UnitTests/bin/Release/net8.0/parity-artifacts/PDF-301/` and the matching `net9.0`
directory, with `original/` and `continuous/` subdirectories containing the PDF and page PNGs.

## Reproduce

From `Source`:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~DocumentMergeExamples
```

## Boundaries

- This feature merges generated documents only. Loading, selecting and merging existing PDF files
  remains FP-302.
- Source document metadata/settings are not copied into the output; set the merged values explicitly.
- Cross-source section links and annotations are not supported; section links resolve within their
  source document.
- A source document must be safe to compose repeatedly if the same instance is used more than once.
- An empty source list is rejected; this is documented in
  [MERGING.md](../MERGING.md).
