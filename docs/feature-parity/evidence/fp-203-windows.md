# FP-203 Windows validation

Recorded October 26, 2026, for [FP-203 / issue #17](https://github.com/samuelkim-scz/FossPDF.Net/issues/17), based on main commit 8a7c158.

Environment: Windows x64; .NET SDK 9.0.305; .NET 8.0.22 and .NET 9.0.9 runtimes. Core targets and package/native dependencies are unchanged.

## Results

| Check | Result |
| --- | --- |
| Restore / Release build with --warnaserror | Passed; zero warnings/errors |
| Complete .NET 8 suite | 245 passed, 0 failed, 26 planned/skipped |
| Complete .NET 9 suite | 246 passed, 0 failed, 26 planned/skipped |
| ImageExportTests | 31 passed per framework |
| Image export plus shared corpus category | 46 passed per framework; 26 planned/skipped |
| .NET 9 examples | 2 passed; PNG/JPEG/WebP file and caller-stream output exercised |

Tests decode the selected formats and check dimensions, two-page ordering, alpha preservation, translucent/opaque background compositing, page-background precedence, JPEG's default white matte, quality effects, lossless WebP, unchanged PNG defaults, WebP dimension boundaries, invalid settings, independent concurrent backgrounds and caller-owned non-seeking streams on success/failure.

The authored grayscale signal has lower decoded error at quality 90 than at 10 for JPEG and lossy WebP; WebP quality 100 matches its rendered pixels exactly. These are fixture-specific assertions rather than universal file-size/quality promises.

OUT-203 is active and writes two 240x200 pages in each format. JPEG page 1 and WebP page 2 decoded PNGs, plus the low-quality JPEG grayscale output, were visually inspected. Their page colors/order and placement match the expected inputs. The remaining 26 feature contracts remain planned.

## Reproduce

From Source:

~~~powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --logger trx --results-directory ../artifacts/fp203/full
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~DocumentSettingsExamples --logger trx --results-directory ../artifacts/fp203/examples
~~~

Generated encoded images, decoded previews, file-workflow outputs and quality fixtures are under the test assemblies' parity-artifacts directories. Example output is under settings-example/image-exports. CI uploads these with the TRX logs for each OS.

See [the image export guide](../IMAGE_EXPORT.md) for API behavior, limits, validation and ownership rules.
