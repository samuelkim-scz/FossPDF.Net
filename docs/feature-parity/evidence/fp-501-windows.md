# FP-501 Windows validation

Recorded October 30, 2026. Cross-platform evidence is produced by the existing CI matrix for the PR.

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | 0 warnings, 0 errors |
| Inspection tests + DEV-501 on .NET 8 | 6 passed, 0 skipped/failed |
| Inspection tests + actual HTTP transport on .NET 9 | 7 passed, 0 skipped/failed |
| Complete .NET 8 suite | 505 passed, 12 skipped, 0 failed |
| Complete .NET 9 suite | 678 passed, 19 skipped, 0 failed |
| FP-003 quality, all modes | Four visual scenarios and all performance budgets pass |

Full-suite skips are framework-scoped fixtures, remaining planned tooling/deployment contracts
and pre-existing external conformance-tool checks. Local validators were not enabled for this
transport change; CI installs and requires the pinned validators. The quality tool records
qpdf/veraPDF as unavailable in this local PATH.

Release tests cover repeated heading geometry, compiler source attributes, stable IDs, JSON,
deferred content, merged parts with physical page numbers, a 500-row document, invalid references
and twenty native picture disposal cycles. The real HTTP host accepts a legacy command with no
inspection field and twenty inspected updates, rejects malformed updates atomically, and clears
obsolete metadata on a later legacy refresh. Its headless dispatcher supports execution on each CI OS.

Ignored evidence outputs: `artifacts/fp501-tests`, `artifacts/fp501-full-tests`,
`artifacts/quality-fp501` and each test output's `parity-artifacts/DEV-501/inspection-v1.json`.
See [PREVIEW_INSPECTION.md](../PREVIEW_INSPECTION.md) for protocol, limits and example commands.

The previewer consumes shared core DTOs through a project reference. Its tool archive includes
the core DLL/Lato font notices and pins Linux HarfBuzz native assets to the core's 8.3.1.5 cohort.
Interactive controls remain FP-502.
