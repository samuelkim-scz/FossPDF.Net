# QuestPDF 2026.9.1 parity audit

Recorded November 1, 2026 against FossPDF merge `4d6daec` (PR #69). The target remains
[QuestPDF 2026.9.1](https://github.com/QuestPDF/QuestPDF/releases/tag/2026.9.1), commit
`5a1422e15040484498236a5696d3aa9eefad8b10`. This audits the accepted baseline contracts;
it does not promise identical internal implementation, API spelling or PDF bytes.

**The final release gate remains open.** Thirty of the thirty-two work items are complete.
FP-502's implementation is merged, but native previewer acceptance remains incomplete; FP-504
cannot close before that prerequisite. The user explicitly kept Linux/macOS native checks pending
and the next parity target unselected. Neither decision removes a capability from this target.
The [expanded Windows record](evidence/fp-502-windows-followup.md) includes clean tool installation,
forty hot reloads and large-document export/recovery. It also records the clipboard provider
failure and remaining native walkthrough items without treating them as passes.

All **36 acceptance fixtures are active**. Activation identifies executable coverage, not proof
that every environment or human checkpoint has passed. RUN-503 requires its dedicated deployment
workflow, conformance fixtures require the pinned validators, and native UI review remains
separate from model/raster/transport tests.

## Traceability

The [machine-readable audit](parity-audit-2026.9.1.json) covers every accepted work item, its
original assessment, target references, merged PRs, fixture IDs, tests, documentation and runnable
example. The [original baseline](baseline-2026.9.1.json) is historical evidence and is preserved.

| Work item | Merged implementation | Fixtures | Guide | Status |
| --- | --- | --- | --- | --- |
| [FP-001](https://github.com/samuelkim-scz/FossPDF.Net/issues/2) | [#34](https://github.com/samuelkim-scz/FossPDF.Net/pull/34) | PAG-001, PAG-002, TXT-001, IMG-001, OUT-001, FAIL-001, FAIL-002, RES-001 | [ACCEPTANCE.md](ACCEPTANCE.md) | Complete |
| [FP-002](https://github.com/samuelkim-scz/FossPDF.Net/issues/3) | [#39](https://github.com/samuelkim-scz/FossPDF.Net/pull/39), [#40](https://github.com/samuelkim-scz/FossPDF.Net/pull/40), [#41](https://github.com/samuelkim-scz/FossPDF.Net/pull/41) | Dedicated checks | [NATIVE_DEPENDENCIES.md](NATIVE_DEPENDENCIES.md) | Complete |
| [FP-003](https://github.com/samuelkim-scz/FossPDF.Net/issues/4) | [#38](https://github.com/samuelkim-scz/FossPDF.Net/pull/38), [#40](https://github.com/samuelkim-scz/FossPDF.Net/pull/40) | Dedicated checks | [QUALITY.md](QUALITY.md) | Complete |
| [FP-004](https://github.com/samuelkim-scz/FossPDF.Net/issues/5) | [#35](https://github.com/samuelkim-scz/FossPDF.Net/pull/35) | OUT-001, SET-004 | [SETTINGS.md](SETTINGS.md) | Complete |
| [FP-101](https://github.com/samuelkim-scz/FossPDF.Net/issues/6) | [#49](https://github.com/samuelkim-scz/FossPDF.Net/pull/49) | TXT-101 | [TEXT_PARAGRAPH_LAYOUT.md](TEXT_PARAGRAPH_LAYOUT.md) | Complete |
| [FP-102](https://github.com/samuelkim-scz/FossPDF.Net/issues/7) | [#43](https://github.com/samuelkim-scz/FossPDF.Net/pull/43) | TXT-102 | [TEXT_CLAMPING.md](TEXT_CLAMPING.md) | Complete |
| [FP-103](https://github.com/samuelkim-scz/FossPDF.Net/issues/8) | [#47](https://github.com/samuelkim-scz/FossPDF.Net/pull/47) | TXT-001, TXT-103 | [TEXT_FEATURES.md](TEXT_FEATURES.md) | Complete |
| [FP-104](https://github.com/samuelkim-scz/FossPDF.Net/issues/9) | [#50](https://github.com/samuelkim-scz/FossPDF.Net/pull/50) | FAIL-002, LAY-104 | [MULTI_COLUMN.md](MULTI_COLUMN.md) | Complete |
| [FP-105](https://github.com/samuelkim-scz/FossPDF.Net/issues/10) | [#37](https://github.com/samuelkim-scz/FossPDF.Net/pull/37) | PAG-001, PAG-002, PAG-105 | [ACCEPTANCE.md](ACCEPTANCE.md) | Complete |
| [FP-106](https://github.com/samuelkim-scz/FossPDF.Net/issues/11) | [#48](https://github.com/samuelkim-scz/FossPDF.Net/pull/48) | LAY-106 | [LAZY_COMPOSITION.md](LAZY_COMPOSITION.md) | Complete |
| [FP-107](https://github.com/samuelkim-scz/FossPDF.Net/issues/12) | [#46](https://github.com/samuelkim-scz/FossPDF.Net/pull/46) | LAY-107 | [LAYOUT_HELPERS.md](LAYOUT_HELPERS.md) | Complete |
| [FP-108](https://github.com/samuelkim-scz/FossPDF.Net/issues/13) | [#44](https://github.com/samuelkim-scz/FossPDF.Net/pull/44) | LAY-108 | [CONTENT_POSITIONS.md](CONTENT_POSITIONS.md) | Complete |
| [FP-109](https://github.com/samuelkim-scz/FossPDF.Net/issues/14) | [#51](https://github.com/samuelkim-scz/FossPDF.Net/pull/51) | TXT-109 | [COMPLEX_SCRIPTS.md](COMPLEX_SCRIPTS.md) | Complete |
| [FP-201](https://github.com/samuelkim-scz/FossPDF.Net/issues/15) | [#52](https://github.com/samuelkim-scz/FossPDF.Net/pull/52) | IMG-001, FAIL-001, IMG-201 | [SVG_INPUT.md](SVG_INPUT.md) | Complete |
| [FP-202](https://github.com/samuelkim-scz/FossPDF.Net/issues/16) | [#45](https://github.com/samuelkim-scz/FossPDF.Net/pull/45) | IMG-001, IMG-202 | [IMAGE_OPTIMIZATION.md](IMAGE_OPTIMIZATION.md) | Complete |
| [FP-203](https://github.com/samuelkim-scz/FossPDF.Net/issues/17) | [#36](https://github.com/samuelkim-scz/FossPDF.Net/pull/36) | OUT-001, OUT-203 | [IMAGE_EXPORT.md](IMAGE_EXPORT.md) | Complete |
| [FP-204](https://github.com/samuelkim-scz/FossPDF.Net/issues/18) | [#53](https://github.com/samuelkim-scz/FossPDF.Net/pull/53) | GFX-204 | [CONTAINER_STYLING.md](CONTAINER_STYLING.md) | Complete |
| [FP-205](https://github.com/samuelkim-scz/FossPDF.Net/issues/19) | [#54](https://github.com/samuelkim-scz/FossPDF.Net/pull/54) | GFX-205 | [LINE_STYLES_SHADOWS.md](LINE_STYLES_SHADOWS.md) | Complete |
| [FP-301](https://github.com/samuelkim-scz/FossPDF.Net/issues/20) | [#42](https://github.com/samuelkim-scz/FossPDF.Net/pull/42) | PDF-301 | [MERGING.md](MERGING.md) | Complete |
| [FP-302](https://github.com/samuelkim-scz/FossPDF.Net/issues/21) | [#55](https://github.com/samuelkim-scz/FossPDF.Net/pull/55) | RES-001, PDF-302 | [PDF_OPERATIONS.md](PDF_OPERATIONS.md) | Complete |
| [FP-303](https://github.com/samuelkim-scz/FossPDF.Net/issues/22) | [#56](https://github.com/samuelkim-scz/FossPDF.Net/pull/56) | PDF-303 | [PDF_LAYERS.md](PDF_LAYERS.md) | Complete |
| [FP-304](https://github.com/samuelkim-scz/FossPDF.Net/issues/23) | [#57](https://github.com/samuelkim-scz/FossPDF.Net/pull/57) | PDF-304 | [PDF_SECURITY.md](PDF_SECURITY.md) | Complete |
| [FP-305](https://github.com/samuelkim-scz/FossPDF.Net/issues/24) | [#60](https://github.com/samuelkim-scz/FossPDF.Net/pull/60) | PDF-305 | [ATTACHMENTS_XMP.md](ATTACHMENTS_XMP.md) | Complete |
| [FP-306](https://github.com/samuelkim-scz/FossPDF.Net/issues/25) | [#59](https://github.com/samuelkim-scz/FossPDF.Net/pull/59) | PDF-306 | [PDF_OPTIMIZATION.md](PDF_OPTIMIZATION.md) | Complete |
| [FP-401](https://github.com/samuelkim-scz/FossPDF.Net/issues/26) | [#61](https://github.com/samuelkim-scz/FossPDF.Net/pull/61) | ACC-401 | [PDF_ACCESSIBILITY.md](PDF_ACCESSIBILITY.md) | Complete |
| [FP-402](https://github.com/samuelkim-scz/FossPDF.Net/issues/27) | [#62](https://github.com/samuelkim-scz/FossPDF.Net/pull/62) | ACC-402 | [PDF_ARCHIVAL.md](PDF_ARCHIVAL.md) | Complete |
| [FP-403](https://github.com/samuelkim-scz/FossPDF.Net/issues/28) | [#63](https://github.com/samuelkim-scz/FossPDF.Net/pull/63) | ACC-403 | [PDF_UNIVERSAL_ACCESSIBILITY.md](PDF_UNIVERSAL_ACCESSIBILITY.md) | Complete |
| [FP-404](https://github.com/samuelkim-scz/FossPDF.Net/issues/29) | [#64](https://github.com/samuelkim-scz/FossPDF.Net/pull/64) | ACC-404 | [EINVOICE.md](EINVOICE.md) | Complete |
| [FP-501](https://github.com/samuelkim-scz/FossPDF.Net/issues/30) | [#65](https://github.com/samuelkim-scz/FossPDF.Net/pull/65), [#67](https://github.com/samuelkim-scz/FossPDF.Net/pull/67) | DEV-501 | [PREVIEW_INSPECTION.md](PREVIEW_INSPECTION.md) | Complete |
| [FP-502](https://github.com/samuelkim-scz/FossPDF.Net/issues/31) | [#68](https://github.com/samuelkim-scz/FossPDF.Net/pull/68) | DEV-502 | [PREVIEWER_TOOLS.md](PREVIEWER_TOOLS.md) | Native UI evidence pending |
| [FP-503](https://github.com/samuelkim-scz/FossPDF.Net/issues/32) | [#69](https://github.com/samuelkim-scz/FossPDF.Net/pull/69) | RUN-503 | [RUNTIME_SUPPORT.md](RUNTIME_SUPPORT.md) | Complete |
| [FP-504](https://github.com/samuelkim-scz/FossPDF.Net/issues/33) | This audit | Dedicated checks | [PARITY_AUDIT.md](PARITY_AUDIT.md) | Release gate pending |

For foundations without a feature fixture, the native prototype, quality tool and their dedicated
CI jobs supply the evidence. FP-504 is a release gate rather than another rendering fixture.
Examples and test-file paths for every row are in the machine-readable audit.

## Verified implementation and deployment

[PR #69's final CI run](https://github.com/samuelkim-scz/FossPDF.Net/actions/runs/41360951828)
passes warning-as-error solution builds, the full test suites, independent PDF inspections and
quality/native-dependency checks on Windows, Linux and Intel macOS. Per OS, the .NET 8 suite has
**515 passes and 12 framework skips**; .NET 9 has **719 passes and one evidence-dependent RUN-503
skip**. No planned fixtures remain. The dedicated
[runtime run](https://github.com/samuelkim-scz/FossPDF.Net/actions/runs/41360951808) passes RUN-503
and all 60 packaged consumers on Windows x64/ARM64, Linux x64/ARM64 and macOS x64.

The [runtime support matrix](RUNTIME_SUPPORT.md) defines the actual feature/runtime/host scope,
including the explicit assembly required for NativeAOT embedded-font registration. These results
do not qualify the interactive previewer, external Java/qpdf integrations or every API for AOT.
macOS ARM64, musl, x86 and mobile/browser deployment remain validation-required.

The package version used by this evidence remains **2026.5.1**. These are locally packed build
artifacts; this audit does not claim that the new features have been published to NuGet. Package
release/version selection and publication remain a separate release action.

## Performance and conformance

The [CI evidence summary](evidence/fp-504-ci.json) retains the measured results and validator pins.
The four quality scenarios pass the existing budgets on all three OS runners. Median milliseconds
per document from that run are:

| Scenario | Windows x64 | Linux x64 | macOS x64 |
| --- | ---: | ---: | ---: |
| Report | 17.28 | 11.32 | 19.91 |
| Image-heavy | 7.13 | 5.63 | 7.47 |
| Large table | 279.41 | 190.98 | 424.82 |
| Concurrent | 4.61 | 3.17 | 6.56 |

These are runner measurements, not cross-machine speed guarantees or a benchmark against
QuestPDF. See [QUALITY.md](QUALITY.md) for document counts, memory metrics, warm-up and budgets.

The feature corpus validates all six PDF/A-2/3 profiles with veraPDF 1.30.3, PDF/UA-1 including
combined archival output, and ZUGFeRD 2.5.2 / Factur-X 1.09.2 EN16931 with Mustang 2.26.0 and
veraPDF. Positive outputs and deliberate negatives are independently inspected in CI. The ordinary
quality-tool document makes no conformance claim; its `None` status does not replace this corpus.
PDF/UA human evidence is the [recorded user-reported Acrobat review](evidence/fp-403-accessibility.md)
of the immutable qualification PDF, with the unavailable tool version kept explicit. It does not
certify arbitrary application content.

## Compatibility, decisions and remaining gates

Use the [migration/API-equivalence guide](MIGRATION.md), [unreleased notes](../../RELEASE_NOTES.md)
and feature guides for behavior, ownership and examples. Core remains `netstandard2.1`/`net9.0`;
the SVG/PDF operations companions and interactive tool target .NET 9. Existing document generation,
metadata, image export and picture-only preview clients remain supported.

[ADR-0001](decisions/ADR-0001-native-backends.md), the [settings contract](SETTINGS.md) and
[native dependency policy](NATIVE_DEPENDENCIES.md) define backend separation, exact dependency
cohorts and required notices. No new scope exception is introduced by this audit. PDF/A-4,
PDF/UA-2, X.509 signing, AcroForm creation and general PDF content extraction remain outside the
pinned released-feature baseline.

Before closing #33 or the parent tracker:

1. Complete and record the remaining [native previewer checklist](PREVIEWER_TOOLS.md), including
   Linux/macOS accessibility, editor/fallback, refresh, export and large-document behavior.
2. Close #31 only after its remaining acceptance evidence exists.
3. Recheck issue/fixture traceability and green CI, then record final release sign-off. Keep the
   next parity target unselected until an explicit, separately versioned rebaseline is chosen.

No newer QuestPDF release was published at the time of this audit: the latest release lookup
still returned 2026.9.1. Future versions do not expand this audit automatically.
