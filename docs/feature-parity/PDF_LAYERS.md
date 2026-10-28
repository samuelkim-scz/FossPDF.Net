# Overlays and underlays

[FP-303 / issue #22](https://github.com/samuelkim-scz/FossPDF.Net/issues/22) adds overlay and
underlay files to [existing-PDF operations](PDF_OPERATIONS.md). An overlay is drawn above a page's
own content; an underlay is drawn beneath it. Both are ordinary PDF files whose pages are placed as
form XObjects, so their text, vectors, fonts and transparency stay vector data instead of being
rasterized.

```csharp
using FossPDF.Fluent;

DocumentOperation
    .LoadFile("report.pdf")
    .UnderlayFile(new DocumentOperation.LayerConfiguration
    {
        FilePath = "letterhead.pdf",
        TargetPages = "1"
    })
    .OverlayFile(new DocumentOperation.LayerConfiguration
    {
        FilePath = "stamp.pdf",
        TargetPages = "2-z",
        SourcePages = "1-3",
        RepeatSourcePages = "1"
    })
    .Save("stamped.pdf");
```

The layer files are read immediately when `UnderlayFile`/`OverlayFile` is called; changing the
`LayerConfiguration` object afterwards does not affect the recorded operation. Output is staged in
memory like every other operation, so a failed layer leaves no partial file.

## Page mapping

`TargetPages` selects **output** pages after any `TakePages`/`Merge` plan has been applied, and
defaults to all output pages. Pages that are not targets keep exactly the content streams they had.

The layer's pages are assigned in target order:

1. `SourcePages` lists the initial layer pages, in selector order. It defaults to all layer pages.
2. When that list is exhausted, layers stop: extra target pages are left untouched.
3. `RepeatSourcePages` enables cycling after the initial list. It repeats its own selector from its
   first page, independently of `SourcePages`.
4. When `RepeatSourcePages` is set without `SourcePages`, it is also the initial list, so cycling
   begins at the first target page.

For example, with a two-page layer and targets `1-4`:

| Configuration | Target 1 | Target 2 | Target 3 | Target 4 |
| --- | --- | --- | --- | --- |
| no selectors | layer 1 | layer 2 | untouched | untouched |
| `SourcePages = "2", RepeatSourcePages = "1-2"` | layer 2 | layer 1 | layer 2 | layer 1 |
| `RepeatSourcePages = "2,1"` | layer 2 | layer 1 | layer 2 | layer 1 |

`TargetPages = "3,1"` assigns the first source page to output page 3 and the second to output page 1,
because assignment follows the selector order rather than page-number order.

## Layering order

Every call to `UnderlayFile` appends to the underlay list and every call to `OverlayFile` appends to
the overlay list. Per output page, the content is painted in this order:

1. underlays, in registration order (the first call is bottom-most);
2. the page's original content streams, unchanged;
3. overlays, in registration order (the first call is above the underlays but below later overlays).

When overlays are present, separate `q`/`Q` streams surround the original painting instructions. This restores the default page graphics state before overlays, so transforms, clipping and transparency left by the original content cannot shift, hide or fade a layer.

The order is implemented with separate content streams rather than by rewriting the original page
content. Layer forms are registered under per-page resource names (`/L0`, `/L1`, ...), and the page
resource dictionary is cloned before those names are added, so an untargeted page never inherits a
layer XObject.

## Placement and geometry

A layer page is fitted uniformly into the destination page's **effective trim box** (TrimBox, else
CropBox, else MediaBox) and centered. Uniform fitting preserves the layer's aspect ratio and may
enlarge as well as shrink it. The page box itself is not changed: output pages keep their original
media box and dimensions.

- The source page is shown as its own `/Rotate` makes it appear, and its visible box is used as the
  form bounding box. Source content outside that box is clipped.
- The destination `/Rotate` is compensated, so the layer appears upright to the reader instead of
  being rotated with the page.
- The layer content is placed with an explicit `cm` matrix; the destination rotation and any
  non-zero trim-box origin are folded into that matrix.
- `/UserUnit` is not applied to layer placement; pages are treated in default user-space units.
- A same-size, unrotated layer with no trim-box offset is placed with the identity matrix.

Layer pages whose effective box is empty or degenerate fail with `PdfOperationException`.

## Preservation and boundaries

Layer resources are copied with the form, including fonts, `/ExtGState` transparency settings,
color spaces and `/Group` transparency groups, so translucent layer content composites with the
destination page instead of being flattened. The primary document's own annotations, links, page
boxes and metadata keep the behavior documented in [PDF_OPERATIONS.md](PDF_OPERATIONS.md).

Boundaries of this work item:

- Annotations, form fields, outlines and JavaScript from **layer** pages are not copied; only page
  content and its resources are placed. (qpdf also copies layer-page annotations; FossPDF does not.)
- Layer pages are not rasterized or previewed.
- Encryption, permissions, attachments and extra compression are covered by
  [PDF_SECURITY.md](PDF_SECURITY.md), FP-305 and FP-306.
- Page-level optional-content configuration and tagged-PDF structure are not interpreted.

## Failure behavior

| Condition | Result |
| --- | --- |
| `null` `LayerConfiguration` | `ArgumentNullException` |
| Blank `FilePath` | `ArgumentException` |
| Blank `TargetPages`, `SourcePages` or `RepeatSourcePages` | `ArgumentException` |
| Missing layer file | `FileNotFoundException` when the layer is added |
| Malformed, truncated or encrypted layer file | `PdfOperationException` when the operation is saved |
| Layer selector references a page outside the layer | `PdfOperationException` |
| Target selector references a page outside the output | `PdfOperationException` |
| Unsupported source content-stream filter | `PdfOperationException` |

Argument validation happens before the file is read, so an invalid selector is reported even when
the path is also wrong.

## Acceptance and example

`PDF-303` is the executable acceptance fixture. On `net9.0` it applies a full-page yellow underlay
and a smaller green overlay to page 1 of the generated PAG-001 document and verifies that only that
page changes, the underlay stream precedes the unchanged original stream, the overlay follows it,
both layers keep their declared rectangles/colors, and pages, dimensions and rotation are preserved.

See [Source/FossPDF.Examples/PdfLayerExamples.cs](../../Source/FossPDF.Examples/PdfLayerExamples.cs)
for runnable examples (including repeating one layer page across the remaining pages) and
[evidence/fp-303-windows.md](evidence/fp-303-windows.md) for the recorded Windows validation.

Independent validation uses the pinned pypdf reader in all three OS CI jobs:

```powershell
python -m pip install -r FossPDF.NativePrototypes/requirements-validation.txt
python FossPDF.UnitTests/Parity/verify_pdf_layers.py FossPDF.UnitTests/bin/Release/net9.0/parity-artifacts/PDF-303
```

After running the full suite (or `PdfLayerTests` plus `PDF-303`), the script parses pages and forms, checks vector resources, page preservation, graphics-state isolation and transparency. With PyMuPDF installed locally, add `--render` to check pixel colors and emit review PNGs. The primary `PAG-001` document has an opaque white background, which correctly hides the yellow underlay; a separate transparent fixture proves visible underlay, original content and half-transparent overlay composition. These checks do not claim PDF/A or PDF/UA conformance.