# FP-502 Windows validation

Recorded October 30, 2026, for the .NET 9 previewer. The core transport remains available on
the frameworks documented in [PREVIEW_INSPECTION.md](../PREVIEW_INSPECTION.md).

| Check | Result |
| --- | --- |
| Release solution and final previewer builds with warnings as errors | 0 warnings/errors |
| Inspection/protocol/UI acceptance tests, .NET 9 | 12 passed, 0 failed/skipped |
| Inspection capture tests, .NET 8 | 6 passed; DEV-502 is scoped to .NET 9 |
| Complete .NET 8 suite | 505 passed, 12 skipped, 0 failed |
| Complete .NET 9 suite | 683 passed, 18 skipped, 0 failed |
| Live Windows sample | Search, occurrence highlighting/source details, measurement, zoom and contrast checked |
| Window/client shutdown | Previewer process exits after close; attached sample exits with code 0 |

The full-suite skips cover framework-scoped contracts, remaining deployment qualification and
external validator checks not enabled in this local invocation. CI installs the pinned tools
and runs the full suite, view-model/HTTP tests and shared quality/native jobs on all three OSes.
Interactive GUI verification here is Windows-specific; reproducible desktop steps for other
sessions are in the guide.

The live sample displayed 75 hierarchy nodes and 64 occurrences. Searching `Modify` found the
expected paragraph; selecting it painted the correct rectangle and displayed Program.cs line
52. Two horizontal picks measured 200.00 points / 70.56 mm. Setting the accessible zoom input to
200 changed both the readout and rendered magnification. Dark-theme controls and source details
were visually checked after fixing an initial contrast mismatch.

The shutdown check reproduced a lingering process, then verified the correction with a fresh
viewer/client pair: explicit host startup, dispatcher-independent teardown and client liveness
detection. The HTTP regression test also verifies pre-canceled and stopped-server exit paths.

Other automated cases cover horizontal/vertical axes, physical page mapping through pan/zoom and
centered pages, escaping Windows/POSIX source locations, missing-editor/source fallback, selection
of unrendered nodes, 2,000-node bounded search and twenty refresh/clear cycles. DEV-502 saves
action results and a real engine layout-error diagnostic.

Ignored evidence outputs are under `artifacts/fp502-final`, `artifacts/fp502-full-tests`,
`artifacts/fp502-lifecycle-tests`, and each test output's `parity-artifacts/DEV-502/`.
