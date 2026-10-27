# Content-position capture

[FP-108 / issue #13](https://github.com/samuelkim-scz/FossPDF.Net/issues/13) records the page,
position, size and canvas transform of identified elements for callers and tooling. The API is in
both core targets, `netstandard2.1` and `net9.0`.

```csharp
column.Item()
    .CaptureContentPosition("invoice-lines")
    .Text("Line item");

document.GeneratePdf();

var positions = document.ContentPositions
    .Where(x => x.Id == "invoice-lines")
    .ToArray();
```

Inside a dynamic component, the same captures are available during composition:

```csharp
var positions = context.GetContentCapturedPositions("invoice-lines")
    .Where(x => x.PageNumber == context.PageNumber)
    .ToArray();
```

## Captured data and units

`PageElementLocation` exposes:

| Member | Meaning |
| --- | --- |
| `Id` | The identifier passed to `CaptureContentPosition` |
| `PageNumber` | One-based page number |
| `X`, `Y` | Top-left corner in page coordinates |
| `Width`, `Height` | Rendered size after the canvas transform |
| `Transform` | The full canvas matrix at capture time |

Coordinates use the output canvas coordinate space. For PDF, SVG and previewer output that is layout
points; for raster page export it is pixels at the selected DPI. The origin is the top-left corner of
the page. The `Transform` member records translation, scale and rotation; callers that need rotated
geometry should use the matrix in addition to the derived bounding values.

## Occurrences and repeated rendering

- An element that appears more than once, or spans multiple pages, produces one `PageElementLocation`
  per rendered occurrence.
- The same identifier can be used for multiple elements; all occurrences are returned. Callers filter
  by `PageNumber` and/or order.
- Captures are recorded during the initial layout pass. Dynamic components may see an empty or
  incomplete collection during that first pass; the built-in document exposes the complete set after
  generation.
- Each generation uses a fresh page context. Repeated generation replaces the document's captures
  rather than appending to them.
- The built-in `Document` exposes `ContentPositions`; custom `IDocument` implementations can read
  captures through `DynamicContext.GetContentCapturedPositions` because the result is not part of the
  `IDocument` contract.
- Conditional or dynamic content that changes size between layout passes may make captured geometry
  unstable; handle an empty/incomplete collection in dynamic components.

## Example and acceptance

`LAY-108` is the executable acceptance fixture. It places twelve 20-point rows on 120x100 pages and
checks that all twelve occurrences report the expected page, X/Y, width and height within 0.25 point,
then regenerates the document and verifies the captures are stable.

See [Source/FossPDF.Examples/ContentPositionExamples.cs](../../Source/FossPDF.Examples/ContentPositionExamples.cs)
and [evidence/fp-108-windows.md](evidence/fp-108-windows.md).

## Limitations

- Captures reflect the initial layout pass; the built-in document does not expose a separate
  final-pass geometry set.
- Captured geometry is a reporting feature, not a layout input. Using captures to conditionally
  change the same content can create an unstable layout.
- The previewer transport and interactive inspection features remain FP-501/FP-502.
