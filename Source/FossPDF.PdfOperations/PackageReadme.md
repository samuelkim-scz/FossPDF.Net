# FossPDF.PdfOperations

Optional existing-PDF operations for [FossPDF](https://www.nuget.org/packages/FossPDF).

The package loads existing PDFs and imports selected pages into a new document. Pages keep their
original page boxes, content streams and resources; nothing is rasterized. It targets `net9.0` and
is not referenced by the core package.

## Usage

```csharp
using FossPDF.Fluent;

// Keep pages 1 and 2 in reverse order, then append pages 1 and 3 of another file.
DocumentOperation
    .LoadFile("report.pdf")
    .TakePages("2,1")
    .MergeFile("appendix.pdf", "1,3")
    .Save("combined.pdf");
```

Page selectors use the compact syntax of the pinned target:

| Selector | Meaning |
| --- | --- |
| `1` | First page; page numbers start at 1 |
| `z` | Last page (`r1`) |
| `r2` | Second-to-last page |
| `1,6,4` | Pages in the listed order |
| `3-7` / `7-3` | Ascending / descending inclusive range |
| `1-z` / `z-1` | All pages ascending / descending |
| `1-20:even` | Even-numbered pages of the range |
| `1-10,x3-4` | The range with pages 3 and 4 removed |

## Loading and saving

`LoadFile(path)`, `LoadFile(path, password)`, `Load(byte[])`, `Load(byte[] , password)`,
`Load(Stream)` and `Load(Stream, password)` read the whole input into memory when called. Borrowed
streams are read from their current position and are never closed.

`Save(path)`, `Save(Stream)` and `SaveToBytes()` execute the recorded operations. Output is staged in
memory before it is written, so a failed operation does not leave a partially written file or
stream. The output stream is left open.

Merging another document appends a new source: `MergeFile(path)`, `MergeFile(path, selector)`,
`Merge(byte[])`, `Merge(byte[], selector)`, `Merge(Stream)` and `Merge(Stream, selector)`. When no
selection has been recorded for the loaded document yet, all of its pages are kept first.

## Behavior

- Page order follows the selectors exactly, including descending ranges and repeated pages.
- Page boxes, content streams, resources and annotations are preserved. Metadata (title, author,
  subject, keywords, creator and creation/modification dates) is copied from the primary loaded
  document; XMP metadata is not copied.
- A malformed, truncated or wrong-password input throws `PdfOperationException`. A syntax error in
  a selector throws `ArgumentException`; a selector that references a page outside the document or
  that produces no pages throws `PdfOperationException`.
- This release does not encrypt, decrypt, attach files, extend XMP metadata, linearize, or overlay
  pages; those workflows are tracked by their own work items.
