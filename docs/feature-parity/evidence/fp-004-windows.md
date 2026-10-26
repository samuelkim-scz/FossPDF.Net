# FP-004 Windows validation

Recorded October 26, 2026, for [FP-004 / issue #5](https://github.com/samuelkim-scz/FossPDF.Net/issues/5), based on main commit 2f46dac.

Environment: Windows x64, .NET SDK 9.0.305, .NET 8.0.22 and .NET 9.0.9 runtimes. Existing package/native dependencies are unchanged.

## Results

| Check | Result |
| --- | --- |
| Restore | All solution projects up to date |
| Release solution build with --warnaserror | Zero warnings, zero errors; all existing samples compiled |
| Complete .NET 8 suite | 212 passed, 0 failed, 27 planned/skipped |
| Complete .NET 9 suite | 213 passed, 0 failed, 27 planned/skipped |
| New settings tests per framework | 42 passed |
| Shared settings fixture | SET-004 passed; nine total active corpus scenarios |
| .NET 9 DocumentSettingsExamples consumer | 1 passed; generated PDF and two PNG resolutions |

The deterministic concurrency tests synchronize separate generation calls during composition and assert different DPI, direction, caching/debug/glyph policies and page limits. They also assert that shared caller metadata and process defaults are unchanged. Other cases check the unchanged IDocument contract, one-time metadata reads/snapshots, precedence, inclusive page limits, explicit page/element direction, validation, reserved feature guards and caller-owned streams.

SET-004 compares its 144-DPI output to independently authored geometry while confirming that caller metadata stays at 36 DPI. Its generated PNG was visually inspected and matches the expected red block at (20,20,200,40) on a 240x200 page.

The missing-glyph diagnostic now disposes native typefaces it temporarily opens while suggesting fallback fonts. Existing borrowed document fonts retain their ownership rules.

## Reproduce

From Source:

~~~powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --logger trx --results-directory ../artifacts/fp004/final
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~DocumentSettingsExamples --logger trx --results-directory ../artifacts/fp004/example-final
~~~

Local TRX logs and generated PDFs/PNGs are ignored artifacts. CI provides platform-specific results and downloadable parity evidence. The 27 planned feature contracts remain skipped; unsupported PDF/A/PDF/UA/compression and JPEG/WebP modes fail explicitly rather than counting as implemented parity.
