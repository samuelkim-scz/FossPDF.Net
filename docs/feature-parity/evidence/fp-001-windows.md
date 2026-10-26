# FP-001 initial Windows evidence

Recorded on October 26, 2026 for [FP-001 / issue #2](https://github.com/samuelkim-scz/FossPDF.Net/issues/2).

- Git base: c336b1e (published roadmap); branch: feature/fp-001-parity-fixtures.
- Reference target: QuestPDF 2026.9.1; original FossPDF baseline: 0339255.
- Host: Windows x64; .NET SDK 9.0.305.
- Test runtimes: .NET 8.0.22 and .NET 9.0.9.
- Rendering dependencies: SkiaSharp 3.119.4, HarfBuzzSharp native assets 8.3.1.5; text fixture uses bundled Lato.
- Catalog schema: 1; eight active scenarios and 27 planned contracts.

## Commands and results

Run from Source:

~~~powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --logger trx --results-directory ../artifacts/fp001/full
~~~

| Check | Result |
| --- | --- |
| Restore | All projects up to date |
| Release solution build | Passed; zero warnings, zero errors |
| .NET 8 complete suite | 169 passed, 0 failed, 27 planned/skipped |
| .NET 9 complete suite | 170 passed, 0 failed, 27 planned/skipped |
| New executable parity checks per framework | 13 passed: eight scenarios, one catalog/traceability check and four raster-comparison checks |
| Planned contracts | All 27 reported as skipped with owning work-item IDs; none counted as a parity pass |

The geometric comparisons passed for explicit pagination, overflow with repeated header/footer, raster quadrant placement and 144-DPI output. Independent reference images use authored rectangles rather than captured FossPDF output.

The generated TXT-001, PAG-002 page 1 and IMG-001 PNGs were visually inspected. The colored text runs render in the intended order and style; the pagination marker blocks, header/footer and four image quadrants match the specified placement.

Rendering fixtures emit PDFs, actual PNGs and applicable reference PNGs beneath each test framework's parity-artifacts directory. The TRX logs and attachments are local ignored artifacts; subsequent CI runs upload the same evidence for review.

## Font-subsetting regression discovered by CI

The [initial cross-platform run](https://github.com/samuelkim-scz/FossPDF.Net/actions/runs/40389499970) exposed an existing Linux/macOS failure in the font-style preservation test. The new parity fixtures passed. HarfBuzz's [default name-ID filter](https://github.com/harfbuzz/harfbuzz/blob/8.3.1/src/hb-subset-input.cc) retains IDs 0-6, discarding the typographic family/subfamily names in IDs 16/17. Different Skia platform backends then resolve the subset fonts into different families.

The subset builder now preserves IDs 16/17. The existing regression test also compares the original and subset typographic name records, so Windows detects the loss as well. This strengthened check failed before the fix and passed afterward. The warning-as-error solution build and complete .NET 8/.NET 9 suites were rerun successfully with the same counts above; logs are under artifacts/fp001/font-fix. The CI matrix now lets each OS finish independently. Follow-up cross-platform results are linked from [PR #34](https://github.com/samuelkim-scz/FossPDF.Net/pull/34).

## Scope of this evidence

This establishes the initial executable corpus on Windows. PDF counts are checked through the generation API and raster output; this run does not perform independent PDF structure, accessibility, PDF/A, encryption or invoice validation. Their catalog scenarios remain planned. Linux/macOS results come from CI and broader platform/performance qualification remains in FP-003/FP-503.
