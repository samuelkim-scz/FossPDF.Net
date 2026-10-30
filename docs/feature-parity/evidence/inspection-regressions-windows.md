# Inspection regression qualification

Recorded October 30, 2026 against the accepted FP-501 implementation at `47803ec`. The public `Inspect` and `GenerateInspectedPreview` APIs and protocol version 1 are preserved. Cross-platform CI is a merge gate.

## Reproduced defects

All three initial regression tests failed on the unmodified base on both .NET 8 and .NET 9:

- Eight deferred rows in balanced columns produced zero inspection markers/occurrences.
- A collector kept an otherwise released element tree alive through strong identity maps.
- A node could name itself as its parent and pass protocol validation.

The fixes keep the inspection association while a proxy canvas switches between speculative measurement and real drawing, scope generated children during measurement, use weak element-key lookups, and validate parent references before inserting the current ID. Generated runtime children receive deterministic slot indexes, preventing distinct dynamic layout helpers from sharing the same structural path.

## Results

Windows x64; .NET SDK 9.0.305, .NET 9.0.21 runtime, SkiaSharp 3.119.4 and HarfBuzzSharp 8.3.1.5.

| Check | Result |
| --- | --- |
| Release solution build, warnings as errors | Zero warnings/errors |
| Full .NET 8 suite | 509 passed, 12 existing planned/framework skips, zero failed |
| Full .NET 9 suite | 682 passed, 19 existing planned/validator skips, zero failed |
| Balanced-column fixture | Eight distinct occurrences; expected two-column coordinates and 90-by-20-point bounds; exact pixel equality with ordinary preview |
| Dynamic helper fixture | Only the selected content paints; distinct helper slots and IDs stable across captures |
| Runtime-tree collection | Released root is collected while collector remains alive |
| Parent reference validation | Self-parent rejected |
| FP-003 quality | All four visual scenarios and all four performance budgets pass |
| Structural PDF check | qpdf 12.4.2 passes |
| Native prototypes | All scenarios pass |
| Independent PDF inspection | pypdf 6.21.2: 28 checks pass |
| Native asset/notice audit | 20 packages, six native packages; lock, files and notice hashes pass |

Existing Java-validator cases were skipped locally; CI installs the pinned veraPDF and Mustang validators and runs them. No skipped test is counted as parity evidence. Existing inspection tests also cover legacy HTTP compatibility, atomic malformed-request rejection, source capture, repeated headers, deferred/merged content and 20 refresh/disposal cycles.

## Reproduction

From `Source`, using the existing portable qpdf directory on PATH:

```powershell
dotnet restore FossPDF.sln --artifacts-path artifacts/inspection-hardening --ignore-failed-sources
dotnet build FossPDF.sln -c Release --no-restore --warnaserror --artifacts-path artifacts/inspection-hardening
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --artifacts-path artifacts/inspection-hardening --logger trx --results-directory artifacts/inspection-hardening-results
dotnet artifacts/inspection-hardening/bin/FossPDF.Quality/release/FossPDF.Quality.dll --mode all --output artifacts/inspection-hardening-quality
dotnet run --project FossPDF.NativePrototypes/FossPDF.NativePrototypes.csproj -c Release --no-restore -- --output artifacts/inspection-hardening-native
python FossPDF.NativePrototypes/verify_artifacts.py artifacts/inspection-hardening-native
python FossPDF.NativePrototypes/audit_dependencies.py FossPDF.NativePrototypes/obj/project.assets.json artifacts/inspection-hardening-native
```

The failing baseline reports are saved locally as `inspection-before_net*.trx`; passing full-suite reports are `inspection-suite_net*.trx`. The focused test filter is `FullyQualifiedName~InspectionRegressionTests`. Documentation and the version 1 contract remain in [PREVIEW_INSPECTION.md](../PREVIEW_INSPECTION.md).
