# Native backend dependency and ownership inventory

This inventory supports [ADR-0001](decisions/ADR-0001-native-backends.md) and
[FP-002 / issue #3](https://github.com/samuelkim-scz/FossPDF.Net/issues/3). It records the dependency
versions, licences, runtime assets, packaging and ownership contracts that the native-backend
prototypes rely on.

The prototype project `Source/FossPDF.NativePrototypes` is deliberately **outside the solution**,
is not packed, and is not referenced by `FossPDF`. Installing or referencing it does not change the
production dependency graph.

## Core cohort

| Package | Version | Licence | Runtime assets | Role |
| --- | --- | --- | --- | --- |
| SkiaSharp | 3.119.4 | MIT (Xamarin/Microsoft) | Win32, macOS, Linux native libraries | Drawing and PDF/XPS/SVG/PNG output |
| SkiaSharp.HarfBuzz | 3.119.4 | MIT | Uses Skia/HarfBuzz native libraries | HarfBuzz-to-Skia text bridge |
| HarfBuzzSharp | 8.3.1.5 | MIT (Xamarin/Microsoft) | Win32, macOS, Linux native libraries | Shaping |
| IDisposableAnalyzers | 4.0.8 | MIT | Build-time analyzer; no runtime asset | Development diagnostics |

The existing package-manifest work (FP-003's notices follow-up / the packaging branch) records the
shipped notices. The core remains MIT.

## Prototype-only dependency graph

Resolved from `FossPDF.NativePrototypes` on .NET 9, Windows x64:

| Package | Version | Licence | Notes |
| --- | --- | --- | --- |
| Svg.Skia | 4.9.1 | MIT | Copyright © Wiesław Šoltés 2026 |
| Svg.Animation | 4.9.1 | MIT | Transitive; same author |
| Svg.Custom | 4.9.1 | **MS-PL** | Transitive; Copyright © Wiesław Šoltés 2026. **Not MIT** |
| Svg.Model | 4.9.1 | MIT | Transitive; same author |
| Svg.SceneGraph | 4.9.1 | MIT | Transitive; same author |
| ShimSkiaSharp | 4.9.1 | MIT | Transitive; same author |
| ExCSS | 4.3.1 | MIT | Transitive of Svg.Model |
| PDFsharp | 6.2.4 | MIT | Copyright © 2026 empira; includes `UniversalAccessibility` |
| Microsoft.Extensions.Logging.Abstractions | 8.0.3 | MIT | PDFsharp dependency |
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.2 | MIT | Transitive of logging abstractions |
| System.Security.Cryptography.Pkcs | 8.0.1 | MIT | PDFsharp dependency |
| HarfBuzzSharp.NativeAssets.Linux | 8.3.1.5 | MIT | Prototype runtime asset |
| SkiaSharp.NativeAssets.Linux.NoDependencies | 3.119.4 | MIT | Prototype runtime asset |

### Version-resolution note

Svg.Skia 4.9.1 declares SkiaSharp 3.119.2 and HarfBuzzSharp 8.3.1.3. The prototype pins the core's
3.119.4/8.3.1.5; NuGet resolves to the pinned versions and the prototype rendered correctly. A
production SVG companion must declare compatible minimum versions and re-run the prototype and
quality baselines on all supported platforms.

### Licence review required before shipping SVG

`Svg.Skia` itself is MIT, but `Svg.Custom` is **MS-PL**. MS-PL is permissive and OSI-approved, but it
is not MIT and it carries notice obligations. Consequences:

- The core `FossPDF` package stays MIT and must not take a dependency on Svg.Skia without a reviewed
  licence decision.
- A future `FossPDF.Svg` companion package may ship Svg.Skia only after the MS-PL notice and binary
  distribution obligations are accepted and documented; the package must include the Svg.Custom
  copyright and MS-PL notice.
- If MS-PL is not acceptable, the SVG fallback is a different permissively licensed parser or an
  external rasterization step. That decision remains open in FP-201.

`Svg.Custom` is prototype-only in this branch; it is not distributed in any package.

## Notice requirements

The prototype-only packages are not distributed in this branch, so they add no production notice
obligation yet. If an owning issue promotes a companion package, that package must carry:

- the MIT notice for Svg.Skia/Svg.Animation/Svg.Model/Svg.SceneGraph/ShimSkiaSharp/ExCSS and the
  **MS-PL notice for Svg.Custom 4.9.1** (Copyright © Wiesław Šoltés 2026);
- the MIT notice for PDFsharp 6.2.4 (Copyright © 2026 empira) and its Microsoft.Extensions/
  System.Security.Cryptography transitive packages (Copyright © Microsoft Corporation);
- the existing SkiaSharp/HarfBuzzSharp MIT notices and the bundled Lato SIL OFL 1.1 notice already
  handled by the core packaging work.

The core `FossPDF` package remains MIT. The MS-PL dependency must not be introduced into the core.

## Runtime assets and packaging

- SkiaSharp/HarfBuzzSharp already carry the native assets required for Windows, Linux and macOS.
  The prototype adds only the Linux `NoDependencies` Skia asset and the Linux HarfBuzz asset, matching
  the existing unit-test setup.
- Svg.Skia, PDFsharp and their transitive packages are managed assemblies. PDFsharp adds no native
  library.
- PDFsharp text rendering normally depends on platform font resolution. The tagging prototype instead
  registers a `IFontResolver` that reads the bundled `Lato-Regular.ttf`/`Lato-Bold.ttf`, so Linux and
  macOS do not need system fonts or Arial installed.
- PDFsharp page import/reorder does not require a font resolver.
- No package in this branch is packed. The prototype project has `IsPackable=false` and is absent
  from `FossPDF.sln`.

## Upgrade policy

1. Pin exact versions in companion packages; no floating ranges.
2. On every upgrade, review the licence, notices, target frameworks and native assets of the package
   and its transitive graph.
3. Re-run the native prototype matrix (Windows/Linux/macOS), the parity corpus and the FP-003 quality
   baselines before accepting the change.
4. Do not take a major-version upgrade or a licence change without updating ADR-0001 and the notices.
5. Treat a change to a transitive licence (for example MS-PL → MIT or vice versa) as a reviewed
   dependency change, not a routine patch.

## Ownership and error-handling contracts

### Streams

- Caller streams are borrowed. Companion packages must not dispose them; the caller chooses whether to
  read, seek or close.
- SVG parsing reads the supplied text/bytes/stream synchronously; the source object does not retain a
  caller stream after parsing.
- PDF import/output streams must satisfy the capabilities documented by the owning issue. The
  prototype verified that PDFsharp left a `MemoryStream` open after `Save`; that behaviour is part of
  the contract to preserve or wrap explicitly.
- Unsupported/encrypted/damaged input fails before page destinations or output files are selected.

### Native and managed objects

- Svg.Skia `SKSvg`/`SKPicture` and any rasterization surface are owned by the companion and disposed
  deterministically after the image is produced.
- PDFsharp `PdfDocument`/`PdfPage` objects are disposed by the companion; imported pages are cloned
  into the destination document.
- PDF/UA structure objects are owned by the `UAManager` attached to the destination document; every
  `BeginElement` has a matching `End`, and the document is saved only after the structure is complete.
- Font bytes used by the PDFsharp resolver are read once and owned by the resolver; they are not
  disposed by callers.

### Errors and fallback

- Invalid SVG, invalid PDFs, unsupported encryption, missing fonts and unsupported platforms fail with
  a documented exception; there is no silent fallback to a different output format.
- Optional packages must be additive: no new `IDocument`/`ICanvas` members and no changes to the
  existing font, image or stream ownership rules.
- A structural prototype is not a PDF/UA or PDF/A conformance claim. Validator evidence (for example
  veraPDF output) is required before any claim, and those gates remain in FP-401/FP-402/FP-403.

## Provisional companion boundaries

| Companion (working name) | Dependencies | Surface | Owning issues |
| --- | --- | --- | --- |
| `FossPDF.Svg` | Svg.Skia 4.9.1 (pending MS-PL review) | Preloaded SVG source, DPI/background, placement through the image path | FP-201 |
| `FossPDF.PdfOperations` | PDFsharp 6.2.4 | Load, page selection/reorder, merge, save | FP-301, FP-302–FP-306 |
| `FossPDF.Accessibility` or a PDF-operations extension | PDFsharp 6.2.4 UA APIs | Post-process completed PDFs with roles, language, alt text; validator hooks | FP-401, FP-402, FP-403 |

Exact public method names remain with the owning issues. The prototype is evidence for feasibility
and contracts, not an implementation commitment.

## Prototype checks

The prototype writes `prototype-results.json` and artifacts under `artifacts/fp-002/`:

- SVG: Svg.Skia rasterized a fixed SVG; four pixels were checked; the PNG was placed in a one-page
  FossPDF PDF.
- PDF operations: PDFsharp imported two Skia-generated PDFs, reordered their pages to widths
  `[300, 200]`, left the caller stream open, and the output reopened with two pages.
- Tagging: PDFsharp `UAManager`/`StructureBuilder` produced a structure tree root, document element,
  `/StructTreeRoot`, `/StructElem`, `/MarkInfo`, `/MCID`, `/ParentTree`, `BDC`/`EMC` operators and a
  reopenable one-page PDF. This is structural evidence only.

See [evidence/fp-002-windows.md](evidence/fp-002-windows.md) and the ADR for the decision rationale.
