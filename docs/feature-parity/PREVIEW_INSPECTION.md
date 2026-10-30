# Optional preview inspection protocol

Inspection protocol **1** adds managed hierarchy/source/occurrence data to the existing local
Skia picture preview. The .NET 9 previewer accepts both legacy picture-only updates and updates
with an `Inspection` object. The core capture API works on netstandard2.1 and .NET 9; .NET 8
consumers use the netstandard2.1 assembly. Source capture uses compiler caller attributes and
works in optimized Release builds without PDBs, reflection or stack walking.

```csharp
using FossPDF.Fluent;
using FossPDF.Previewer;

// In a document composition callback:
column.Item().Inspect("revenue", "Revenue total", configuration: "currency=EUR")
    .Text("Revenue: EUR 12.00");

// .NET 9: explicitly send local source paths, text and layout metadata to the previewer.
document.ShowInPreviewer(port: 12500, enableInspection: true);

// All core frameworks: capture without starting a UI or network service.
using var preview = document.GenerateInspectedPreview();
foreach (var occurrence in preview.Inspection.Occurrences)
    Console.WriteLine($"{occurrence.NodeId}: page {occurrence.Page}, {occurrence.X}, {occurrence.Y}");
```

`Inspect` marks a logical boundary, records file/line/member and optional configuration, and is
transparent to ordinary PDF/image output. No inspection payload is collected or sent by ordinary
generation or the original `ShowInPreviewer` overload. Capture is an explicit opt-in because it
contains document text and absolute source paths. The endpoint binds only to localhost and has
no persistence or telemetry. CI explicitly exports the acceptance snapshot as fixture evidence.

## Version and geometry contract

- `GET /inspection/version` returns integer `1`. An inspection-enabled client requires this
  response before rendering/sending metadata; older previewers give a clear update/disable
  inspection error. Existing `/ping`, `/version` and `/update/preview` behavior remains available.
- The existing multipart `command` JSON retains its `Pages` collection. `Inspection` is optional;
  absent/null means a legacy refresh and clears obsolete metadata. Unknown inspection versions
  and malformed references return HTTP 400 before transferring page ownership.
- Snapshot fields are `Version`, ordered `Nodes`, ordered `Occurrences` and optional `Diagnostic`.
  Nodes carry `Id`, `ParentId`, `Type`, `Label`, `Configuration`, `SourceFile`, `SourceLine` and
  `SourceMember`. Parents precede children. Explicit IDs are scoped to each merged document part;
  automatic IDs follow structural paths and remain stable for an unchanged composed hierarchy.
  Inserting/reordering structural nodes may change automatic IDs; use explicit IDs for stable
  logical boundaries. Caller IDs must be unique in their logical part.
- Every occurrence references a node and **physical 1-based output page**, including merged
  documents using original numbering. Geometry is in page points from the top-left, with the
  transformed four-corner axis-aligned bounding box. Rotations and scale are included; clipping
  does not shrink the bounds. A repeated header has one node and a separate occurrence per page.
- Automatic nodes expose the composed hierarchy and text block contents. Source locations and
  custom configuration are attached through `Inspect`; no source is inferred for internal engine
  nodes. Deferred and runtime-composed boundaries are registered when their actual content draws.
  Generated contents may change structural IDs when their logical hierarchy changes by page.
- Layout caching is disabled for this explicit rendering operation, so cached paint cannot hide
  descendant occurrences. Document settings and ordinary rendering defaults are preserved.

## Limits and ownership

The protocol accepts at most 20,000 nodes, 100,000 occurrences and 5,000 pages; hierarchy depth is
bounded to 256. IDs, labels/configuration, source paths and diagnostics have explicit character
limits checked by `InspectionSnapshot.Validate`. Geometry must be finite, dimensions nonnegative,
and node/page references valid. The server bounds the full request at 128 MiB and command JSON at
16 MiB; duplicate/missing page files and invalid page dimensions fail before native deserialization.
Reduce the document or disable inspection if these limits are exceeded.

`InspectedPreview` owns its native pictures and must be disposed. The snapshot contains managed
values only. Failed captures dispose partial pictures; HTTP upload failures dispose generated
pictures in a `finally` block. The previewer replaces each snapshot, disposes previous pictures,
clears stale metadata on legacy refreshes and unsubscribes/disposes when its window closes.

## Validation and example

