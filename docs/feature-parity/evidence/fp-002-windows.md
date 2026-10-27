# FP-002 validation evidence

Recorded October 26, 2026, for [FP-002 / issue #3](https://github.com/samuelkim-scz/FossPDF.Net/issues/3).
Branch: `feature/fp-002-native-backends-adr`, based on main `e9bee81`.

## Local Windows evidence

Windows x64, .NET SDK 9.0.305, .NET 9 runtime, SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.5.

| Check | Result and observable evidence |
| --- | --- |
| SVG | Raster pixels match; reused SKPicture draws directly into vector PDF; independent parser finds paths and no image XObjects in that output |
| Existing PDFs | Actual FossPDF/Skia pages import/reorder to widths `[300, 200]`; non-seekable borrowed streams remain open after backend disposal |
| Tagging actual output | Two FossPDF pages acquire heading/paragraph structure with correct role/language/page/MCID/parent-tree associations; original paint/font/ToUnicode bytes and extracted text survive |
| Multiple batches on one page | Two FossPDF fragments import as vector forms with independent invocation streams and distinct tags/MCIDs; original fragment content/font/Unicode data and assembled text survive |
| Ownership and errors | SKSvg releases its native picture; malformed/external SVG, malformed PDF and missing/duplicate semantic associations fail; output failures propagate; caller streams remain available |
| Dependency audit | All 21 package versions/licenses match the lock; all six native packages have redistributable binaries and matching reviewed license/notice hashes |
| Independent inspection | Pinned pypdf 6.21.2 passes serialized-object, marked-content, text/font and vector-output checks |
| Production build/tests | Release solution and prototype builds pass with zero warnings/errors; 255 .NET 8 and 256 .NET 9 tests pass, with 25 planned fixtures skipped per framework |

The proof includes a regression demonstrating `UAManager`'s rejection of imported pages. The chosen
path uses the low-level PDF object model, retaining the Skia renderer, rather than drawing a separate
PDFsharp document and calling it FossPDF integration.

## CI evidence

The `native-prototypes` job runs locked restore, warning-free Release build, all executable proofs,
independent inspection and dependency/native/notice audit on Windows, Ubuntu and macOS. It uploads
`fp-002-native-prototypes-<runner>` artifacts. The main and quality jobs separately run the solution
build, parity/unit tests, settings example and FP-003 quality baselines on the same three OS families.

CI run links and observed runtime identifiers are recorded here after the final branch revision is
validated. Merely configuring the matrix is not acceptance evidence.

## Reproduce and artifacts

See [the runnable README](../../../Source/FossPDF.NativePrototypes/README.md) for exact commands,
including the independent Python inspector and native asset audit. Artifacts include:

- Source/reordered/vector/raster/tagged PDFs, SVG and PNG.
- `semantic-batches.json` and `paint-batches.json`: declared semantics and explicit paint associations.
- `prototype-results.json`: every executable scenario must pass; process exits nonzero on failure.
- `independent-inspection.json`: pypdf version/platform and exact serialized-output assertions.
- `dependency-assets.json`: resolved versions/licenses/copyrights, RID/native binary hashes and notice hashes.
- `environment.json`: actual OS, runtime identifier, framework and managed backend versions.

## Scope and remaining feature work

These are mechanism proofs. They do not activate the production parity fixtures or remove existing
unsupported conformance/linearization guards. FP-201 still owns full SVG/fonts/resources/fit behavior;
FP-302 through FP-306 own PDF operation APIs and unsupported-feature policies. FP-401 must add semantic
composition/layout capture, artifact and link/annotation policies, repeated/split elements and complex
structure while preserving visual output. The isolated proof already exercises explicit whole-page
and multi-batch vector-form associations, but core does not yet capture them automatically.

No PDF/UA or PDF/A validator or human accessibility review is claimed here. FP-402/FP-403 require
those reports before any conformance claim. Wider RID, NativeAOT and trimming support remain FP-503.
