# FP-103 Windows validation

Recorded October 26, 2026, for [FP-103 / issue #8](https://github.com/samuelkim-scz/FossPDF.Net/issues/8), based on main commit 6579f28 plus the `feature/fp-103-font-features-decorations` branch.

Environment: Windows x64; .NET SDK 9.0.305; .NET 8.0.x and .NET 9.0.x runtimes. No package, native dependency or target-framework changes.

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Passed; zero warnings/errors |
| Complete .NET 8 suite | 263 passed, 0 failed, 24 planned/skipped |
| Complete .NET 9 suite | 264 passed, 0 failed, 24 planned/skipped |
| New `TextFeatureTests` per framework | 7 passed |
| Parity category per framework | 17 passed, 0 failed, 24 planned/skipped |
| Shared text fixture | `TXT-103` active and passing |
| .NET 9 text-feature examples | 2 passed |

## Fixture evidence

`TXT-103` emits `document.pdf` plus decorated/plain page rasters beneath `parity-artifacts/TXT-103/` for both target frameworks.

- Lato shaped `office` into six glyphs with the fixture's `liga=0` setting and five glyphs when liga was enabled independently.
- Disabling `kern` changed the measured `AV` advance, confirming the feature reached HarfBuzz shaping.
- Adding decoration settings did not change the shaped glyph count.
- The decorated and plain pages produced identical black glyph bounds, so the decoration did not affect text advance or wrapping.
- The dashed `#cc0000` underline at thickness 1.5 produced multiple painted column runs separated by gaps, with a pixel close to the requested color.

`TextFeatureTests` additionally covers style mutation for all controls, last-feature-wins ordering, all five decoration styles, thickness scaling, repainting on wrapped lines, and superscript/subscript decoration placement. The examples emit `font-features.pdf` and `decorations.pdf` under `text-feature-example/`.

OpenType features affect shaping, measurement, line breaking, drawing, fallback-style inheritance and subsetting through the shared text-style key. Decorations are painted rectangles sized to the measured run; wavy output is an approximation assembled through the public canvas contract. Custom dash/gap lengths remain future work.

## Reproduce

From `Source`:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --logger trx --results-directory ../artifacts/fp103/full
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity --logger trx --results-directory ../artifacts/fp103/parity
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~TextFeatureExamples --logger trx --results-directory ../artifacts/fp103/examples
```

Local TRX logs and generated PDFs/PNGs are ignored artifacts. CI provides platform-specific results and downloadable parity evidence. The remaining 24 planned feature contracts stay skipped.

See [the text features guide](../TEXT_FEATURES.md) for API behavior, inheritance, decoration limits and examples.
