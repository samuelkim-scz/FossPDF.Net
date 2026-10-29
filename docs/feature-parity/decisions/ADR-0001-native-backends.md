# ADR-0001: Native backends and dependency contracts

- Status: Accepted in [PR #39](https://github.com/samuelkim-scz/FossPDF.Net/pull/39)
- Date: 2026-10-26
- Work item: [FP-002 / issue #3](https://github.com/samuelkim-scz/FossPDF.Net/issues/3)
- Target: QuestPDF 2026.9.1; FossPDF baseline `0339255`
- Runnable evidence: `Source/FossPDF.NativePrototypes`
- Validation: [FP-002 evidence](../evidence/fp-002-windows.md)

## Context and capability boundary

FossPDF uses SkiaSharp 3.119.4 and HarfBuzzSharp 8.3.1.5. `SKDocument.CreatePdf` writes PDFs;
it does not expose PDF import or a semantic structure tree through these managed bindings.
`ICanvas.DrawText` receives shaped glyphs (`SKTextBlob`), not a logical semantic model.
Tags and reading order cannot be reconstructed reliably from that flattened output.

| Capability | Existing stack | Selected path | Owner |
| --- | --- | --- | --- |
| Drawing and shaping | Skia/HarfBuzz; PDF, raster, SVG/XPS output | Keep current renderer and font manager | Core |
| SVG input | Parser currently belongs to examples | Svg.Skia 4.9.1 companion, replay `SKPicture` through `Canvas` | FP-201 |
| Existing PDFs | No reader/page model | PDFsharp 6.2.4 companion | FP-302 through FP-305 |
| Tagged PDF | No managed semantic/marked-content API | PDFsharp low-level object model plus captured semantic paint batches | FP-401, FP-403 |
| PDF/A profiles | Skia legacy PDF/A-2b output plus semantic assembly | Optional .NET 9 GeneratePdfA writer, final-output validation with pinned veraPDF 1.30.3; see PDF_ARCHIVAL.md | FP-402 |
| Linearization | No writer in the selected cohort has been qualified | Keep explicit unsupported gate; qualify an opt-in qpdf adapter in FP-306 | FP-306 |

This decision establishes mechanisms and integration contracts. It does not implement the production
SVG/PDF/accessibility features, select a conformance level, or claim PDF/UA/PDF/A compliance.

## Decisions

### Keep Skia and HarfBuzz for visual output

The core keeps its existing `netstandard2.1`/`net9.0` targets, public `IDocument` API and native cohort.
The companion packages initially target `net9.0`, are optional, and are never referenced by core.
Internal rendering hooks may be added by their owning issues without adding required public interface
members. In particular, retaining the current **internal** `ICanvas` unchanged is not a constraint on FP-401.

### SVG: replay vectors and make rasterization explicit

Select Svg.Skia 4.9.1 (managed) with the existing native cohort. A reusable source owns its `SKSvg`
and borrowed `Picture`; consumers do not dispose that picture independently. The companion parses
strings/bytes/files/borrowed streams once, uses the picture's cull bounds/view box for measurement,
and replays the picture with a saved/restored transform and clip into the existing public `Canvas` API.
PDF/vector-capable targets retain paths. Raster exports naturally rasterize at their requested DPI.
For unsupported SVG effects, a documented opt-in fallback rasterizes with explicit DPI/background;
otherwise fail before output. Never silently replace all vector input with a bitmap.

The proof reuses one picture for pixel-checked raster output and direct FossPDF PDF output. An
independent parser asserts path geometry and **no image XObjects** in the vector PDF, and an image
in the explicitly rasterized fallback. This preserves FP-201's vector-output requirement.

FP-201 must integrate FossPDF's registered/default font resolution and define fit modes, external
resource policy, unsupported effects and resource limits. External network/file loading is disabled
by default; any explicit resource resolver must have a documented ownership contract. The proof
accepts only a small self-contained shape subset, rejects external references/DTD/malformed input,
and is not a production SVG sanitizer.

Svg.Skia is MIT; **Svg.Custom 4.9.1 is MS-PL**. Select the unchanged binary dependency with its own
license and attribution intact in the optional companion. FossPDF's own code remains MIT; do not
relicense or copy vendor source under MIT. Notices and distribution rules are recorded in the
[dependency inventory](../NATIVE_DEPENDENCIES.md). A future license/cohort change requires a new review.

### PDF operations: pin PDFsharp 6.2.4

Select the managed PDFsharp 6.2.4 package for reading, page import/reorder/merge and the low-level
PDF object model. Loading, password handling, page ranges, overlays, attachments and XMP APIs belong
to FP-302 through FP-305. Import into a new destination and dispose all backend documents after save.
The prototype imports Skia-generated pages, checks order/media boxes, and stages output before
writing to borrowed streams, including non-seekable streams.

**Version/license boundary:** 6.2.4 is MIT. The published 6.5.0 package declares AGPL-3.0-only;
upgrading is a license decision, not routine maintenance. Exact ranges and a committed NuGet lock
file prevent an accidental cohort change. A security fix unavailable under the accepted license
requires review of a maintained permissive fork or a separately qualified replacement; never bypass
security or license review to keep this ADR's version. No automatic replacement is selected.

### Tagging: preserve Skia paint; write structure through the object model

Reject `UAManager`/`StructureBuilder` as a post-processor for imported Skia pages. PDFsharp 6.2.4's
[implementation](https://github.com/empira/PDFsharp/blob/v6.2.4/src/foundation/src/PDFsharp/src/PdfSharp/UniversalAccessibility/UAManager.cs)
listens to new-page/XGraphics drawing events and rejects page import. A regression proof records
that rejection. Repainting shaped FossPDF text with `XGraphics.DrawString` is not the selected path.

Select PDFsharp's public low-level object model to write `/StructTreeRoot`, `/StructElem`, `/ParentTree`,
page `/StructParents`, `/Lang`, and balanced MCID-marked content around **known paint batches**.
The executable proof tags two actual FossPDF/Skia pages using an explicit sidecar: one heading batch
and one paragraph batch. It preserves the original painting instructions, embedded fonts and
ToUnicode streams; independent inspection verifies text extraction, role/page/MCID associations,
reverse parent-tree entries and exact declared Unicode. Whole-page batching is a deliberately narrow
mechanism proof, not an automatic tagging feature for arbitrary PDFs.

A second executable assembles two independently painted FossPDF/Skia fragments as vector Form
XObjects on one page and tags their separate invocation streams. Independent inspection verifies
distinct MCIDs, correct forward/reverse associations and preservation of each original fragment's
paint, font and Unicode data. This proves the selected batch transport as well as the structure writer.

FP-401 must implement the following integration contract **before** exposing production tagging:

1. Composition records a semantic tree: stable node/parent IDs, standard role, language, reading
   order, logical text, alternate text and artifact status. Layout assigns repeated/split fragments
   to those nodes explicitly. Semantic order is independent of painting order.
2. Rendering captures an ordered `PaintBatch(nodeId, pageIndex, batchId, transform, clip, paint)` for
   every semantic fragment/artifact, while the original logical text is still available. An internal
   rendering observer/recorder is additive; it must not infer text from glyph IDs or PDF operators.
3. Each batch has an independently addressable PDF content sequence. The first implementation path
   records batch paint with Skia, emits isolated batch PDFs and imports them as vector Form XObjects
   at their captured positions. The assembler wraps each invocation in a page-local MCID, writes the
   tree/parent-tree association and preserves font resources. Alternatively, an instrumented native
   Skia bridge can emit boundaries directly, but it needs its own ABI/runtime proof before adoption.
4. Links/annotations, document destinations and figure alternatives are sidecar records, not inferred
   from imported form streams. Backgrounds/decorations use artifacts. Batch IDs are unique, MCIDs are
   page-local, and repeated content has an explicit reuse/artifact policy.
5. Reject missing/duplicate/overlapping associations and missing required declarations before writing
   output. Never tag an entire mixed-content page as a paragraph as a fallback.
6. FP-401 qualifies multiple nodes on one page, nested lists/tables, split/repeated elements,
   clipping/transforms, links, mixed scripts and unchanged visual output. These remain its feature
   acceptance tests. FP-403 adds independent conformance validation and human accessibility review.

The proofs validate whole-page wrapping and multi-batch Form assembly with explicit sidecars. They
are not evidence that automatic fine-grained capture is implemented in core. Those internal
hooks and their cost are now an explicit FP-401 requirement, rather than a claim that a completed
opaque PDF can be semantically reconstructed by installing a companion.

### Unsupported paths fail explicitly

| Situation | Decision/fallback |
| --- | --- |
| Missing/mismatched Skia/HarfBuzz native asset | Report initialization failure with required RID/cohort; no silent output downgrade |
| Unsupported framework/RID | Throw a documented unsupported-platform error; use an explicitly deployed supported service if the application chooses |
| Unsupported SVG effect | Explicit documented rasterization option or error; vector path is the default |
| Unmapped/ambiguous semantic paint | Reject tagging; ordinary untagged generation remains a separate caller choice |
| PDF/A-2/3, PDF/UA-1 or linearization requested before qualification | Preserve existing `NotSupportedException` guards; a tag tree or legacy PdfA flag is insufficient |
| PDFsharp cannot preserve an input feature | Explicit failure/declared limitation; no claimed byte-identical, signed or conforming round-trip |

FP-306 must qualify and package a qpdf adapter for the required compression/linearization controls,
including version/license/notices, process cancellation, secrets and runtime deployment. qpdf is
an assessed fallback candidate, not a dependency or a proven backend in this branch. FP-402 must
qualify a profile-capable converter/writer and validators for all requested PDF/A levels. These
unsupported decisions prevent later contributors from treating PDFsharp import as proof of those features.

## Ownership, errors and integration boundaries

- Caller streams are borrowed, read from their current position and left open on success/failure.
  The proof copies at most 32 MiB into owned seekable memory and stages PDF output. Production limits
  are owned by the feature APIs. Output is appended at its current position; callers own truncation.
- Backend documents, parser owners, pictures, bitmaps, images, encoded data and temporary streams
  are disposed deterministically. The SVG source must outlive every generation that replays it;
  concurrent parser/picture use requires qualification or synchronization in FP-201.
- Validate inputs and semantic associations before output. Copy/write failures propagate; staging
  prevents parse/serialization failures from writing partial output, but cannot roll back a failing
  caller sink. Passwords and input content are never included in production diagnostics.
- Production wrappers normalize malformed-input/backend exceptions while preserving inner causes;
  the isolated proof records the pinned backend exceptions rather than publishing an exception API.
- Companion names are provisional: `FossPDF.Svg`, `FossPDF.PdfOperations`, `FossPDF.Accessibility`.
  Method names, full semantic APIs, resource budgets and conformance remain with their owning issues.

## Runtime assets, notices and upgrades

See [NATIVE_DEPENDENCIES.md](../NATIVE_DEPENDENCIES.md) for exact transitive versions, redistributable
asset/notice evidence, qualified versus merely available RIDs, copied notices and upgrade rules.
The proof has `IsPackable=false`, lives outside `FossPDF.sln`, and adds no production dependencies.
Keep the NuGet lock and exact package references in sync; CI restores in locked mode.

Every upgrade reviews package/native licenses and notices, ABI/cohort, frameworks, RID assets and
security support, then runs the prototype matrix, parity corpus and FP-003 quality baselines.
No unreviewed major or license change is allowed. No new native PDF engine is selected: PDFium would
add packaging burden; iText/MuPDF licensing needs a separate decision. A small structure writer is
necessary because the chosen high-level UA helper cannot process the existing renderer's output.

## Validation and acceptance

The same executable and pinned independent inspector run on Windows, Linux and macOS in the
`native-prototypes` CI job. Artifacts include PDFs/PNG/SVG, semantic sidecar, package/native/notice
manifest, environment and pass/fail JSON. See the [evidence record](../evidence/fp-002-windows.md)
for reproduction commands and linked CI results. This ADR closes the backend decision task only;
FP-201/302-306/401-403 retain their production feature and conformance gates.


## FP-402 qualification update

The profile gate is lifted only for the optional companion's `GeneratePdfA` API. It combines
Skia's embedded fonts/ToUnicode/sRGB output intent with the FP-401 semantic writer for A levels,
FP-305 associated files/XMP, and a qualified metadata serialization step for all six PDF/A-2/3
profiles. Final bytes, including any FP-306 qpdf pass, must pass the exact pinned veraPDF 1.30.3
profile before publication. Core remains independent and directs selected-profile GeneratePdf
calls to the companion. Legacy GeneratePdf behavior is preserved without retroactively claiming
all legacy documents conform. PDF/UA remains gated by FP-403. See
[PDF_ARCHIVAL.md](../PDF_ARCHIVAL.md) and [FP-402 evidence](../evidence/fp-402-windows.md).


## FP-403 machine qualification update

The optional .NET 9 `GeneratePdfUa` writer completes structure-root parent links, annotation
descriptions and structure tab order, adds PDF/UA-1 identification and the extension declaration
required by combined archival output, and enforces final pinned veraPDF machine checks. Both
profiles must pass for combined output. Core remains independent and directs PDF/UA requests
to the companion. The logical model and Skia painting mechanisms remain those chosen above.
Human reading-order/alternative/language/usability evidence is required for FP-403
acceptance. The user-reported Acrobat review of the immutable fixture is recorded with its
unreported-version limitation; no automated report substitutes for it. See
[PDF_UNIVERSAL_ACCESSIBILITY.md](../PDF_UNIVERSAL_ACCESSIBILITY.md).
