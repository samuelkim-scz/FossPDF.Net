# Generated-document merging

[FP-301 / issue #20](https://github.com/samuelkim-scz/FossPDF.Net/issues/20) adds merging for
generated `IDocument` instances with original or continuous page numbering. It does not load
existing PDF files; that is provided by the optional `FossPDF.PdfOperations` package from FP-302
(see [PDF_OPERATIONS.md](PDF_OPERATIONS.md)).

```csharp
var merged = Document
    .Merge(firstDocument, secondDocument)
    .UseContinuousPageNumbers()
    .WithMetadata(new DocumentMetadata { Title = "Merged report" })
    .WithSettings(new DocumentSettings { DocumentLayoutExceptionThreshold = 100 });

merged.GeneratePdf("merged.pdf");
```

`Document.Merge` accepts `params IDocument[]` or `IEnumerable<IDocument>`. The default is original
page numbering.

## Numbering strategies

| Strategy | Current page | Total pages | Example |
| --- | --- | --- | --- |
| `UseOriginalPageNumbers()` (default) | Restarts at 1 for each source document | Each source's own page count | Two-page + three-page = 1, 2, 1, 2, 3 |
| `UseContinuousPageNumbers()` | Continues across all sources | Combined page count | Two-page + three-page = 1, 2, 3, 4, 5 |

Both strategies preserve source order, page sizes and page content. A new source document always
starts on a new page. Section page numbers and links are scoped to the source document, so the same
section name can be used in multiple sources without colliding. Cross-source section links are not
supported.

`Dynamic` components and page-number text (`CurrentPageNumber`, `TotalPages`,
`TotalPagesWithinSection`, and section start/end values) observe the same strategy as the final
output.

## Metadata and settings precedence

The merged document owns the output metadata and settings. Source document metadata and settings
are **not** inherited. If no merged metadata/settings are supplied, the ordinary defaults apply.

- `WithMetadata` replaces the merged metadata; a null argument keeps the current value.
- `WithSettings` replaces the merged settings; a null argument throws `ArgumentNullException`.
- `DocumentSettingsExtensions.WithSettings(IDocument, ...)` also works on the returned document, but
  the concrete `MergedDocument.WithSettings` avoids an extra wrapper and keeps the merged type.

The merged `DocumentLayoutExceptionThreshold` applies to the **combined** page count in both
strategies. Exceeding it throws `DocumentLayoutException`; the renderer never silently returns a
truncated document.

## Inputs and ownership

- At least one source is required. Null input, an empty sequence, or a null entry throws an
  argument exception.
- A source instance may appear more than once. Source documents must support repeated `Compose`
  calls and create a fresh element tree each time, as the standard `Document` builder does.
- Mixed page sizes are supported; each source keeps its own page geometry.
- `MergedDocument` borrows the source `IDocument` instances and does not dispose or change them.
  Shared images, fonts and caller-owned streams follow the existing generation contracts.
- Sources should produce at least one page. Existing-PDF page selection and merging are provided by
  the optional `FossPDF.PdfOperations` package; see [PDF_OPERATIONS.md](PDF_OPERATIONS.md).

## Framework availability and dependencies

The API is part of the core `FossPDF` package and is available in both `netstandard2.1` and
`net9.0`. It adds no package or native dependency.

## Acceptance and example

`PDF-301` is the executable acceptance fixture. It merges two two-page documents and verifies:

- four pages in red, blue, red, blue order;
- original numbering `1/2, 2/2, 1/2, 2/2`;
- continuous numbering `1/4, 2/4, 3/4, 4/4`;
- merged metadata/settings precedence and repeated-source handling in the unit suite.

See [Source/FossPDF.Examples/DocumentMergeExamples.cs](../../Source/FossPDF.Examples/DocumentMergeExamples.cs)
for a runnable example and [evidence/fp-301-windows.md](evidence/fp-301-windows.md) for the recorded
Windows validation.

## Limitations

- This feature merges **generated** documents only. Loading and merging existing PDF files is
  provided by the optional `FossPDF.PdfOperations` package; see [PDF_OPERATIONS.md](PDF_OPERATIONS.md).
- Source PDF metadata is not copied into the merged output; configure the merged metadata explicitly.
- Cross-source section links and annotations are outside this contract.
- A source document must be safe to compose again if the same instance is used more than once.
