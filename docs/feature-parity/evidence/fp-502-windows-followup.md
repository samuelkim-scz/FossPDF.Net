# FP-502 additional Windows native and package evidence

Recorded November 1, 2026 on Windows 11 x64 (build 26300), SDK 9.0.305, .NET 9.0.21,
Avalonia 11.3.10 and VS Code 1.144.0. The installed tool is packed from merge `4d6daec`;
the test client uses an isolated checkout with port 12501 so the user's local sample edit is preserved.
This extends [the original Windows record](fp-502-windows.md); it does not close the three-OS gate.

## Observed checks

- Open source launches the captured sample file in VS Code at its annotated source location.
- A fresh local-only NuGet feed and isolated package cache install `FossPDF.Previewer` 2026.5.1
  successfully. The installed shim launches a real native window and receives the watched sample.
  Package SHA-256: `2b5ddd6309b22228ef44c4fcc28a3d1255dc5a5a879d278c53d5f7b3f4edb19c`.
- Twenty normal-document hot reloads succeed, followed by twenty with the 1,000-row sample.
  Search remains active and responsive; selected nodes clear after refresh. The large document has
  **4,075 elements, 6,301 occurrences and 84 pages**. A broad Revenue search visibly caps at 500
  results. The final normal/large update text is observed in the native preview/search results.
- With an explicitly nonexistent editor path, Open source explains that VS Code was not found
  and offers Copy location. The source path remains selectable. The clipboard provider returned
  `oldValue has incorrect type for this property`; the application catches it and displays the
  selectable-path fallback. Positive clipboard copying is **not qualified** by this run.
- Open PDF exports the large document. Independent PyMuPDF 1.26.7 inspection confirms 84 pages,
  the latest refreshed text and a white background inside the selected heading's highlight area:
  the cyan selection overlay is absent from exported PDF content.
- Deliberately impossible content produces a real layout-exception preview. The expanded diagnostic
  contains `DocumentLayoutException` and its stack. Find exception text updates the search to
  `Exception`. A subsequent successful refresh removes the diagnostic.
- Restarting the client without inspection clears the hierarchy, selected occurrence, source
  actions and diagnostic. The native picture remains visible. Numeric horizontal measurement
  from (60,60) to (160,60) on page 1 reports **100 pt / 35.28 mm** without inspection metadata.
- Closing the installed previewer during an active search exits its native window normally;
  the server error log is empty. The manually started document/watch client is managed separately.

## Remaining acceptance

Native Linux/macOS smoke and accessibility review remain pending by the user's explicit decision.
The full Windows keyboard traversal, pointer pan/Ctrl+wheel checks, measurement reset/invalid-input
walkthrough and successful clipboard copy are not all covered by these additional observations;
keep the [complete checklist](../PREVIEWER_TOOLS.md) as the acceptance gate. Existing automated
model/raster/transport tests cover those modeled behaviors on the three CI OS runners, but do
not substitute for a completed native walkthrough.

The audit also reproduced the previewer's old Documentation link failing DNS resolution
(`www.fosspdf.com`). The correction opens the repository's previewer guide and builds with zero
warnings/errors using a separate output directory, preserving the running primary previewer.

Ignored reproducible artifacts are under `artifacts/fp-504/`: the isolated checkout, tool feed/cache,
installed-tool logs, `watch*.log`, `hot-reload-normal.json`, `hot-reload-large.json`,
`native-large-export.pdf` and `native-export-inspection.json`. The general native checklist and
[final audit](../PARITY_AUDIT.md) document scope and reproducible commands.
