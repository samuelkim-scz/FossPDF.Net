# Feature-parity acceptance specification

This is the FP-001 acceptance contract for functional parity with **QuestPDF 2026.9.1**, starting from FossPDF commit `0339255ff8f987a4abb62ef81f06bbcf00e0b81e`. It accompanies the [roadmap](../../FEATURE_PARITY.md) and [shared tracker](https://github.com/samuelkim-scz/FossPDF.Net/issues/1).

The independently authored [fixture catalog](acceptance-fixtures.json) defines inputs, expected outcomes and traceability for all 27 feature work packages. Fixtures marked **active** are executed by the parity suite and contribute evidence; the remaining contracts are planned and appear as **skipped**, with their owning work item, until contributors implement and activate them. A skipped contract contributes no evidence of feature parity.

## Public-contract and equivalence rules

- Equivalent supported FossPDF APIs are acceptable when they provide the documented behavior. Preserve existing namespaces, defaults, stream/resource ownership and supported framework contracts; document any new availability limits.
- Behavioral contracts below are normative. Settings APIs follow the [FP-004 contract](SETTINGS.md); other proposed method names remain illustrative until their feature issues settle the API. A fixture does not authorize a breaking API change.
- Page count, ordering, semantic structure, Unicode mappings, metadata values and expected failure categories are exact assertions. Visual similarity alone does not establish correctness.
- Compare logical results and decoded output. PDF timestamps, object numbering, font subset prefixes, compression and byte identity are outside equivalence.
- Treat existing equivalents separately from missing capabilities. RTL, shaping, fallback, SVG output, basic page controls and ordinary compression already exist. Qualification tasks must reproduce gaps before requiring fixes.
- Keep fixture inputs, fonts and dependencies independently authored or appropriately licensed. The initial visual cases use bundled Lato and generated color blocks/quadrants.

Foundation deliverables remain defined by [FP-001](https://github.com/samuelkim-scz/FossPDF.Net/issues/2), [FP-002](https://github.com/samuelkim-scz/FossPDF.Net/issues/3), [FP-003](https://github.com/samuelkim-scz/FossPDF.Net/issues/4) and [FP-004](https://github.com/samuelkim-scz/FossPDF.Net/issues/5). The final [FP-504 audit](https://github.com/samuelkim-scz/FossPDF.Net/issues/33) reconciles implementation evidence across all feature contracts. These planning/release work items are not additional missing rendering features.

## Executable corpus

| Fixture | Input and expected result |
| --- | --- |
| `PAG-001` — Explicit page break preserves marker order | PDF reports exactly two pages; raster export has two 120x100 pages. A red 100x20 block starts at (10,10) on page 1; a blue block occupies the same rectangle on page 2. |
| `PAG-002` — Overflow keeps ordered blocks and repeated header/footer | Two pages are produced; red and green blocks fit on page 1, blue continues on page 2. A black header occupies (10,10,100,5) and a gray footer (10,85,100,5) on both pages. |
| `PAG-105` — Page controls keep groups, repeat markers and page-aware predicates | A 30-point group moves intact from the 20-point remainder to page 2. A repeated marker renders once on each page its row spans, and a `ShowIf` predicate sees a null total-page count during the initial pass and the resolved count during the final pass. Regenerating the document neither consumes nor duplicates content. |
| `PDF-301` — Generated-document merging preserves order and numbering | Two two-page documents merge into four pages in red/blue/red/blue order. Original numbering yields 1/2, 2/2, 1/2, 2/2; continuous numbering yields 1/4, 2/4, 3/4, 4/4. Merged metadata/settings take precedence, and a source instance may be repeated. |
| `TXT-001` — Styled spans retain color and left-to-right placement | Exactly one PDF/raster page is produced. Red and blue glyph pixels both exist; the red run precedes the blue run and both stay inside the 10-point margins. This is a styled-rendering baseline, not a Unicode-extraction or bidi conformance claim. |
| `TXT-101` — Paragraph justification, logical alignment, indentation and word spacing | Each scenario produces exactly one 200x160 page except `multipage`, which produces at least two. The first line is indented by 12 points; continuation lines, including a paragraph continued on page 2, start at the margin. Non-final justified lines end at the content edge without stretching glyphs, and the final line is not stretched. RTL `Start` and `End` resolve to the right and left edges. Word spacing is a font-size factor: the seven gaps gain exactly 7 x 2 x 12 points and wrap earlier. Layout geometry is exact to 0.01 point; raster text ink is compared with measured glyph bearings within 2.5 pixels at 72 DPI. No text goldens are used. |
| `TXT-109` — Complex-script, mixed-direction and CJK qualification | The fixture uses licensed Noto subsets registered under stable custom family names, so results do not depend on installed fonts. Font fallback preserves the logical character sequence and splits text into one run per covering font without splitting surrogate pairs. Shaped clusters follow UTF-16 logical order in every run, and each of the LTR/RTL paragraph directions renders the same samples with ink and deterministic run positions. Right-to-left content places runs toward the left in logical order, while each run is shaped in the direction of its own script. CJK text wraps at reviewed break opportunities without spaces; no continuation line starts with closing punctuation and no non-final line ends with opening punctuation. Combining marks never start a line, and no supported glyph renders as notdef. Known gaps are declared and asserted: no Unicode bidi reordering across runs, no automatic system fallback, and monochrome emoji only. |
| `LAY-104` — Newspaper-style multi-column flow and balancing | On a 240x180 page with a 10-point margin, two 104-point columns and a 12-point gap, content reads down the first column and then down the second. Per-paragraph glyph ink matches the same paragraphs rendered in a single 104-point-wide column, so nothing is lost or duplicated. Unbalanced flow draws 10+10 of 24 blocks on page 1 and only the remaining four on page 2. Balancing places 4+4 of eight blocks in two 60-point columns and shrinks the element to the balanced height instead of the full offered height. The separator occupies exactly the declared 1-point gap width and stays centered. Right-to-left content fills the right column first. Raster images flow as ordinary content. Empty content produces one blank page; a child taller than a column throws `DocumentLayoutException`. Repeated generation produces identical per-page ink counts. |
| `IMG-001` — Raster quadrant input preserves aspect ratio and order | Exactly one 120x100 page is produced. The image occupies (10,10,80,40), with red/green above blue/yellow and white outside. |
| `IMG-201` — SVG input, preloading and vector replay | On `net9.0` only (the SVG companion package's target framework), an 80x40 viewBox document reports that intrinsic size; text, bytes and stream inputs draw identically; `FitWidth`, `FitHeight`, `FitArea` and `Resize` measure the expected boxes; the same preloaded image renders three identical pages; vector output contains path paint and no `/Subtype /Image`; the explicit 144-DPI raster fallback embeds a 320x160 image for the 160x80 page. Malformed input, scripts, external references and missing intrinsic sizes throw `SvgParseException` while caller streams stay open. See [SVG_INPUT.md](SVG_INPUT.md). |
| `GFX-204` — Rounded containers, border alignment and gradients | On a 160x120 page at 288 DPI (one pixel is 0.25 point), an 80x40 box at (20,20) with a 4-point border renders ink extents at 20/18/16 and 100/102/104 points for `Inside`/`Center`/`Outside` alignment, each within one pixel. An 8-point inside-aligned ring leaves the corner cutout and the inner hole empty. A red-to-blue horizontal gradient reaches both ends and the (128,0,128) midpoint within eight sRGB levels, and PDF uses a vector `/ShadingType` while SVG keeps `<linearGradient>`. A 2x scaled, 15-degree rotated child is clipped to the rounded bounds: the corner is white, the center is the child color, the PDF content stream clips and SVG keeps `<clipPath>`. See [CONTAINER_STYLING.md](CONTAINER_STYLING.md). |
| `GFX-205` — Advanced line styles and shadows | On a 160x120 page at 288 DPI (one pixel is 0.25 point), a 2-point line from (10,20) to (110,20) with a 6/3 dash and phase 2 starts with a 4-point dash and repeats at the declared intervals, and the same geometry doubles under a 2x canvas scale. A red-to-blue gradient line reaches both ends and the (128,0,128) midpoint within eight sRGB levels. A 60x40 box at (30,50) with an 8-point radius, offset (3,4) and spread 2 produces sharp-shadow ink within 1.5 pixels of 31/52/95/96 points, keeps the background and its own content above the shadow, and paints a half-transparent black center. A 5-point blur (sigma 2.5) matches an independent Gaussian profile at the center, edge and outside falloff. Dashes remain vector PDF path quads and SVG `<path>` elements, the gradient remains `/ShadingType`/`<linearGradient>`, and sharp shadows remain vector; a blurred shadow becomes a PDF image sized by `DocumentMetadata.RasterDpi` and a 72-DPI SVG `<image>` without a claimed `<filter>`. Repeated generation is deterministic. See [LINE_STYLES_SHADOWS.md](LINE_STYLES_SHADOWS.md). |
| `OUT-001` — Document DPI scales page and marker dimensions | The PDF has one page; raster export is 240x200 pixels at 144 DPI. The red rectangle becomes (20,20,200,40) without changing page content coordinates. |
| `SET-004` — Document settings override legacy DPI without mutation | A 144-DPI document override produces a 240x200 PNG with the red rectangle at (20,20,200,40); caller metadata remains at 36 DPI, and a one-page limit accepts the completed page. |
| `OUT-203` - PNG/JPEG/WebP page export | All formats decode into two 240x200 pages at 144 DPI, with red then blue markers. PNG probes are exact; lossy RGB probes allow eight levels away from edges. Alpha, backgrounds, quality, bounds and output ownership are covered by ImageExportTests. |
| `FAIL-001` — Malformed image produces a composition error | Rendering throws DocumentComposeException; no valid PDF output is returned. |
| `FAIL-002` — Impossible fixed-size content produces a layout error | Rendering throws DocumentLayoutException instead of silently clipping or returning a completed PDF. |
| `RES-001` — Caller-owned PDF output stream stays open | The stream contains a PDF header after rendering and remains readable, seekable and writable. Appending data after generation succeeds; the fixture disposes its own stream. |

Rectangles and dimensions in each fixture's `raster` object are authored reference data. The runner renders these references directly with Skia, independently of FossPDF layout. Expected images are never updated by copying the current FossPDF output. Raster page order and PDF-reported page count are checked independently against the catalog.

A fixture may declare `frameworks` to scope it to the frameworks whose runner is compiled. `IMG-201` is scoped to `net9.0` because `FossPDF.Svg` targets `net9.0`; the `net8.0` run reports the fixture as skipped and the catalog consistency check compares active fixtures only against runners available on the current framework.

The PDF baseline checks establish successful generation and reported page count; the geometric comparisons inspect raster output. Future PDF-processing and conformance fixtures require independent PDF inspection/validators, as specified in their work items.

## Feature contracts

Each catalog entry records the current capability, initial assessment, concrete input and observable expected results. These assessments remain pinned to the original baseline; execution state and GitHub completion represent subsequent progress.

| Work item | Fixture | Public behavioral contract |
| --- | --- | --- |
| [FP-101](https://github.com/samuelkim-scz/FossPDF.Net/issues/6) | `TXT-101` | Expose paragraph justification, logical start/end alignment, first-line indentation and word spacing with direction-aware layout. |
| [FP-102](https://github.com/samuelkim-scz/FossPDF.Net/issues/7) | `TXT-102` | Limit paragraphs to a configured line count and render a custom ellipsis within the same width. |
| [FP-103](https://github.com/samuelkim-scz/FossPDF.Net/issues/8) | `TXT-103` | Allow explicit OpenType feature selection and configurable text-decoration style, color and thickness. |
| [FP-104](https://github.com/samuelkim-scz/FossPDF.Net/issues/9) | `LAY-104` | Flow a single content stream through multiple columns with balancing and separator content. |
| [FP-105](https://github.com/samuelkim-scz/FossPDF.Net/issues/10) | `PAG-105` | Provide PreventPageBreak, repeating content and page-context conditional composition. |
| [FP-106](https://github.com/samuelkim-scz/FossPDF.Net/issues/11) | `LAY-106` | Defer composition and optionally cache lazy content with explicit invalidation and ownership. |
| [FP-107](https://github.com/samuelkim-scz/FossPDF.Net/issues/12) | `LAY-107` | Support independent-axis shrinking, general element stacking order and inline vertical alignment. |
| [FP-108](https://github.com/samuelkim-scz/FossPDF.Net/issues/13) | `LAY-108` | Capture stable element identifiers and per-page fragment positions/sizes for callers and tooling. |
| [FP-109](https://github.com/samuelkim-scz/FossPDF.Net/issues/14) | `TXT-109` | Qualify mixed-script shaping, bidi ordering, CJK wrapping and fallback; fix only reproduced gaps. |
| [FP-201](https://github.com/samuelkim-scz/FossPDF.Net/issues/15) | `IMG-201` | Accept SVG strings/bytes/streams and reusable preloaded SVG images through supported fluent APIs. |
| [FP-202](https://github.com/samuelkim-scz/FossPDF.Net/issues/16) | `IMG-202` | Control raster resolution/resampling and compression quality per image with document defaults. |
| [FP-203](https://github.com/samuelkim-scz/FossPDF.Net/issues/17) | `OUT-203` | Export every page as configurable PNG, JPEG or WebP with stable ordering and DPI. |
| [FP-204](https://github.com/samuelkim-scz/FossPDF.Net/issues/18) | `GFX-204` | Render rounded corners, aligned borders and gradients as fluent container styles. |
| [FP-205](https://github.com/samuelkim-scz/FossPDF.Net/issues/19) | `GFX-205` | Expose line dash patterns/phase, gradient line styles and container shadows. |
| [FP-301](https://github.com/samuelkim-scz/FossPDF.Net/issues/20) | `PDF-301` | Merge generated documents with original or continuous page numbering. |
| [FP-302](https://github.com/samuelkim-scz/FossPDF.Net/issues/21) | `PDF-302` | Load/save existing PDFs and select, reorder, extract and merge their pages. |
| [FP-303](https://github.com/samuelkim-scz/FossPDF.Net/issues/22) | `PDF-303` | Apply existing-PDF overlays and underlays to declared page ranges with documented placement. |
| [FP-304](https://github.com/samuelkim-scz/FossPDF.Net/issues/23) | `PDF-304` | Encrypt/decrypt PDFs and configure supported password/security permissions. |
| [FP-305](https://github.com/samuelkim-scz/FossPDF.Net/issues/24) | `PDF-305` | Embed attachments with associated-file relationships and extensible XMP metadata. |
| [FP-306](https://github.com/samuelkim-scz/FossPDF.Net/issues/25) | `PDF-306` | Expose explicit extra compression and PDF linearization controls with documented interactions. |
| [FP-401](https://github.com/samuelkim-scz/FossPDF.Net/issues/26) | `ACC-401` | Expose semantic roles, language, reading order, alternative text and artifacts in tagged PDF. |
| [FP-402](https://github.com/samuelkim-scz/FossPDF.Net/issues/27) | `ACC-402` | Select and validate all six PDF/A-2/3 conformance levels with profile-aware metadata/font/color rules. |
| [FP-403](https://github.com/samuelkim-scz/FossPDF.Net/issues/28) | `ACC-403` | Generate PDF/UA-1 output with machine validation and recorded human accessibility review. |
| [FP-404](https://github.com/samuelkim-scz/FossPDF.Net/issues/29) | `ACC-404` | Package invoice XML and required XMP/associated-file metadata for explicitly supported e-invoice profiles. |
| [FP-501](https://github.com/samuelkim-scz/FossPDF.Net/issues/30) | `DEV-501` | Expose optional versioned inspection/source-mapping data while preserving existing preview clients. |
| [FP-502](https://github.com/samuelkim-scz/FossPDF.Net/issues/31) | `DEV-502` | Provide interactive hierarchy inspection, measurement, search, source navigation and diagnostics. |
| [FP-503](https://github.com/samuelkim-scz/FossPDF.Net/issues/32) | `RUN-503` | Qualify supported trimmed/NativeAOT consumers and native assets by publishing and executing them. |

PDF-operation inputs reuse the generated PAG-001/PAG-002 documents, whose page colors make selection/order observable. ACC-401 declares independently authored semantic content, language, heading order and figure alternatives; the PDF/A/PDF/UA contracts build on it. PDF-305 supplies exact attachment bytes/XMP values. These inputs define future acceptance scenarios; they do not claim current tagged, encrypted or conforming output.

## Visual tolerances

| Check | Initial acceptance budget |
| --- | --- |
| Raster page dimensions and page order | Exact |
| Solid geometry reference comparison | Per-channel RGBA difference at most 2; at most 0.2% of pixels may exceed that difference |
| Geometry capture and measurement contracts | At most 0.25 point, using documented units/origins |
| Styled text baseline | Bundled Lato; both colored runs must render in order and within margins |
| Text golden images across platforms | Introduce only after FP-003 records fonts/native versions and approves platform-specific references/tolerances |
| JPEG/WebP/blur/gradient comparisons | Declare feature-specific decoded-image/color-space budgets during implementation; the solid-color budget does not automatically apply |

The small changed-pixel allowance accommodates raster edges. It cannot excuse a missing page, changed text order, wrong dimensions or failed semantic assertion. `RasterTolerance_*` regression tests establish the channel threshold and dimension-mismatch behavior. Increase budgets only through a reviewed change with before/after artifacts and a reason.

## Run the corpus

From `Source`, with the .NET 9 SDK and .NET 8 runtime installed:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity --logger trx --results-directory ../artifacts/parity
```

Remove `--filter TestCategory=FeatureParity` to run the complete suite. Both target frameworks run automatically. The category contains eighteen executable scenarios on .NET 9 (seventeen on .NET 8, where the net9.0-scoped `IMG-201` is skipped), one catalog/traceability check, four raster-comparison checks, and 18 explicitly planned/skipped contracts.

Artifacts are written beneath `Source/FossPDF.UnitTests/bin/Release/net8.0/parity-artifacts/` and `net9.0/parity-artifacts/`, grouped by fixture ID. Each rendering fixture emits its PDF and actual PNGs; geometric cases also emit reference PNGs. TRX attachments link these outputs. GitHub Actions uploads `parity-evidence-<OS runner>` artifacts, including TRX results, after each Windows/Linux/macOS test job.

See the [initial Windows evidence](evidence/fp-001-windows.md) for the recorded local run. Other platform qualification and broader conformance/performance coverage remain tracked by their work items.

[FP-003](https://github.com/samuelkim-scz/FossPDF.Net/issues/4) adds a separate reproducible quality-baseline layer: runtime and font pins, semantic raster metrics, performance/memory measurements and optional external validator output. See [QUALITY.md](QUALITY.md) for the runnable tool, tolerances, budgets and CI artifacts. The parity corpus above remains the acceptance contract; the quality tool supplies the cross-platform baseline evidence and does not add parity fixtures.

[FP-002](https://github.com/samuelkim-scz/FossPDF.Net/issues/3) records the native-backend decision and prototypes for SVG input, existing-PDF operations and semantic tagging. See [ADR-0001](decisions/ADR-0001-native-backends.md), [NATIVE_DEPENDENCIES.md](NATIVE_DEPENDENCIES.md) and the [Windows evidence](evidence/fp-002-windows.md). The prototypes are isolated from the production dependency graph, and a structural tagging prototype is not a PDF/UA or PDF/A conformance claim; those remain with FP-401, FP-402 and FP-403.

## Activate or extend a fixture

1. Agree the feature's public behavior and settings/ownership contract in its issue before broad changes.
2. Implement the behavior and an executable runner under `Source/FossPDF.UnitTests/Parity`; mark it with `[ParityFixtureRunner("<ID>")]`, which the suite discovers by reflection.
3. Set `execution` to `active` in the catalog. Add negative/edge cases and independent semantic or conformance validation required by the owning issue.
4. Keep inputs and expected outcomes explicit. Preserve existing baseline fixtures; add IDs for new scenarios instead of replacing an old regression case.
5. Run the category and full suite on .NET 8/.NET 9, review artifacts, and link evidence in the PR/issue. Document framework limitations.
6. Close the feature only after its entire acceptance checklist is met. Activating one happy-path fixture does not by itself complete the work item.

The catalog test rejects unknown/missing work-item mappings, duplicate fixture IDs, undocumented states and active fixtures without registered runners. The runner fails on unknown active IDs rather than silently skipping them. Planned contracts remain visible in test results with their owning issue.
