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

## Overlays and underlays

`UnderlayFile(configuration)` draws another PDF beneath selected output pages;
`OverlayFile(configuration)` draws it above them. Both accept a page selector, an optional source
page list and optional repetition:

```csharp
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
        RepeatSourcePages = "1"
    })
    .Save("stamped.pdf");
```

Layer pages are imported as form XObjects and fitted uniformly into the destination trim box,
centered, with source and destination rotation handled. Underlays are drawn in registration order
below the original content; overlays are drawn in registration order above it. The layer file is
read when the method is called.

## Encryption and permissions

`Encrypt` writes the PDF standard security handler. The owner password is required; the user
password is optional. `Encryption256Bit` (AES-256, the recommended strength), `Encryption128Bit`
(RC4, 128-bit) and `Encryption40Bit` (legacy RC4, 40-bit) expose permission flags for printing,
copying, modification, annotations, form filling and page assembly where the revision supports
them:

```csharp
DocumentOperation
    .LoadFile("report.pdf")
    .Encrypt(new DocumentOperation.Encryption256Bit
    {
        UserPassword = "reader-password",
        OwnerPassword = "owner-password",
        AllowPrinting = false,
        AllowContentExtraction = false
    })
    .Save("report-protected.pdf");
```

Loading with the user or owner password and saving without `Encrypt` produces an unencrypted
document; `Decrypt()` states that intent explicitly. Permission flags are advisory for conforming
readers, not copy protection. Encrypted PDFs cannot be PDF/A conforming. See
[docs/feature-parity/PDF_SECURITY.md](https://github.com/samuelkim-scz/FossPDF.Net/blob/main/docs/feature-parity/PDF_SECURITY.md)
for the strength/bit mapping, password rules and failure behavior.

## Attachments and XMP metadata

`AddAttachment` embeds a file with its exact bytes and lists it in both the embedded-files name
tree and the catalog's associated-files array. The name, media type, description, timestamps and
associated-file relationship (`Data`, `Source`, `Alternative`, `Supplement` or `Unspecified`) are
written into the PDF. Attachments carried by the primary loaded document are kept; a duplicate key
throws `PdfOperationException` unless `Replace` is set.

```csharp
DocumentOperation
    .LoadFile("report.pdf")
    .AddAttachment(new DocumentOperation.DocumentAttachment
    {
        FilePath = "terms.txt",
        MimeType = "text/plain",
        Relationship = DocumentOperation.DocumentAttachmentRelationship.Supplement
    })
    .Save("report-with-attachment.pdf");
```

`ExtendMetadata` appends a well-formed XML fragment to the XMP packet. The extension is written as
a PDF incremental update, so the pages and the original document information stay unchanged.
`ExtendMetadata` cannot be combined with `Encrypt`.

## Compression and linearization

`EnableAdditionalCompression()`, `DisableCompression()` and `Linearize()` post-process the saved
document with the external [qpdf](https://qpdf.readthedocs.io/) 11.0 or newer executable, which the
application provides (FossPDF does not distribute it). `UseQpdfExecutable(path)` overrides discovery
for one operation; otherwise `DocumentSettings`/`FossPDF.Settings.QpdfExecutablePath` and then
`PATH` are used. The optimizer runs after every other recorded operation, including attachments,
XMP extensions, overlays and decryption.

```csharp
DocumentOperation
    .LoadFile("report.pdf")
    .EnableAdditionalCompression()
    .Linearize()
    .UseQpdfExecutable(@"C:\Program Files\qpdf 12.4.2\bin\qpdf.exe")
    .Save("report-optimized.pdf");
```

Optimization cannot be combined with `Encrypt`: qpdf rewrites the document and writes it
unencrypted, so such a plan throws `InvalidOperationException`. A missing or too-old qpdf throws
`NotSupportedException` before any output is written. See
[docs/feature-parity/PDF_OPTIMIZATION.md](https://github.com/samuelkim-scz/FossPDF.Net/blob/main/docs/feature-parity/PDF_OPTIMIZATION.md)
for the flag mapping, measured sizes and failure behavior.

## Behavior

- Page order follows the selectors exactly, including descending ranges and repeated pages.
- Page boxes, content streams, resources and annotations are preserved. Metadata (title, author,
  subject, keywords, creator and creation/modification dates) is copied from the primary loaded
  document. XMP metadata is not copied unless `ExtendMetadata` is used; see
  [docs/feature-parity/PDF_ATTACHMENTS.md](https://github.com/samuelkim-scz/FossPDF.Net/blob/main/docs/feature-parity/PDF_ATTACHMENTS.md).
- A malformed, truncated or wrong-password input throws `PdfOperationException`. A syntax error in
  a selector throws `ArgumentException`; a selector that references a page outside the document or
  that produces no pages throws `PdfOperationException`.
- Explicit extra compression, decompression and linearization require an installed qpdf 11.0+ and
  fail clearly when it is unavailable; page content, page boxes and metadata stay equivalent.
