# Native backend prototypes (FP-002)

These prototypes are isolated evidence for
[FP-002](https://github.com/samuelkim-scz/FossPDF.Net/issues/3). They are **not** production
dependencies, are not part of `FossPDF.sln`, and are not packed.

```powershell
dotnet run --project FossPDF.NativePrototypes/FossPDF.NativePrototypes.csproj -c Release -- --output ../artifacts/fp-002
```

The run writes `prototype-results.json` plus SVG/PNG, source/reordered PDFs and a tagged PDF.
Each scenario verifies observable output:

- **SVG input:** Svg.Skia 4.9.1 renders a fixed SVG; sampled pixels are checked and the rasterized
  result is placed into a FossPDF PDF.
- **Existing-PDF operations:** PDFsharp 6.2.4 imports two Skia-generated PDFs, reverses their pages,
  checks the page order/sizes and verifies the caller-owned stream remains open.
- **Semantic tagging:** PDFsharp `UAManager`/`StructureBuilder` creates a structure tree, marked
  content and a language declaration. This verifies structural markers only.

## Licensing

Svg.Skia 4.9.1 is MIT, but its transitive `Svg.Custom 4.9.1` is **MS-PL**, not MIT. The SVG path is
prototype-only pending a reviewed licence decision. PDFsharp 6.2.4 is MIT.

The tagging prototype is **not** a PDF/UA or PDF/A conformance claim; validator evidence remains in
FP-401/FP-402/FP-403.

See [ADR-0001](../../docs/feature-parity/decisions/ADR-0001-native-backends.md),
[NATIVE_DEPENDENCIES.md](../../docs/feature-parity/NATIVE_DEPENDENCIES.md) and
[fp-002-windows.md](../../docs/feature-parity/evidence/fp-002-windows.md).
