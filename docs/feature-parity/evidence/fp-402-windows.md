# FP-402 Windows validation

Recorded October 28, 2026, for [FP-402 / issue #27](https://github.com/samuelkim-scz/FossPDF.Net/issues/27),
on `feature/fp-402-pdfa` based on `feature/fp-401-tagged-pdf` at commit `e5d1336`.

Environment: Windows 11 Pro x64 (10.0.26200), .NET SDK 9.0.305, .NET 9.0.9 runtime, SkiaSharp
3.119.4, HarfBuzzSharp 8.3.1.5, PDFsharp 6.2.4 (MIT, exact pin). Independent inspection used Python
3.12.10 with pypdf 6.21.2 and the pinned veraPDF 1.30.3 release. Local `java` on `PATH` was Java 8;
the validation used a portable Temurin JRE `17.0.20.1+1` (Windows x64 zip SHA-256
`bc21a93923103cdaac93ee337b0ae4365e739fde36df823dd456bc67c8a9d352`). CI installs Temurin 17 with
`actions/setup-java`. The verifier imports no artifact paths; every path is passed as an argument,
and the downloaded installer is opened as data, never executed.

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Zero warnings, zero errors |
| Complete .NET 8 suite | 506 total: 493 passed, 0 failed, 13 skipped |
| Complete .NET 9 suite | 664 total: 659 passed, 0 failed, 5 skipped |
| Parity category | 28 passed / 13 skipped on .NET 8; 36 passed / 5 skipped on .NET 9 |
| `ACC-402` fixture | Active and passing on .NET 9; skipped on .NET 8 by declared framework scope |
| `PdfATests` (companion) | 26 passed on .NET 9 |
| PDF/A examples | 4 passed |
| `FossPDF.Quality --mode all` | All requested checks passed; optional qpdf/veraPDF conformance checks skipped because the quality tool does not discover those executables |
| Pinned veraPDF 1.30.3 | PASS for all 9 artifacts at their exact flavours |
| `verify_pdfa.py` | `PASS ACC-402 (135 independent checks)` |

The skipped counts are the five planned contracts on .NET 9 and, on .NET 8, those five plus the
eight net9.0-scoped fixtures (`IMG-201`, `PDF-302`, `PDF-303`, `PDF-304`, `PDF-305`, `PDF-306`,
`ACC-401`, `ACC-402`).

## VeraPDF output

The pinned release reported the exact flavour for every artifact:

```text
PASS .../ACC-402/pdfa-2a.pdf 2a
PASS .../ACC-402/pdfa-2b.pdf 2b
PASS .../ACC-402/pdfa-2u.pdf 2u
PASS .../ACC-402/pdfa-3a.pdf 3a
PASS .../ACC-402/pdfa-3b.pdf 3b
PASS .../ACC-402/pdfa-3u.pdf 3u
PASS .../ACC-402/pdfa-3a-attachment.pdf 3a
PASS .../ACC-402/pdfa-3b-attachment.pdf 3b
PASS .../ACC-402/pdfa-3u-attachment.pdf 3u
PASS ACC-402 (135 independent checks)
```

Each artifact also received a machine-readable `--format mrr` report. Report files are written under
`artifacts/pdfa/` as `verapdf-<artifact>-txt` and `verapdf-<artifact>-mrr.xml`; CI uploads them with
the parity evidence.

The verifier pins veraPDF by version, installer URL and installer SHA-256. The installer is opened
as a ZIP, and the CLI fat jar is extracted from `resources/packs/pack-veraPDF CLI`. That pack has an
IzPack launcher script appended after the ZIP end record, which makes `java -jar` report
`Invalid or corrupt jarfile`; `verify_pdfa.py` trims the trailing launcher before caching the jar.
The installer itself is never executed.

## Verified behavior

- All six profiles write `%PDF-1.7`; the XMP packet declares the exact `pdfaid:part` (2 or 3) and
  `pdfaid:conformance` (A, B or U) in the PDF/A identification namespace. The catalog carries
  `/OutputIntents` with `/S /GTS_PDFA1` and a decodable ICC profile whose header contains `acsp`.
- Every output is tagged: `/MarkInfo << /Marked true >>`, the requested catalog `/Lang`,
  `/ViewerPreferences << /DisplayDocTitle true >>`, a populated `/ParentTreeNextKey`, and the
  structure order `Document` → `H1` → `P` → `Figure` → `P`. The figure `/Alt` is exact, and the
  decorative content never appears in the structure tree.
- Fonts are embedded (`/FontFile2`) and carry `/ToUnicode` maps. pypdf extracts the heading text
  from every profile, so the Unicode claim is observable, not just declared.
- PDF/A-3 writes the PDF-305 attachment into the embedded-files name tree with its exact bytes,
  declared media type, `Data` associated-file relationship and a modification date, and lists the
  file specification in the catalog `/AF` array. PDF/A-2 and a missing profile reject attachments
  with `ArgumentException` before output.
- An A level without `DocumentMetadata.Language` throws `PdfOperationException` and names the
  profile. A level requested in the call that differs from the settings throws
  `InvalidOperationException`. Calling `GeneratePdfA()` without any selection throws
  `InvalidOperationException`. A rejected call leaves the caller's stream untouched.
- `DocumentSettings.PdfAConformance = None` overrides the legacy `DocumentMetadata.PdfA` flag; the
  legacy flag and `PdfAConformance.Legacy` both resolve to PDF/A-2B and write `2`/`B` explicitly.
- `EnableAdditionalCompression`, `Linearize` and `DisableCompression` are rejected with
  `NotSupportedException` before rendering, because the qpdf pass may rewrite the structure tree and
  conformance metadata. `ZIndex` content is rejected by the inherited tagged-capture rules.
- Settings-selected profiles are honored by both `GeneratePdfA()` and `GenerateTaggedPdf()`.
  `GeneratePdf` (the core Skia writer) rejects a selectable profile with `NotSupportedException`
  instead of producing an unvalidated claim.
- Byte/string/stream overloads and the two-stage `GenerateTaggedContent()` → `ToPdfA()` path produce
  the same profile; caller-owned output streams stay open.

## Artifacts

`ACC-402` writes beneath `Source/FossPDF.UnitTests/bin/Release/net9.0/parity-artifacts/ACC-402/`:

- `pdfa-2a.pdf`, `pdfa-2b.pdf`, `pdfa-2u.pdf`, `pdfa-3a.pdf`, `pdfa-3b.pdf`, `pdfa-3u.pdf`
- `pdfa-3a-attachment.pdf`, `pdfa-3b-attachment.pdf`, `pdfa-3u-attachment.pdf`

The examples write beneath `Source/FossPDF.Examples/bin/Release/net9.0/pdfa/`:
`report-pdfa-2b.pdf`, `report-pdfa-3u.pdf`, `legacy-pdfa.pdf`, plus the temporary `invoice.xml` and
`payload.txt` inputs they create.

## Reproduce

From `Source`:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PdfATests
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PdfAExamples
dotnet run --project FossPDF.Quality/FossPDF.Quality.csproj -c Release -- --mode all --output ../artifacts/quality
python -m pip install -r FossPDF.UnitTests/Parity/requirements-pdfa-validation.txt
python FossPDF.UnitTests/Parity/verify_pdfa.py FossPDF.UnitTests/bin/Release/net9.0/parity-artifacts/ACC-402 --reports ../artifacts/pdfa
```

`verify_pdfa.py` needs Java 17 or newer on `PATH`; `--verapdf <path>` points at an existing veraPDF
executable instead, and `--skip-verapdf` runs only the pypdf checks. On first use it downloads the
installer from the pinned URL, checks its SHA-256 and extracts the CLI jar.

## Boundaries

- `FossPDF.PdfOperations` and therefore all PDF/A APIs target `net9.0` only, per ADR-0001. The core
  package exposes the settings enum and the tagged capture on both targets, but `GeneratePdf` does
  not write PDF/A.
- The output intent is PDFsharp's sRGB ICC profile. A different print-condition profile is not
  exposed.
- PDFsharp writes a PDF/A-1 identification; the writer replaces the two single-character values with
  the selected part and level after saving. Both replacements are equal-length ASCII, so object
  offsets do not change. A missing or unexpected value throws `PdfOperationException`.
- The writer always emits tagged structure, including for B levels; custom structure roles and role
  maps are not supported. The semantic API and its limitations are documented in
  [PDF_ACCESSIBILITY.md](../PDF_ACCESSIBILITY.md).
- PDF/A-4, PDF/UA-1 and ZUGFeRD/Factur-X packaging are separate work items (FP-403 and FP-404). A
  PDF/A file is not automatically PDF/UA conforming, and the legacy boolean is never presented as
  its own validated profile.
- The quality tool's own optional qpdf/veraPDF checks were skipped because neither executable is on
  this machine's `PATH`; the separate verifier above supplies the profile validation. The quality
  report uses ordinary PDF output and makes no conformance claim.
