# FP-302 Windows validation

Recorded October 28, 2026, for [FP-302 / issue #21](https://github.com/samuelkim-scz/FossPDF.Net/issues/21), on `feature/fp-302-pdf-loading` based on main commit `c9950a2`.

Environment: Windows x64, .NET SDK 9.0.305, .NET 9.0.9 runtime, SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.5, PDFsharp 6.2.4 (MIT, exact pin).

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Zero warnings, zero errors |
| Complete .NET 8 suite | 463 passed, 0 failed, 13 skipped (11 planned contracts + 2 net9.0-scoped fixtures) |
| Complete .NET 9 suite | 520 passed, 0 failed, 11 skipped (planned contracts) |
| Parity category | 28 passed / 13 skipped on .NET 8; 30 passed / 11 skipped on .NET 9 |
| `PDF-302` fixture | Active and passing on .NET 9; skipped on .NET 8 by declared framework scope |
| PDF operation unit tests | 19 passed on .NET 9 |
| PDF operation examples | 2 passed |
| `FossPDF.Quality --mode all` | All requested checks passed; qpdf and veraPDF not installed, so those conformance checks were skipped |
| Companion package contents | `lib/net9.0`, `PackageReadme.md`, `THIRD-PARTY-NOTICES.md`, `licenses/PDFsharp-6.2.4-LICENSE.txt` |

## Verified behavior

- `Load(byte[])`, `Load(Stream)` and `LoadFile(path)` read the input immediately; borrowed streams are read from their current position and left open, and caller byte arrays are not modified.
- `TakePages` supports single pages, `z`/`rN`, ascending and descending ranges, `odd`/`even` filters, repetitions and `x` exclusions. `ArgumentException` reports syntax errors; `PdfOperationException` reports pages outside the document or a selection that produces no pages.
- `MergeFile`, `Merge(byte[])` and `Merge(Stream)` append another source, with or without a selector. A merge before any explicit selection keeps all primary pages first.
- Selected pages are imported in selector order with their media boxes, content streams, resources and annotations intact. A generated page carrying a link annotation keeps that annotation after import.
- Metadata (title, author, subject, keywords, creator, creation/modification dates) is copied from the primary loaded document. XMP metadata is not copied and the producer is set by PDFsharp.
- `Save(path)`, `Save(Stream)` and `SaveToBytes()` stage output in memory. A failed operation writes nothing; the output stream stays open and appendable.
- Malformed, truncated and empty inputs throw `PdfOperationException`. A protected input without a password or with a wrong password throws `PdfOperationException`; the declared password opens it for import.
- `PDF-302` selects PAG-001 pages 2 then 1, merges PAG-002 page 1, and reports three pages in blue, red, first-overflow-page order. Imported content streams are byte-identical to the corresponding source pages, page boxes remain 120x100, and the title comes from PAG-001.

Artifacts for `PDF-302` are written beneath
`Source/FossPDF.UnitTests/bin/Release/net9.0/parity-artifacts/PDF-302/`, including
`sources/first.pdf`, `sources/second.pdf`, `selected-merged.pdf`, `password-protected.pdf` and
`decrypted.pdf`.

The PDF operation examples write beneath
`Source/FossPDF.Examples/bin/Release/net9.0/pdf-operations-selection/` and
`Source/FossPDF.Examples/bin/Release/net9.0/pdf-operations-streams/`.

## Reproduce

From `Source`:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore -f net9.0 --filter FullyQualifiedName~PdfOperationTests
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PdfOperationExamples
dotnet run --project FossPDF.Quality/FossPDF.Quality.csproj -c Release -- --mode all --output ../artifacts/quality
```

## Boundaries

- The companion package targets `net9.0` only and the core package does not reference it, per ADR-0001.
- PDFsharp `6.2.4` is pinned exactly because `6.5.0` declares AGPL-3.0-only. See
  [NATIVE_DEPENDENCIES.md](../NATIVE_DEPENDENCIES.md).
- This work item does not include overlays/underlays (FP-303), configurable encryption and
  permissions (FP-304), attachments/XMP extensions (FP-305), or extra compression/linearization
  (FP-306).
- Existing PDF pages are not rasterized or previewed. Validation inspects the imported objects with
  PDFsharp rather than comparing rendered pixels.
- XMP metadata, forms, JavaScript and optional-content behaviour are not part of this contract and
  are not claimed.
- qpdf and veraPDF were unavailable, so no independent PDF conformance claim is made; the feature
  contract is covered by object-level assertions in `PDF-302` and the unit suite.
