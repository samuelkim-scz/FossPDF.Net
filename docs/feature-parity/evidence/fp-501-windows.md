# FP-501 Windows qualification

Recorded October 30, 2026 for [issue #30](https://github.com/samuelkim-scz/FossPDF.Net/issues/30). This is local Windows evidence; cross-platform CI remains a merge gate. The protocol and supported workflows are documented in [PREVIEW_INSPECTION.md](../PREVIEW_INSPECTION.md).

## Environment and commands

Windows x64, .NET SDK 9.0.305, .NET 9.0.21 runtime, SkiaSharp 3.119.4 and HarfBuzzSharp 8.3.1.5. Core targets remain netstandard2.1/net9.0 and tests target net8.0/net9.0. Local builds used an isolated artifacts directory because the restricted process could not overwrite existing build outputs.

From `Source`:

```powershell
dotnet restore FossPDF.sln --artifacts-path artifacts/fp501-build --ignore-failed-sources
dotnet build FossPDF.sln -c Release --no-restore --warnaserror --artifacts-path artifacts/fp501-build
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-restore --artifacts-path artifacts/fp501-build --logger trx --results-directory artifacts/fp501-results
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --artifacts-path artifacts/fp501-build --filter "FullyQualifiedName~PreviewInspectionTests|FullyQualifiedName~PreviewProtocolTests|Name~Parity_DEV-501"
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --artifacts-path artifacts/fp501-build --filter FullyQualifiedName~PreviewInspectionExamples
dotnet artifacts/fp501-build/bin/FossPDF.Quality/release/FossPDF.Quality.dll --mode all --output artifacts/fp501-quality
dotnet run --project FossPDF.NativePrototypes/FossPDF.NativePrototypes.csproj -c Release --no-restore -- --output artifacts/fp501-native
python FossPDF.NativePrototypes/verify_artifacts.py artifacts/fp501-native
python FossPDF.NativePrototypes/audit_dependencies.py FossPDF.NativePrototypes/obj/project.assets.json artifacts/fp501-native
```

The unit-test run and a subsequent quality `--mode conformance` run included the portable qpdf 12.4.2 directory on PATH. Existing Java validator-dependent tests were skipped locally; CI installs pinned veraPDF and Mustang and must pass those cases before merge.

## Results

| Check | Result |
| --- | --- |
| Release solution build | Pass, zero warnings/errors |
| Full .NET 8 suite | 509 passed, 12 skipped, zero failed |
| Full .NET 9 suite | 688 passed, 19 skipped, zero failed |
| Final focused inspection tests and DEV-501 | .NET 8: 10 passed; .NET 9: 17 passed; zero skipped/failed |
| Runnable snapshot example | 1 passed |
| Visual baseline | All four scenarios passed |
| Performance budgets | All four scenarios passed |
| Structural PDF baseline | qpdf 12.4.2 passed |
| Native prototypes | All five scenarios passed |
| Independent native/PDF inspection | pypdf 6.21.2: 28 checks passed |
| Dependency and notice audit | 20 packages, six native packages; lock, assets and notice hashes passed |
| Previewer tool package | Required managed assemblies/notices present; 38 native files; all desktop HarfBuzz native packages aligned to 8.3.1.5; packaged licence matches vendored SHA-256 `89101e35a8c66fd4d6dffc1763259161d35cb564c169714ec227a768c89f2938` |

The full-suite skip totals include planned/framework-specific fixtures and the existing external-validator cases; they are not parity passes. The new inspection coverage has no skipped cases on its supported frameworks.

## Acceptance evidence

`DEV-501` exports a three-page document with 113 hierarchy nodes and 168 painted occurrences. Its indented JSON is 93,403 bytes on this checkout; source paths vary by build. JSON, summary and reviewed page PNGs are in `Source/artifacts/fp501-build/bin/FossPDF.UnitTests/release_net*/parity-artifacts/DEV-501`. The page image was inspected visually and shows the repeated heading and four rows with the expected spacing. Automated comparison requires exact pixel equality between ordinary and inspected preview pictures.

Tests verify deterministic IDs, compiler caller file/line/member in Release, repeated header geometry, transformed bounds, scalar configuration, lazy/inline/dynamic elements, balanced columns without speculative occurrences, a document over 50 pages, 20 refreshes, duplicate IDs and lowered resource limits. Both successful replacement and window disposal release the old SKPictures. Partial decoding, failed dispatch and rejected HTTP uploads release unaccepted resources. A weak-reference check verifies released snapshots do not retain document closures.

The real loopback HTTP test negotiates capabilities, uploads multipart pictures and inspection, checks the received hierarchy and rejects a browser Origin request. Legacy commands remain accepted without inspection; unknown versions, missing/invalid pictures, invalid page descriptors and invalid inspection references/geometry are rejected. A picture-only error/legacy update clears stale inspection. An older server without capabilities produces the explicit update-or-disable-inspection error before uploading source data.

CI runs these tests and the runnable example on Windows, Linux and Intel macOS, and uploads DEV-501 and example artifacts. Interactive inspection controls remain FP-502.
