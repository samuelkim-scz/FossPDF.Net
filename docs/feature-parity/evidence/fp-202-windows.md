# FP-202 Windows validation

Recorded October 26, 2026, for [FP-202 / issue #16](https://github.com/samuelkim-scz/FossPDF.Net/issues/16), based on main commit 6579f28 plus the `feature/fp-202-image-optimization` branch.

Environment: Windows x64; .NET SDK 9.0.305; .NET 8.0.x and .NET 9.0.x runtimes. No package, native dependency or target-framework changes.

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Passed; zero warnings/errors |
| Complete .NET 8 suite | 267 passed, 0 failed, 24 planned/skipped |
| Complete .NET 9 suite | 268 passed, 0 failed, 24 planned/skipped |
| New `ImageOptimizationTests` per framework | 11 passed |
| Parity category per framework | 17 passed, 0 failed, 24 planned/skipped |
| Shared image fixture | `IMG-202` active and passing |
| .NET 9 image-optimization examples | 2 passed |

## Fixture evidence

`IMG-202` draws one shared 800x400 opaque quadrant image twice at 80x40 points, with DPI 72/quality 40 and DPI 144/quality 90. Independent PDF inspection of both target-framework artifacts finds:

| Embedded image | Width | Height | Filter |
| --- | --- | --- | --- |
| 72 DPI, quality 40 | 80 | 40 | `/DCTDecode` |
| 144 DPI, quality 90 | 160 | 80 | `/DCTDecode` |

The decoded page raster retains red/green above blue/yellow within the declared lossy tolerances, and the shared source remains 800x400. Artifacts are under `parity-artifacts/IMG-202/` (`document.pdf`, `page-1.png`) for both frameworks.

`ImageOptimizationTests` additionally covers 72/144 DPI resampling, the no-upscale cap, UseOriginalImage, different quality values producing different decoded pixels, transparent-pixel preservation, descriptor range validation, document defaults for static and dynamic images (`ImageRasterDpi = 144`, `ImageCompressionQuality = 90`), per-image values overriding those defaults, shared-source immutability and playback of a processed image from a preview picture after its temporary SKImage is disposed. The examples emit `per-image.pdf` and `document-defaults.pdf` under `image-optimization-example/`.

Opaque sources take the JPEG/DCTDecode path. Images with alpha use WebP for the lossy stage and are stored by the PDF backend as lossless FlateDecode with alpha preserved; quality affects decoded color but does not guarantee a smaller PDF for that path. This limitation is recorded in the guide.

## Reproduce

From `Source`:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --logger trx --results-directory ../artifacts/fp202/full
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity --logger trx --results-directory ../artifacts/fp202/parity
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~ImageOptimizationExamples --logger trx --results-directory ../artifacts/fp202/examples
```

Local TRX logs and generated PDFs/PNGs are ignored artifacts. CI provides platform-specific results and downloadable parity evidence. The remaining 24 planned feature contracts stay skipped.

See [the image optimization guide](../IMAGE_OPTIMIZATION.md) for API behavior, encoding rules, precedence and ownership.
