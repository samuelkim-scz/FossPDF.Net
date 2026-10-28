# FP-306 Windows validation

Recorded October 28, 2026, for [FP-306 / issue #25](https://github.com/samuelkim-scz/FossPDF.Net/issues/25), on `feature/fp-306-pdf-compression` based on `feature/fp-305-pdf-attachments` at commit `890be4e`.

Environment: Windows x64, .NET SDK 9.0.305 (SDKs 8.0.319/9.0.102/9.0.305/10.0.301 installed), .NET 9.0.9 runtime, SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.5, PDFsharp 6.2.4 (MIT, exact pin). The external optimizer was qpdf 12.4.2, installed with winget under `C:\Program Files\qpdf 12.4.2\bin\qpdf.exe`; the installer does not add that directory to `PATH`, so the tests and examples resolve it through the explicit path (`QpdfExecutablePath`/`UseQpdfExecutable` and the test helper's installer-directory fallback). Independent inspection used Python 3.13 with pypdf 6.21.2 and qpdf 12.4.2; the script and interpreter directories are separate from the inspected artifacts and the paths are passed as arguments.

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Zero warnings, zero errors |
| Complete .NET 8 suite | 482 passed, 0 failed, 13 skipped (7 planned contracts + 6 net9.0-scoped fixtures) |
| Complete .NET 9 suite | 609 passed, 0 failed, 7 skipped (planned contracts) |
| Parity category | 28 passed / 13 skipped on .NET 8; 34 passed / 7 skipped on .NET 9 |
| `PDF-306` fixture | Active and passing on .NET 9; skipped on .NET 8 by declared framework scope |
| `PdfOptimizationTests` (core) | 18 cases passed on both frameworks |
| `PdfDocumentOptimizationTests` (companion) | 8 passed on .NET 9 |
| `DocumentSettingsTests` optimization cases | Passing on both frameworks |
| PDF compression examples | 4 passed |
| `FossPDF.Quality --mode all` | All requested checks passed; qpdf/veraPDF conformance checks skipped because the quality tool does not discover them |
| Independent pypdf + qpdf inspection | 17 checks passed: pages, page boxes, decoded content and title equal; `/Linearized` and `/ObjStm` present; `qpdf --check` and `--check-linearization` clean |

## Verified behavior

- `DocumentSettings.EnableAdditionalCompression`, `Linearize`, `CompressDocument = false` and `QpdfExecutablePath` drive the optimizer. The public `PdfOptimizationSettings` and `PdfOptimizer.Optimize` expose the same post-processing directly.
- Additional compression runs qpdf with `--object-streams=generate --recompress-flate --compression-level=9`; disabling compression runs `--stream-data=uncompress --object-streams=disable`; linearization adds `--linearize`. The generated output contains `/ObjStm` and `/Linearized` as expected, and `qpdf --check-linearization` reports `no linearization errors`.
- Optimization is applied after rendering and after every companion operation (selection, merge, overlays, attachments, XMP extension, decryption). Attachments and extended XMP survive qpdf: the companion test reads the exact attachment bytes and the `Supplement` relationship back from an optimized document, and the XMP extension is still present and parseable.
- Page content streams, page dimensions (within 0.01 point) and the document title are unchanged by both the generated and existing-document paths.
- qpdf exit code 0 and 3 are accepted (3 means warnings); 1 and 2 raise `PdfOptimizationException` with truncated stderr. The real process backend was exercised with a deliberate failing invocation.
- A missing executable (`NotSupportedException`), a qpdf older than 11.0 and an unrecognized version string are rejected before composition. Contradictory options (`EnableAdditionalCompression` with `DisableCompression`, or `CompressDocument = false` with `EnableAdditionalCompression`) throw `ArgumentException` before composition.
- Optimized generation renders into memory first, so failures leave the caller stream untouched (verified by writing a sentinel byte and asserting it is still there). `DocumentOperation.Save(Stream)` behaves the same way.
- Process cancellation and the configurable timeout terminate qpdf and surface `OperationCanceledException` / `PdfOptimizationException`.
- `DocumentOperation` rejects optimization combined with encryption in either call order with `InvalidOperationException`, because qpdf would otherwise silently write an unencrypted document.

## Measured sizes

The acceptance requires measurement rather than assumed improvement. Recorded from the runnable examples and the fixture:

| Document | Before | After | Delta |
| --- | --- | --- | --- |
| Generated three-page text report | 90,820 B | 88,865 B | -1,955 B |
| Image-heavy report (three 600x400 images) | 67,098 B | 65,360 B | -1,738 B |
| Existing one-page invoice with an attachment | 76,841 B | 77,520 B | +679 B |
| One-page invoice saved with `CompressDocument = false` | 76,841 B | 153,854 B | +77,013 B |
| `PDF-306` fixture, generated path | 1,233 B | 2,031 B | +798 B |
| `PDF-306` fixture, existing path | 1,233 B | 3,645 B | +2,412 B |

Exact byte counts can vary by a few bytes between runs because generated documents carry fresh
identifiers; the deltas above are from the recorded run.

The small fixture grows because object streams and linearization hint tables dominate a two-page 1.2 KB document. The existing-document path additionally passes through PDFsharp before qpdf. The guide documents both effects; no universal size guarantee is claimed.

## Independent validation

`verify_pdf_compression.py` reads the fixture artifacts with pypdf, a separate implementation from PDFsharp, and runs qpdf as an independent structural checker:

```text
FP-306: 17 independent compression checks passed (pypdf 6.21.2, qpdf version 12.4.2).
{
  "sizes": { "source.pdf": 1233, "generated.pdf": 2031, "optimized.pdf": 3645 },
  "qpdf --check": "File is linearized / No syntax or stream encoding errors found",
  "qpdf --check-linearization": "no linearization errors"
}
```

Both optimized outputs keep every page box and decoded content stream byte-for-byte identical to the source, keep the document title, carry the linearization dictionary and use object streams. The report is written to `independent-validation.json` next to the artifacts.

## Artifacts

`PDF-306` writes beneath `Source/FossPDF.UnitTests/bin/Release/net9.0/parity-artifacts/PDF-306/`: `source.pdf`, `generated.pdf`, `optimized.pdf`, `measurements.txt` and `independent-validation.json`.

The examples write beneath `Source/FossPDF.Examples/bin/Release/net9.0/`:

- `pdf-optimization-generated/` (`report.pdf`, `report-optimized.pdf`)
- `pdf-optimization-images/` (`image-report.pdf`, `image-report-optimized.pdf`)
- `pdf-optimization-existing/` (`invoice.pdf`, `terms.txt`, `invoice-optimized.pdf`)
- `pdf-optimization-uncompressed/` (`invoice.pdf`, `invoice-uncompressed.pdf`)

## Reproduce

From `Source`:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore -f net9.0 --filter TestCategory=PdfOptimization
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PdfCompressionExamples
dotnet run --project FossPDF.Quality/FossPDF.Quality.csproj -c Release -- --mode all --output ../artifacts/quality
python FossPDF.UnitTests/Parity/verify_pdf_compression.py FossPDF.UnitTests/bin/Release/net9.0/parity-artifacts/PDF-306
```

The tests and examples that need qpdf report an explicit skip when it is not installed. CI installs qpdf 11+ on Ubuntu (apt), macOS (Homebrew) and Windows (Chocolatey) before running the tests, then runs the pypdf/qpdf verification step.

## Boundaries

- qpdf is an optional external executable, not a distributed dependency; FossPDF does not ship it and does not control its version beyond requiring 11.0+. See [NATIVE_DEPENDENCIES.md](../NATIVE_DEPENDENCIES.md).
- The companion `DocumentOperation` API targets `net9.0` only per ADR-0001. The core settings and `PdfOptimizer` exist in both core targets, and no core package reference was added.
- Optimization does not make PDF/A or PDF/UA conformance claims, and it is rejected together with encryption in the companion.
- Size improvements are workload-dependent. Text-heavy and image-heavy output shrank by roughly 2% and 2.6%; tiny documents grew. Image bytes are already compressed, so the gain comes mostly from the document structure.
- qpdf and veraPDF are not installed for `FossPDF.Quality`, so the quality tool still skips its conformance checks; linearization is validated by the fixture, unit tests and the independent script instead.

## Integrated review after FP-305 selection

The branch now integrates the reviewed attachment implementation merged in #60, replacing the superseded #58 code and guide. Stream/byte attachments, namespace-preserving XMP, and the protected-metadata bridge remain intact. Compression runs after the completed attachment/XMP revision and preserves it. The adapter stages PDFs in owner-only Unix directories (both framework paths checked in CI), and actual waiting processes are terminated by cancellation and timeout tests. Windows CI explicitly exports the installed qpdf directory for the Python inspector.

Independent validation on the integrated tree: 47 layer checks, 36 security checks, 91 attachment/XMP checks with MuPDF, and 17 qpdf/pypdf compression checks pass. All four compression examples pass. Windows visual/performance checks pass, including the qpdf structural check; veraPDF is unavailable. The local qpdf 12.4.2 tool was obtained as the official portable release and its SHA-256 verified, without installing it system-wide. The Release solution build has zero warnings/errors; before adding the two real-process termination cases, the full suites passed 483 tests / 13 skips on net8.0 and 617 / 7 on net9.0. The final focused optimizer suites pass 21 / 29 cases respectively, including the new termination cases.
