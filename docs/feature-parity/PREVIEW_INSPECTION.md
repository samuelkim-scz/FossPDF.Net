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

Interactive selection, measurement, search and source-launch controls are tracked separately in
FP-502; the transport delivers the data those controls consume.

The previewer sample also has an explicit inspection switch:

```powershell
dotnet run --project Source/FossPDF.Previewer.Examples -c Release -- --inspect
```

Run it with the locally built/updated previewer tool. Its annotated heading/body demonstrate
source capture; omit `--inspect` to exercise the existing picture-only workflow.
