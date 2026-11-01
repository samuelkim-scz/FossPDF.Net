# Interactive previewer tools (FP-502)

The .NET 9 Avalonia previewer can inspect the hierarchy and painted occurrences captured by
[inspection protocol 1](PREVIEW_INSPECTION.md). Ordinary picture-only clients still work;
point coordinates, magnification and measurement also work without inspection metadata.
No additional public library API or wire-protocol version is required.

[Windows validation evidence](evidence/fp-502-windows.md) records the build, regression suites,
native smoke observations and remaining cross-platform checks.

## Run the example

From the repository root, start the local previewer in one terminal, then the sample in another:

```powershell
dotnet run --project Source/FossPDF.Previewer -c Release -- 12500
dotnet run --project Source/FossPDF.Previewer.Examples -c Release -- --inspect
```

The sample has a repeated annotated heading and forty identified revenue rows. Add
`--large` for 1,000 rows, or `--layout-error` for deliberately impossible content. Omit
`--inspect` to check the original picture-only workflow. The client supports hot reload
through `dotnet watch --project Source/FossPDF.Previewer.Examples -- --inspect`.

## Inspect, navigate and measure

- Search text, node IDs, labels, types, source members or configuration. Search runs after a
  150 ms debounce on a worker, cancels on refresh/close, and displays at most 500 matches.
  Refine the search when the cap is reached. The hierarchy uses a virtualized list.
- Select a hierarchy/search row to highlight all its painted occurrences. Choose a specific
  occurrence to focus its physical page. Details show the node ID, type, annotation, point
  rectangle, captured text and nearest annotated ancestor's source location.
- Expand/collapse the selected node with the button or Space/Right. Parent or Left selects
  its parent. In Pick element mode, click a page to select the last captured occurrence
  containing that point; the hierarchy reveals its ancestors and clears an old search.
  Parent selection lets you inspect enclosing wrappers sharing the same bounds.
- Coordinates use physical 1-based pages and points from each page's top-left. Move the pointer
  to read coordinates, or enter Page/X/Y and press Pick to navigate/select without a mouse.
- Choose Measure horizontal or Measure vertical and pick two points on one page. The displayed
  length is the absolute difference on that axis, in points and millimetres. Clicking a
  different page starts a new measurement. Escape or changing tools clears the measurement.
  The acceptance tolerance against captured geometry is 0.25 point.
- Enable the 3× magnifier for a clipped enlargement of the page under the pointer.
- In Pan mode, drag with the left button. Middle-drag pans in every tool. The wheel scrolls,
  Ctrl+wheel zooms around the pointer; a focused canvas also accepts arrows and plus/minus.
- Open PDF preserves the existing export workflow. Inspection overlays are never exported.

All interactive form controls have keyboard focus and accessible names. Source paths, details,
measurements and diagnostics are selectable text. The controls use the existing Fluent theme.

## Source navigation and diagnostics

Open source starts VS Code using a separate `--goto` argument and the captured file/line.
It inherits a source annotation from the nearest parent when the selected engine node has none.
Set `FOSSPDF_VSCODE_PATH` to an absolute editor executable path if automatic discovery fails.
On Windows this must be `Code.exe`, rather than a command script. On Linux/macOS the normal
VS Code CLI entry point is supported. Source files must exist locally; build-machine paths are
displayed as captured and are not automatically remapped.

If the editor or file is unavailable, the status explains the failure and Copy location remains
available. It copies `absolute-path:line`; the selectable source text also works when the
clipboard is unavailable. No editor is required for inspection or measurement.

For failed composition/layout, the client sends the existing exception preview with a bounded
diagnostic string. The Runtime / layout diagnostic expander shows the exception and stack trace;
Find exception text searches the rendered exception page. A successful/legacy refresh clears
stale diagnostics, selection, coordinates, measurement and source data.

## Reproducible UI smoke check

Run these on Windows, Linux and macOS before closing the issue, recording OS, architecture,
SDK, editor version and outcomes. Automated model/raster/transport tests run in the existing
three-OS CI matrix; they do not substitute for a native-window accessibility review.

1. Run the sample with inspection. Search for "Hot reload heading", select its row and choose
   an occurrence on a later page. Verify every heading is highlighted and the chosen page is
   visible. Inspect its type, source, configuration and geometry.
2. Search "Revenue", select a text node, navigate to Parent, expand/collapse it using the
   keyboard, then use Pick element to select another revenue row on the page.
3. Pan and Ctrl+wheel zoom. Verify picking/highlights remain aligned. Enable the magnifier
   and verify the crosshair follows the same page location.
4. Use numeric coordinates with the measurement tools: page 1, (60, 60) to (160, 60) must give
   100 pt / 35.28 mm horizontally; (60, 60) to (60, 132) gives 72 pt / 25.4 mm vertically.
   Check Escape, cross-page measurement reset, and invalid/NaN input.
5. Open source with an installed editor. Then configure a nonexistent editor path, restart the
   previewer and verify the failure message and Copy location fallback.
6. Run with `--inspect --layout-error`. Expand the diagnostic, search the exception text,
   then restart a successful sample and verify the diagnostic disappears.
7. Modify the sample under hot reload at least twenty times while a search/selection is active.
   Verify search, scrolling and input remain responsive, old highlights disappear and Open PDF
   still opens a clean document. Repeat with `--inspect --large`.
8. Restart without `--inspect`. Verify the metadata clears, the picture remains visible and
   pan/zoom, magnification, numeric measurement and PDF export work. Tab through all controls.
   Close the previewer during an active search and confirm it exits without an exception.

## Automated evidence

```powershell
cd Source
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release -f net9.0 --filter "FullyQualifiedName~Inspection|Name~DEV-50"
```

DEV-502 exports `parity-artifacts/DEV-502/interactive-inspection.json` from the real repeated-page
capture. Tests also check transform round trips across mixed page sizes, pixel placement of
highlights, magnifier rendering, repeated refreshes, search caps/cancellation, diagnostics,
source-launch arguments and fallback failures. .NET 8 keeps the core protocol/capture coverage;
the interactive tool and DEV-502 fixture are .NET 9 only.
