# Existing-PDF operations

[FP-302 / issue #21](https://github.com/samuelkim-scz/FossPDF.Net/issues/21) adds loading, page
selection, reordering, merging and saving for **existing** PDF files. Generated-document merging
(`Document.Merge`) is a different feature; see [MERGING.md](MERGING.md).

```csharp
using FossPDF.Fluent;

// Keep pages 2 and 1 in that order, then append pages 1 and 3 of another file.
DocumentOperation
    .LoadFile("report.pdf")
    .TakePages("2,1")
    .MergeFile("appendix.pdf", "1,3")
    .Save("combined.pdf");
```

Pages are imported into a new document, so they keep their original page boxes, content streams,
resources and annotations instead of being rasterized. Metadata is copied from the primary loaded
document.

## Package and framework availability

The API lives in the optional `FossPDF.PdfOperations` companion package, which targets `net9.0`
only. The core `FossPDF` package does not reference it, following
[ADR-0001](decisions/ADR-0001-native-backends.md). Install it alongside the core package:

```powershell
dotnet add package FossPDF.PdfOperations
```

The package pins PDFsharp `6.2.4` exactly. That release is MIT-licensed; published PDFsharp
`6.5.0` declares AGPL-3.0-only and is deliberately not used. The licence text ships in the package
under `licenses/PDFsharp-6.2.4-LICENSE.txt`, and the dependency graph is recorded in
[NATIVE_DEPENDENCIES.md](NATIVE_DEPENDENCIES.md).

## Page selectors

Selectors are evaluated from left to right, and page numbers start at 1.

| Selector | Meaning |
| --- | --- |
| `1` | First page |
| `z` | Last page (same as `r1`) |
| `r2` | Second-to-last page |
| `1,6,4` | Pages in the listed order; repetitions are allowed |
| `3-7` | Ascending inclusive range |
| `7-3` | Descending inclusive range |
| `1-z` / `z-1` | All pages ascending / descending |
| `1-20:even` | Even-numbered pages of the range; `odd` selects the other pages |
| `1-10,x3-4` | The accumulated selection with pages 3 and 4 removed |

An exclusion (`x`) removes pages from everything accumulated so far, not only from the immediately
preceding range. `TakePages` may be called more than once; each call appends its selection in call
order. Selecting the same page twice includes it twice.

## Operations and ordering

`DocumentOperation` records a plan. Nothing is processed until one of the save methods is called.

- `LoadFile(path)`, `LoadFile(path, password)`, `Load(byte[])`, `Load(byte[], password)`,
  `Load(Stream)` and `Load(Stream, password)` load the **primary** document immediately.
- `TakePages(selector)` appends selected pages of the primary document.
- `MergeFile(path[, selector])`, `Merge(byte[][, selector])` and `Merge(Stream[, selector])` add
  another source and append its selected pages. Without a selector, all pages of that source are
  merged.
- If the first merge happens before any selection was recorded for the primary document, all of its
  pages are kept first, then the merged source.
- Selections, merges and further selections are applied in the order the methods were called.

`Save(path)`, `Save(Stream)` and `SaveToBytes()` execute the plan. The result is staged in memory
before it is written, so a failed operation does not leave a partially written file or stream.

## Input and output ownership

- Byte arrays are read without being modified.
- Borrowed streams are read from their current position and are left open. They may be disposed as
  soon as `Load`/`Merge` returns.
- The output stream passed to `Save(Stream)` is left open and remains writable; data is appended at
  its current position. `Save(path)` creates the file only after the operation succeeds.

## Preservation and failure behavior

Imported pages keep their original media boxes, content streams, resources and annotations (for
example link annotations). The following metadata fields are copied from the primary loaded
document when present: title, author, subject, keywords, creator, and creation/modification dates.
The output producer is set by the PDF writing library, and XMP metadata is not copied.

| Condition | Result |
| --- | --- |
| Malformed, truncated or empty input | `PdfOperationException` |
| Protected input without the correct password | `PdfOperationException` |
| Selector syntax error, null or blank selector | `ArgumentException` / `ArgumentNullException` |
| Selector references a page outside the document | `PdfOperationException` |
| The selection produces no pages | `PdfOperationException` |
| Saving without loading a document | `InvalidOperationException` |

The declared password opens a protected input for import. Encrypting the output, writing permission
flags and explicitly decrypting are covered by [PDF_SECURITY.md](PDF_SECURITY.md).

## Scope

This work item delivers loading, selection, reordering, extraction, merging and saving. Overlays
and underlays build on it in [PDF_LAYERS.md](PDF_LAYERS.md); encryption, permissions and decryption
are covered by [PDF_SECURITY.md](PDF_SECURITY.md). It does not include:

- attachments, associated files and XMP extensions (FP-305);
- explicit extra compression or linearization (FP-306);
- rasterizing or previewing existing PDF pages.

## Acceptance and example

`PDF-302` is the executable acceptance fixture. On `net9.0` it builds the PAG-001 and PAG-002
documents, selects PAG-001 pages `2,1`, merges PAG-002 page 1, and verifies that the result has
three pages in blue, red, first-overflow-page order with identical content streams, retained page
boxes and the primary document's metadata. It also checks failure categories, password handling and
caller-owned arrays/streams.

See [Source/FossPDF.Examples/PdfOperationExamples.cs](../../Source/FossPDF.Examples/PdfOperationExamples.cs)
for runnable examples and [evidence/fp-302-windows.md](evidence/fp-302-windows.md) for the recorded
Windows validation.
