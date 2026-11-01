# FP-502 Windows validation

Recorded November 1, 2026 on Windows x64 (build 26300), SDK 9.0.305 and .NET 9.0.21.
The implementation is on `codex/fp-502-interactive-inspection`. This records local evidence;
it does not assert that the branch has merged or that Linux/macOS CI has passed.

| Check | Result |
| --- | --- |
| Final Release solution build, `--warnaserror` | 0 warnings, 0 errors |
| Full .NET 8 suite with qpdf available | 509 passed, 12 framework/planned skips, 0 failed |
| Full .NET 9 suite with qpdf 12.4.2 and pinned veraPDF/Mustang | 711 passed, 1 planned skip, 0 failed |
| Final inspection/model/raster/HTTP suite, DEV-501/502 and catalog | 26 passed, 0 skipped/failed |
| FP-003 quality, all modes | Four visual scenarios and all performance budgets pass |
| Native prototype independent inspection | 28 checks pass with pypdf 6.21.2 |
| Native dependency/notice audit | 20 packages and 6 native packages pass |

The full .NET 9 run completed before the last render-pass/frame-lifetime hardening; the final
26-test inspection run and final full solution build include that hardening and its two new
regression tests. Core document-generation code was unchanged by the hardening. The quality
tool's optional qpdf/veraPDF discovery checks were skipped in its default PATH; the full .NET 9
suite separately enabled the existing pinned qpdf, veraPDF 1.30.3 and Mustang 2.26.0 installations.

## Acceptance and regressions

DEV-502 is active on .NET 9 and exports `interactive-inspection.json`. The real capture has
three pages and three occurrences of one annotated heading. Search finds ten revenue text
nodes; a horizontal measurement agrees with the fixture geometry at 100 pt (0.25 pt tolerance).
The .NET 8 catalog explicitly skips the .NET 9 interactive tool.

Automated checks cover mixed-width page transforms, pointer-anchored zoom, inter-page gaps,
hierarchy/ancestor selection, repeated highlights and source inheritance, horizontal/vertical
measurement, invalid coordinates and cross-page reset, the 20,000-node/500-result search bound,
canceled searches after refresh/close, diagnostics, and twenty real HTTP-to-window refreshes.
Legacy updates clear obsolete metadata and disposal releases every previous picture.

Queued drawing frames retain their pictures independently of replacement/disposal, and snapshot
their transforms. A lifecycle test proves that retired pictures stay valid until the queued
frame releases them. A software-render-target regression checks that Render does not publish
scroll bindings synchronously: those notifications must occur after the render pass.

Native testing exposed Avalonia's "Visual was invalidated during the render pass" exception when
navigation updated the scrollbar from Render. Deferred publication fixes the exception. Shutdown
also clears the stopped Avalonia synchronization context before asynchronous HTTP-host cleanup.

## Native Windows smoke evidence

The locally built window was exercised with the four-page sample (235 nodes, 301 occurrences):

- Searched and selected the repeated annotated heading, verified its highlight, occurrence
  choices, source path/line and element details.
- Navigated to physical page 3 through numeric coordinates after the render-pass fix; the
  viewport and document scrollbar remained responsive.
- Selected a measurement tool through its keyboard-accessible combo box. Numeric vertical
  measurement from (0, 0) to (0, 72) on page 3 displayed **72 pt / 25.4 mm** and its line.
- Enabled the 3x magnifier and picked inside the heading: enlarged glyphs aligned with the
  pointer's page location.
- Closed the window normally: the host process exited successfully without hanging.
- Verified the canvas and input controls appear in Windows UI Automation. Automated tests
  verify editor-launch argument separation and missing-file/editor fallback; an actual VS Code
  launch was not exercised in this native smoke check.

The complete checklist, including actual editor launch, repeated hot reload, layout-error
recovery, large-document input, PDF opening and picture-only refresh, is in
[PREVIEWER_TOOLS.md](../PREVIEWER_TOOLS.md). Automated refresh/legacy paths retain their
coverage; this native check is a subset of that checklist.

## Reproduce and artifacts

From `Source`:

```powershell
dotnet build FossPDF.sln -c Release --no-restore --warnaserror -p:UsedAvaloniaProducts=
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release -f net9.0 --no-build --no-restore --filter "FullyQualifiedName~Inspection|Name~DEV-50|Name~Catalog_Accounts"
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release -f net8.0 --no-build --no-restore
dotnet run --project FossPDF.Quality/FossPDF.Quality.csproj -c Release --no-build --no-restore -- --mode all --output ../artifacts/fp-502-quality
```

The build flag disables Avalonia's build telemetry task. Full .NET 9 conformance coverage
requires the validator paths described in the archival/invoice guides.

Ignored artifacts are under repository-root `artifacts/fp-502/`, `artifacts/fp-502-quality/`,
`artifacts/fp-502-native/` and the .NET 9 test output's `parity-artifacts/DEV-502/`.
Native test windows and sample clients were closed after validation.

Windows/Linux/macOS CI and native UI smoke checks on Linux/macOS remain acceptance gates.
No cross-platform native UI result or issue closure is claimed by this local record.
