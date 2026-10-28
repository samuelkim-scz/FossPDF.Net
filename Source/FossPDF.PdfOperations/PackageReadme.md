# FossPDF.PdfOperations

Optional advanced PDF operations for [FossPDF](https://www.nuget.org/packages/FossPDF).

The package loads existing PDFs and imports selected pages into a new document, and it assembles
the semantic structure captured by the core package into a tagged PDF. Pages and forms keep their
original content streams and resources; nothing is rasterized. It targets `net9.0` and is not
referenced by the core package.

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

## Behavior

- Page order follows the selectors exactly, including descending ranges and repeated pages.
- Page boxes, content streams, resources and annotations are preserved. Metadata (title, author,
  subject, keywords, creator and creation/modification dates) is copied from the primary loaded
  document. Existing primary-document attachments and XMP metadata are also preserved.
- A malformed, truncated or wrong-password input throws `PdfOperationException`. A syntax error in
  a selector throws `ArgumentException`; a selector that references a page outside the document or
  that produces no pages throws `PdfOperationException`.
- Explicit compression and linearization require an external qpdf 11.0+ executable.

## Attachments and XMP

Use `AddAttachment(new DocumentOperation.DocumentAttachment { FilePath = "data.csv", MimeType = "text/csv" })`
to capture a file, or pass bytes/a borrowed stream with a configuration object. Unicode display
names, descriptions, timestamps, replacement keys and associated-file relationships are supported.
An existing key requires `Replace = true`; file content and settings are captured when added.

`ExtendMetadata("<custom:Value xmlns:custom='urn:example'>value</custom:Value>")` adds a qualified
XMP property while retaining existing namespaces and document metadata. Duplicate property names
fail instead of overwriting existing values. Encryption settings also apply to the extended XMP.

See [the attachment/XMP guide](https://github.com/samuelkim-scz/FossPDF.Net/blob/main/docs/feature-parity/ATTACHMENTS_XMP.md)
for ownership, naming, supported relationships, backend requirements and validation. These
packaging primitives do not establish PDF/A, PDF/UA or invoice-profile conformance.

## Compression and linearization

`EnableAdditionalCompression()`, `DisableCompression()` and `Linearize()` post-process the saved
document with [qpdf](https://qpdf.readthedocs.io/) 11.0 or newer, which the application provides.
`UseQpdfExecutable(path)` overrides `FossPDF.Settings.QpdfExecutablePath` and then PATH discovery.
Optimization runs after page operations, attachments and XMP extensions. Additional compression
and linearization can be combined; compression and decompression cannot.

```csharp
DocumentOperation.LoadFile("report.pdf")
    .EnableAdditionalCompression().Linearize()
    .UseQpdfExecutable(@"C:\Program Files\qpdf 12.4.2\bin\qpdf.exe")
    .Save("report-optimized.pdf");
```

Combining optimization with `Encrypt` throws before writing output: this adapter does not forward
passwords to qpdf. Missing or unsupported executables fail clearly. See the
[optimization guide](https://github.com/samuelkim-scz/FossPDF.Net/blob/main/docs/feature-parity/PDF_OPTIMIZATION.md)
for settings precedence, measured sizes, cancellation and timeout behavior.


## Tagged PDF

`GenerateTaggedPdf` renders a document through the core package's semantic capture and assembles a
tagged PDF: a logical structure tree, marked-content identifiers, `/Alt` alternative text, element
and catalog language, a parent tree, and `/Artifact` marks for decorative content. The visual
output is unchanged.

```csharp
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;

var document = Document.Create(container =>
{
    container.Page(page =>
    {
        page.Content().Column(column =>
        {
            column.Item().SemanticHeading1().Text("Quarterly totals");
            column.Item().SemanticParagraph().Text("Revenue is twelve units.");
            column.Item().SemanticFigure("Four colored quadrants.").Height(60).Background("#ff0000");
        });
    });
}).WithMetadata(new DocumentMetadata { Title = "Accessible report", Language = "en-US" });

document.GenerateTaggedPdf("report.pdf");
```

Standard text, images, Canvas/SVG, links and tables receive automatic tags. Semantic wrappers
override those defaults; `SemanticIgnore()` excludes a subtree. Headers/footers and page layers
are decorative by default, with explicit semantic overrides available. `SemanticLanguage("de-DE")`
scopes a language change, `SemanticLink` associates URL and section-link annotations with a
`/Link` element, and a semantic element split across pages stays one structure element.
`GenerateTaggedContent()` and `ToTaggedPdf()` expose the two stages separately. Documents that use
`ZIndex` or the qpdf compression/linearization settings cannot be captured and throw
`NotSupportedException`. This is structural tagging, not a PDF/UA conformance claim; see
[docs/feature-parity/PDF_ACCESSIBILITY.md](https://github.com/samuelkim-scz/FossPDF.Net/blob/main/docs/feature-parity/PDF_ACCESSIBILITY.md).
