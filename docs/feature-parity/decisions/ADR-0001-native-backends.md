# ADR-0001: Native backends and dependency contracts

- Status: Proposed
- Date: 2026-10-26
- Work item: [FP-002 / issue #3](https://github.com/samuelkim-scz/FossPDF.Net/issues/3)
- Target: QuestPDF 2026.9.1
- Baseline: FossPDF `0339255`
- Prototype: `Source/FossPDF.NativePrototypes`
- Evidence: [fp-002-windows.md](../evidence/fp-002-windows.md)

## Context

The core library draws with **SkiaSharp 3.119.4** and shapes with **HarfBuzzSharp 8.3.1.5** through `SkiaSharp.HarfBuzz`. Its `ICanvas` contract can translate, scale, rotate, draw rectangles/text/images, and write links and destinations. `PdfCanvas` wraps `SKDocument.CreatePdf`, which is a **write-only** PDF generator.

The parity target adds capabilities that the current drawing stack does not expose:

| Capability | Current stack | Owning work |
| --- | --- | --- |
| SVG input | No parser; examples rasterize SVG through an external `Svg.Skia` reference | FP-201 |
| Existing-PDF operations | No PDF reader or page model | FP-302–FP-306 |
| Tagged PDF / semantic structure | No structure tree, marked-content or role APIs | FP-401, FP-403 |
| Selectable PDF/A profiles | Only Skia's unqualified `PdfA` flag | FP-402 |
| Font subsetting | Existing optional `FossPDF.FontSubset`/`Glue` packages | Already present |

Constraints from the roadmap:

- Keep FossPDF MIT-licensed and preserve existing public APIs and stream ownership.
- Keep the core `netstandard2.1`/`net9.0` targets; new packages may be .NET 9-only.
- Support Windows, Linux and macOS with redistributable native assets and deterministic cleanup.
- Do not add unreviewed major-version or licence changes.
- A structural prototype is **not** a PDF/UA or PDF/A conformance claim. Those claims require validator evidence in FP-402/FP-403.

## Decision

### 1. Keep SkiaSharp + HarfBuzzSharp as the core drawing and shaping cohort

No core backend replacement is proposed. Skia remains the renderer for PDF, XPS, PNG, SVG and preview output; HarfBuzz remains the shaping engine. Existing `ICanvas`, `IDocument`, font ownership and stream contracts remain unchanged.

### 2. Adopt Svg.Skia 4.9.1 for SVG input, as an optional companion package

`Svg.Skia` parses SVG into an `SKPicture` that is drawn into Skia surfaces. The proposed package boundary is a companion package (working name `FossPDF.Svg`) that:

- preloads SVG text/bytes/streams into a reusable source object;
- rasterizes into an `SKImage`/`Image` at a declared DPI, then uses the existing image path;
- owns and disposes `SKSvg`/`SKPicture`/surface objects after rasterization;
- never becomes a dependency of the core `FossPDF` package.

The prototype rendered a fixed 240×140 SVG with four checked pixels and placed the rasterized result into a one-page FossPDF PDF. Vector-preserving PDF embedding is not selected for the first implementation; the existing `Image`/`ICanvas` path is the integration contract.

**Licence caveat:** Svg.Skia itself is MIT, but its transitive `Svg.Custom 4.9.1` is **MS-PL**, not MIT. Svg.Skia is therefore prototype-only in this branch. A production `FossPDF.Svg` companion may ship it only after the MS-PL notice and binary-distribution obligations are reviewed and accepted. If MS-PL is unacceptable, FP-201 must choose a different parser or an external rasterization path. The core package must not take this dependency.

### 3. Adopt PDFsharp 6.2.4 for existing-PDF operations, as an optional companion package

PDFsharp reads and writes PDFs, imports pages between documents and preserves page media boxes. The proposed package boundary (working name `FossPDF.PdfOperations`) exposes loading, page selection/reordering, merging and saving while leaving streams caller-owned. The core library stays write-only.

The prototype opened two Skia-generated PDFs, imported and reversed their pages into a new document, verified page widths `[300, 200]`, and confirmed that PDFsharp left the caller-owned output stream open. Encryption/password policy, page-range syntax, attachments and XMP remain owned by FP-302–FP-306.

### 4. Adopt PDFsharp 6.2.4 UniversalAccessibility for the semantic-tagging prototype

PDFsharp 6.2.4 (MIT) includes `PdfSharp.UniversalAccessibility.UAManager` and `StructureBuilder`. They produce a structure tree root, document structure element, marked-content references, a parent tree and a mark-information dictionary. The proposed path is a post-processing/backend companion that consumes a completed PDF and applies structure, language and alternate-text declarations.

The prototype created a one-page tagged document with an `Article` → `Heading1`/`Paragraph` structure, embedded Lato through a custom font resolver, and verified the structural markers. This is a **structural prototype**, not PDF/UA-1 conformance. The final tagging API, role mapping and conformance validation belong to FP-401/FP-403.

### 5. Keep optional packages additive

The optional packages must not require new members on `IDocument`/`ICanvas`, must not change font/stream ownership, and must not be referenced by the core package. Consumers opt in by installing the companion package that matches their owning feature.

## Framework availability

- The core `FossPDF` library keeps its existing `netstandard2.1`/`net9.0` targets and API surface.
- The prototype project targets `net9.0` only and is not packed.
- The proposed companion packages would target `net9.0` initially. Svg.Skia 4.9.1 also targets
  `netstandard2.0`/`net6.0`/`net8.0`; PDFsharp 6.2.4 targets `netstandard2.0`/`net8.0`/`net9.0`.
  Multi-targeting a companion for `netstandard2.1` should be considered only after the owning issue
  validates the package graph; the core must not require the companion.
- Unsupported platforms or frameworks fail explicitly; there is no silent fallback in the core.

## Dependency and runtime contract

| Package | Version | Licence | Runtime assets | Role |
| --- | --- | --- | --- | --- |
| SkiaSharp | 3.119.4 | MIT | Win32/macOS/Linux native assets | Core drawing |
| SkiaSharp.HarfBuzz | 3.119.4 | MIT | Uses Skia/HarfBuzz natives | Core shaping bridge |
| HarfBuzzSharp | 8.3.1.5 | MIT | Win32/macOS/Linux native assets | Core shaping |
| Svg.Skia | 4.9.1 | MIT | Managed; uses SkiaSharp natives | SVG prototype / proposed companion |
| Svg.Custom | 4.9.1 | **MS-PL** | Managed | Transitive SVG parser; prototype-only pending licence review |
| PDFsharp | 6.2.4 | MIT | Managed; no new native assets | PDF operations/tagging prototype |
| Microsoft.Extensions.Logging.Abstractions | 8.0.3 | MIT | Managed | PDFsharp dependency |
| System.Security.Cryptography.Pkcs | 8.0.1 | MIT | Managed | PDFsharp dependency |

Full transitive versions, copyrights and the prototype-only notices are recorded in
[NATIVE_DEPENDENCIES.md](../NATIVE_DEPENDENCIES.md).

Version resolution note: Svg.Skia 4.9.1 declares SkiaSharp 3.119.2 and HarfBuzzSharp 8.3.1.3. The
prototype pins the core's 3.119.4/8.3.1.5 and exercises rendering on that combination. Any companion
package must declare the same minimum set and be tested against the core's pins.

## Ownership and error-handling contracts

### SVG

- Caller-provided strings, byte arrays and streams are read synchronously for parsing; streams remain caller-owned.
- The source object owns its `SKSvg`/`SKPicture`; rasterized `SKImage`/`Image` ownership follows the existing image contract.
- Malformed SVG throws an `InvalidDataException`/dedicated exception before any document output; no partial PDF is accepted.
- Rasterization DPI and background are explicit; unsupported SVG features fail or rasterize according to the documented companion behaviour, never silently change the page geometry.

### Existing PDFs

- Caller streams remain open; the companion must not dispose them. Required read/write/seek capabilities are documented per API.
- Imported `PdfDocument`/`PdfPage` objects are disposed by the companion after the output is written; imported pages are cloned into the destination document.
- Encrypted, damaged or unsupported PDFs fail before page destinations are selected. Passwords are never logged.
- The companion does not claim byte-identical round-tripping or PDF/A preservation; the owning issue defines page-range, merge and metadata semantics.

### Tagging

- `UAManager.ForDocument` is called before pages are added; every `BeginElement` has a matching `End`.
- Fonts used for tagged text are loaded through an explicit resolver and embedded; the prototype uses the bundled Lato files, so no system-font resolution is required on Linux/macOS.
- Structure, language and alternate text are declared by the companion; a missing declaration is a validation failure in the owning issue, not silently ignored.
- Validator output (veraPDF) is required before any PDF/UA or PDF/A claim. The prototype only checks structural markers and must not be cited as conformance.

### Native packaging and upgrade policy

- Pin exact package versions; no floating ranges in shipped companion packages.
- Review licence, notices, target frameworks and native assets on every upgrade.
- Run the prototype matrix, the parity corpus and the FP-003 quality baselines before accepting an upgrade.
- Do not take a major-version change or a licence change without updating this ADR.
- Prefer managed-only companions where possible; any new native asset must be packaged for the declared runtime identifiers and included in the notices.

## Integration contract for contributors

The owning issues can implement these additive surfaces without changing the core:

| Companion (working name) | Proposed surface | Owning issues |
| --- | --- | --- |
| `FossPDF.Svg` | Preloaded SVG source (text/bytes/stream), DPI/background options, placement through the existing image path | FP-201 |
| `FossPDF.PdfOperations` | Load, page selection/reorder, merge, save; caller-owned streams | FP-301, FP-302–FP-306 |
| `FossPDF.Accessibility` (or a PDF-operations extension) | Post-process a completed PDF with structure roles, language and alt text; validator hooks | FP-401, FP-402, FP-403 |

Exact method names are illustrative until their issues settle them. The non-negotiable parts are the
package boundary, stream ownership, deterministic disposal, explicit failure, and the separation
between structural output and a conformance claim.

## Consequences

- The core package gains no new dependencies; consumers choose the companion packages they need.
- Svg.Skia and PDFsharp remain prototype-only until their owning issues adopt them.
- Svg.Skia's MS-PL transitive dependency is a licence-review gate for any SVG companion; the core stays MIT.
- PDFsharp provides both PDF reading and a high-level UA structure builder in the same MIT package, so a separate tagging backend is not required for the first implementation path.
- Font resolution for tagged text is the main cross-platform risk; the prototype avoids system fonts by using the bundled Lato files.
- Performance, memory and native-asset impact must be measured in the owning issues and in FP-003 before release claims.

## Alternatives considered

- **Keep SVG/PDF/tagging in the core package.** Rejected: it would add optional dependencies and licensing/native risk to every consumer and change the core's clean drawing contract.
- **PDFium or another native PDF engine.** Rejected for the first path: additional native binaries and packaging burden; PDFsharp is managed and MIT at the pinned version.
- **iText or other dual-licensed PDF libraries.** Rejected: licence obligations conflict with the MIT-only distribution goal.
- **Custom SVG parser or custom PDF structure writer.** Rejected: high implementation and validation cost; Svg.Skia and PDFsharp already exercise the required primitives. The Svg.Skia licence review is a conditional gate, not an unreviewed acceptance.
- **Treat Skia's `PdfA` flag as a PDF/A profile.** Rejected: it is not a validated profile and remains a legacy flag.

## Validation and next steps

- Prototype results on Windows x64 are recorded in [fp-002-windows.md](../evidence/fp-002-windows.md).
- CI runs the same prototype project on Windows, Linux and macOS and uploads its artifacts.
- FP-201, FP-302–FP-306, FP-401, FP-402 and FP-403 must adopt or refine these paths in their own issues; this ADR does not implement them.
- The prototype must not be cited as PDF/UA or PDF/A evidence. Validator reports are required before any conformance claim.
