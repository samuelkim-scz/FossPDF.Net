# FP-502 Windows keyboard, pointer and clipboard follow-up

Recorded November 2, 2026 on Windows 11 x64 (build 26300), .NET 9.0.21 and SDK 9.0.305,
with Avalonia 11.3.10. The checkout starts at `f418606` (merged label PR #72); the fixes
described here are part of this follow-up. Separate checkouts/builds preserve the user's
previewer branding work and sample edits. The existing inspected sample supplies four pages,
235 elements and 301 occurrences.

## Reproductions and corrections

- **Clipboard:** Copy location reported `oldValue has incorrect type for this property`.
  A known safe marker was copied in Notepad++ before clicking the previewer's button; a native
  paste afterward still returned the marker. The copy had genuinely failed. The previewer used
  a top-level asynchronous entry point without the Windows STA apartment needed by OLE.
  A synchronous `[STAThread]` entry point fixes startup while preserving port selection and
  host cleanup after the Avalonia loop ends. The corrected application reports
  `Source location copied.`; pasting into a new Notepad++ tab produces the exact captured
  `C:\repos\FossPDF.Net\Source\FossPDF.Previewer.Examples\Program.cs:59`.
- **Numeric Escape:** A completed horizontal measurement stayed visible when Escape was pressed
  with the numeric Pick button focused. Escape was handled only by the canvas. The window now
  handles an otherwise-unhandled Escape when a measurement exists, and reset clears stale
  measurement instructions. The native retest creates **100 pt / 35.28 mm**, presses Escape
  while Pick retains focus, and observes no measurement line plus `Measurement cleared.`.
- **Element picking:** Clicking a visible revenue row selected a later, empty, page-wide
  `DefaultArtifactElement` foreground wrapper. Picking now prefers the deepest containing
  nonzero-area occurrence; equal depths retain later-occurrence precedence. A native click on
  Revenue 1 after zoom selects its `TextBlock`, correct source and exact row-sized highlight.
  Real captured two-page fixtures exercise the same foreground-wrapper regression.
- **Hierarchy shortcuts:** The ListBox consumed Left/Right before the bubbling key handler.
  Shortcuts now use the tunnel route. Revealing or collapsing a row recreates virtualized
  containers, so keyboard focus is restored after realization only if selection remains current.
  The native retest searches Revenue 0, selects its text, presses Left to select its annotated
  parent, then expands/collapses with Space and expands with Right. The selected parent retains
  keyboard focus through every operation; the child appears and disappears as expected.
- **Coordinate fields:** The user observed the first coordinate field slightly overlapping the
  second. The row now has a 64-point page column, explicit six-point column gaps and text boxes
  allowed to shrink within their allocated columns instead of overflowing theme minimum widths.
  Native visual inspection at 1302 × 882 confirms separate borders and clear gaps between Page,
  X, Y and Pick with the sample loaded. Tab advances through those four controls in order.

## Observed Windows checks

- Tab reaches the inspection selector, magnifier, PDF export, Documentation, canvas, search,
  hierarchy buttons and search-result item. Down selects a result and exposes its source and
  painted occurrence. The accessible tree provides names for the canvas, search, occurrence
  selector, coordinate inputs and hierarchy.
- Space toggles the magnifier, whose native crosshair and enlarged content are visible.
  Canvas Down changes the scroll value from 0.013134 to 0.027695; plus zoom enlarges content
  with its selected-row highlight aligned.
- A left-button drag in Pan mode moves the document, changing scroll from 0.242908 to 0.275256.
- The user manually confirms **Ctrl+wheel zoom around the pointer keeps the highlight aligned**.
  The user separately confirms **middle-button dragging in Pick element mode pans with the
  highlight aligned**. These are human observations, not substituted keyboard simulations.
- Numeric horizontal points (60,60) to (160,60) display **100 pt / 35.28 mm**. Vertical points
  (60,60) to (60,132) display **72 pt / 25.4 mm**. Picking another page starts a new measurement.
  Changing tools clears the prior measurement; canvas-focused Escape also clears it.
- `NaN` and nonnumeric coordinates are rejected with actionable status messages, preserve the
  current measurement, and leave the window responsive. Valid coordinates work afterward.
- Closing the STA previewer exits its window and permits another build to start on the same
  port. The inspected client reconnects and supplies the normal sample.

## Validation and remaining scope

The four new regression tests cover the compiled STA entry-point contract, real captured text
versus empty foreground wrappers, equal-depth/zero-area selection, and measurement/reset status.
The focused interactive-inspection group passes all **16** cases. Native hierarchy event routing
and focus retention are checked in the real window, in addition to model/raster tests.

The final Release solution build passes with `--warnaserror` (**0 warnings, 0 errors**).
The complete test suites pass **544** cases on .NET 8 and **730** on .NET 9, with **16** and
**27** existing framework/optional-validator skips respectively and no failures. The quality
harness passes all four visual scenarios and all performance budgets on win-x64; qpdf and
veraPDF are unavailable locally and their conformance checks are explicitly skipped.
The locked native prototypes also build without warnings and pass their mechanism checks;
pypdf **6.21.2** passes independent inspection, and the dependency audit verifies **20 packages**,
including **6 native packages**, with matching assets, notices and lock. These validation-only
checks add no production dependency and make no PDF/UA or PDF/A conformance claim.

Reproduce from the isolated checkout's `Source` directory (the empty Avalonia products property
avoids the optional build-service integration without changing the pinned UI dependencies):

```powershell
dotnet build FossPDF.sln -c Release --artifacts-path C:\repos\FossPDF.Net\artifacts\fp502-validation-build --no-restore --warnaserror -p:UsedAvaloniaProducts=
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --artifacts-path C:\repos\FossPDF.Net\artifacts\fp502-validation-build --no-build --no-restore --results-directory C:\repos\FossPDF.Net\artifacts\fp502-test-results
dotnet C:\repos\FossPDF.Net\artifacts\fp502-validation-build\bin\FossPDF.Quality\release\FossPDF.Quality.dll --mode all --output C:\repos\FossPDF.Net\artifacts\fp502-quality
dotnet restore FossPDF.NativePrototypes/FossPDF.NativePrototypes.csproj --locked-mode
dotnet build FossPDF.NativePrototypes/FossPDF.NativePrototypes.csproj -c Release --no-restore --warnaserror
dotnet run --project FossPDF.NativePrototypes/FossPDF.NativePrototypes.csproj -c Release --no-build --no-restore -- --output C:\repos\FossPDF.Net\artifacts\fp502-native-validation
python FossPDF.NativePrototypes/verify_artifacts.py C:\repos\FossPDF.Net\artifacts\fp502-native-validation
python FossPDF.NativePrototypes/audit_dependencies.py FossPDF.NativePrototypes/obj/project.assets.json C:\repos\FossPDF.Net\artifacts\fp502-native-validation
```

Ignored native observation logs are saved under `artifacts/fp502-windows-observations-*.json`;
isolated source/build artifacts are under `artifacts/fp502-windows-checks`,
`artifacts/fp502-validation-build` and `artifacts/fp502-final-build`.

This extends the [November 1 package/refresh record](fp-502-windows-followup.md), which remains
a historical record of the clipboard failure. It does not claim a screen-reader audit or native
Linux/macOS acceptance. The complete [three-OS checklist](../PREVIEWER_TOOLS.md) still applies;
#31 and the final release gate remain open until their remaining evidence exists.