`InspectionTests.CreateDocument` is a runnable multi-page example with a repeated heading,
identified revenue rows and source annotations. `DEV-501` exports its versioned JSON. Release
tests verify stable IDs, source attributes, repeated geometry, lazy/merged content, round-trip
serialization and twenty disposal cycles. The .NET 9 HTTP test starts the actual host with a
headless dispatcher, sends legacy and inspection updates, repeats twenty refreshes, rejects
malformed updates atomically and checks stale-data clearing. It runs on the existing Windows,
Linux and Intel macOS CI matrix.

```powershell
cd Source
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --filter "FullyQualifiedName~Inspection|Name~DEV-501"
```

## Interactive inspection controls (FP-502)

The .NET 9 previewer displays an inspection panel when it receives protocol 1 metadata. A legacy
refresh hides it and clears previous selection, search results, measurements and diagnostics.

- Expand the hierarchy and select a logical node, or select a search result to focus/highlight
  its page occurrence. Details show physical page, bounds, configuration and nearest annotated
  source location. Repeated elements have separately selectable occurrences.
- Search matches composed text, labels and IDs without changing the document. Results are capped
  at 500 to keep large snapshots usable. A paragraph's text is logical composed text; its separate
  page fragments can all match. This is document inspection rather than arbitrary PDF extraction.
- Choose **Inspect** and click the page to pick coordinates and the smallest containing element.
  **Pan** preserves drag navigation. The zoom slider and Ctrl+wheel provide 10–1000% magnification.
- Choose **Measure horizontal** or **Measure vertical**, then click two points on the same page.
  The orange overlay and status report the chosen axis distance in points and millimeters.
  Moving to another page starts a new measurement instead of combining unrelated page coordinates.
- **Open source in VS Code** uses the registered `vscode://file/` handler and an escaped local
  path/line. Missing source files or an unavailable handler produce a clear fallback; the details
  text remains selectable for copying into any editor. A source annotation is inherited from the
  nearest `Inspect` boundary. No editor shell command is constructed.
- Failed render diagnostics appear in a selectable panel alongside the error preview. Enable
  per-document debugging when a detailed engine layout trace is needed.

PDF export uses the original page pictures, so highlights and measurement overlays remain preview
controls. Pan, zoom, source-independent operation, export and hot reload remain available. The
panel uses keyboard-operable standard controls and explicit accessible names for its input fields.
The view-model and coordinate tests run headlessly on the Windows/Linux/Intel macOS CI matrix;
interactive Windows verification covers the rendered controls. Other desktop sessions can use
the same smoke steps below without IDE integration.

### Reproducible UI smoke test

1. Run the newly built previewer, then run the sample with `--inspect` below. Confirm the panel
   appears and the document remains visible. Run the sample without that flag for legacy fallback.
2. Search `Modify`, select the matching paragraph, and confirm its blue rectangle and source
   file/line agree with `FossPDF.Previewer.Examples/Program.cs`.
3. Use Inspect on the paragraph and verify page coordinates. Pan/zoom, then repeat: the hit and
   rectangle must continue to align, including when a whole page fits inside the viewport.
4. Make one horizontal and one vertical measurement. Check line placement and units; select a
   point on another page to verify that the old anchor is discarded.
5. Use keyboard navigation on the hierarchy, search and zoom controls. Source details must remain
   copyable when VS Code is absent or the source path is unavailable locally.
6. With `dotnet watch`, edit the sample repeatedly, then temporarily introduce a layout that
   cannot fit. Confirm refreshed data/error diagnostics replace the previous snapshot. Restore
   the sample, export PDF, and close the previewer to exercise disposal.

`DEV-502` records search/selection/measurement results and a real engine layout-error diagnostic.
Automated tests additionally cover viewport transforms, source URI escaping/fallback, bounded
2,000-node searches and twenty refresh/clear cycles.

The preview window owns explicit server startup/shutdown. Teardown runs outside the completed
Avalonia dispatcher context, and the client detects closure of an already-running previewer.
Normal window closure returns without a cancellation exception or a lingering server process.

The previewer sample also has an explicit inspection switch:

```powershell
dotnet run --project Source/FossPDF.Previewer.Examples -c Release -- --inspect
```

Run it with the locally built/updated previewer tool. Its annotated heading/body demonstrate
source capture; omit `--inspect` to exercise the existing picture-only workflow.
