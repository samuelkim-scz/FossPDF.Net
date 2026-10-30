# Preview inspection and source mapping

FP-501 adds optional local inspection to the picture and hot-reload preview protocol. The interactive hierarchy, selection, search and source-navigation UI is tracked separately in FP-502.

## Use inspection

```csharp
using FossPDF.Fluent;
using FossPDF.Previewer;

var document = Document.Create(d => d.Page(page =>
    page.Content().Inspect("revenue", "Revenue total").Text("Revenue: $125.00")));

// Updated .NET 9 previewer tool; inspection is disabled in existing overloads.
document.ShowInPreviewer(new PreviewerOptions { EnableInspection = true });

// Local export without a running previewer; available on netstandard2.1 and net9.0.
using var snapshot = document.GeneratePreviewerSnapshot(
    new PreviewerOptions { EnableInspection = true });
var inspection = snapshot.Inspection;
```

`Inspect` captures compiler-supplied caller file, line and member information in Debug and Release builds without stack walking or PDB discovery. A boundary's descendants inherit its source location until another boundary overrides it. Elements outside a boundary have no source location. File paths reflect the compiler's caller-file path, including any build path mapping; a tool must not assume those files exist on another computer. No files are read or opened by the transport.

For a reusable component, put `Inspect(instanceId)` inside the component to point at its implementation, or at its invocation to point at the caller. Give each component instance a unique ID. Explicit IDs are URI-escaped and prefixed with `id:` on the wire. Repeated page occurrences retain one element ID. Automatic IDs use deterministic structural paths and are stable across refreshes with the same composition; inserting/removing siblings can change automatic IDs. They are not persistent application identifiers. Explicit duplicate IDs in different structural positions fail inspection generation. Dynamic/deferred subtrees reuse their structural positions; nodes created only for measurement can have no painted occurrences.

The runnable `PreviewInspectionExamples.SaveLocalInspectionSnapshot` exports JSON and a native picture. `FossPDF.Previewer.Examples` demonstrates an interactive inspection-enabled connection. Existing `ShowInPreviewer()` and `ShowInPreviewerAsync(port)` calls remain picture-only.

## Protocol version 1

The producer still uses multipart `POST /update/preview`, with a `command` JSON field and one SKPicture file per page ID. A legacy command contains `Pages`; its absent `ProtocolVersion` means zero. Version 1 adds `ProtocolVersion: 1` and optional `Inspection`. The existing `/ping` and `/version` endpoints are unchanged. `/capabilities` advertises `InspectionVersions: [1]` and byte limits. Inspection-enabled producers negotiate before rendering/uploading; an older tool without capabilities receives a clear update-or-disable-inspection error. Picture-only producers do not negotiate capabilities. Unknown protocol or inspection versions fail with HTTP 400.

`Inspection` carries:

- `Version`, currently 1.
- `Elements` in parent-before-child order: `Id`, optional `ParentId`, element `Type`, optional `Label` and `Source` (`File`, `Line`, `Member`), and scalar `Configuration` values. Configuration excludes delegates, native objects, caches and component object graphs. Strings are capped at 512 characters. Typed collection items can be hierarchy-only nodes; replaceable drawing boundaries have occurrences.
- `Occurrences` in draw order: `ElementId`, physical output `PageNumber` (1-based), axis-aligned `X`, `Y`, `Width`, `Height`, local dimensions, and a six-value affine `Transform` (`M11`, `M12`, `M21`, `M22`, `M41`, `M42`). Units are points (72 per inch), with the origin at the page's top left. Bounds use all four transformed corners and describe layout allocation before clipping. One ID can have multiple fragments or repeated occurrences.

Occurrences come only from the final picture-render pass. Pagination, font-subset passes and speculative multi-column measurement do not emit occurrences. Lazy, dynamic and inline content participate when generated. The detached snapshot contains no document, component, delegate or native references in its inspection data.

## Limits and ownership

The default and maximum limits are 20,000 elements, 100,000 occurrences, 128 hierarchy levels, 10,000 pages, a 16 MiB JSON command and a 256 MiB multipart request. Producers also use a conservative running payload budget, so a very detailed snapshot can reach its byte budget before its count limits. `MaxElements` and `MaxOccurrences` can lower count limits. Oversized inspection fails clearly rather than silently returning an incomplete hierarchy. Export callers handle that exception; the live preview replaces failed renders with a picture-only error document, clearing prior inspection.

`PreviewerSnapshot` owns its SKPictures. Dispose it after reading/exporting/sending; do not retain a picture past disposal. Generation failure disposes finished pictures and active recording resources. HTTP failures do not take ownership of a caller's snapshot. Live refreshes are serialized, and receiver updates replace pictures and inspection together on the UI dispatcher. Rejected updates leave the previous preview intact and dispose any pictures decoded so far. Successful replacement and window close dispose the old pictures; the viewer retains only the latest update.

The server binds to localhost, rejects non-loopback peers and browser Origin requests, and does not enable CORS. Inspection is an explicit developer option because configuration, document text and local source paths are included. It is not embedded in generated PDFs. The HTTP transport is available in the core net9.0 target; the netstandard2.1 asset includes the source annotation and local snapshot APIs, preserving .NET 8 compatibility.

## Validation

Run the Release solution build, then the focused tests and executable acceptance fixture:

```powershell
dotnet test Source/FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --filter "FullyQualifiedName~PreviewInspectionTests|FullyQualifiedName~PreviewProtocolTests|Name~Parity_DEV-501"
dotnet test Source/FossPDF.Examples/FossPDF.Examples.csproj -c Release --filter FullyQualifiedName~PreviewInspectionExamples
```

`DEV-501` saves inspection JSON, a count/size summary and page PNGs under `parity-artifacts/DEV-501`. Tests cover picture-only compatibility, version negotiation, a real loopback HTTP upload, malformed input, transformed and repeated geometry, Release call sites, balanced/deferred/dynamic/inline content, failure disposal, 20 refreshes and a document with over 50 pages. Raster comparisons require exact pixel equality between ordinary and inspected previews. See [qualification evidence](evidence/fp-501-windows.md).
