# Attachments, associated files and XMP metadata

[FP-305 / issue #24](https://github.com/samuelkim-scz/FossPDF.Net/issues/24) adds embedded files and
XMP metadata extensions to [existing-PDF operations](PDF_OPERATIONS.md). The output is an ordinary
PDF document: readers list the files in their attachments panel and expose the metadata packet as
XMP, without a FossPDF-specific format.

```csharp
using FossPDF.Fluent;

DocumentOperation
    .LoadFile("report.pdf")
    .AddAttachment(new DocumentOperation.DocumentAttachment
    {
        FilePath = "terms.txt",
        AttachmentName = "terms.txt",
        MimeType = "text/plain",
        Description = "Payment terms",
        Relationship = DocumentOperation.DocumentAttachmentRelationship.Supplement
    })
    .ExtendMetadata(
        "<invoice:reference xmlns:invoice=\"urn:example:invoices\">2026-002</invoice:reference>")
    .Save("report-with-attachment.pdf");
```

Both calls are recorded on the operation and executed by `Save`. The API lives in the optional
`FossPDF.PdfOperations` companion package, which targets `net9.0` only and is not referenced by the
core package, following [ADR-0001](decisions/ADR-0001-native-backends.md).

## Embedded files

`AddAttachment` reads the file's bytes when it is called. The handle is closed before the method
returns, so the file can be moved or deleted immediately afterwards, and later changes to the file
do not affect the recorded operation. The bytes are embedded unchanged; FossPDF does not re-encode,
transcode or rasterize them.

Each attachment is written as:

- an embedded file stream (`/Type /EmbeddedFile`) with the exact bytes, the media type as
  `/Subtype` and `/Params` containing `/Size` and, when configured, `/CreationDate` and `/ModDate`;
- a file specification (`/Type /Filespec`) with `/F`, `/UF`, `/Desc`, `/EF` references and
  `/AFRelationship`;
- an entry in the catalog's embedded-files name tree (`/Names /EmbeddedFiles /Names`) and a
  reference in the catalog's associated-files array (`/AF`).

### Names, keys and duplicates

| Property | Default | Behavior |
| --- | --- | --- |
| `FilePath` | required | The file read when `AddAttachment` is called. |
| `AttachmentName` | file name | The name readers show. Any Unicode text is accepted. |
| `Key` | `AttachmentName` | The unique key in the embedded-files name tree. |
| `MimeType` | `application/octet-stream` | Must be a bare media type such as `text/plain`. |
| `Description` | omitted | Written as `/Desc`. |
| `CreationDate`/`ModificationDate` | omitted | Written into `/Params` when set. |
| `Relationship` | `Unspecified` | Written as `/AFRelationship`. |
| `Replace` | `false` | See below. |

Names are written twice. `/UF` carries the exact name as a Unicode text string; `/F` is the
PDFDocEncoding fallback in which characters outside printable ASCII are replaced with `?`, so a
name such as `résumé-附件.txt` keeps its exact form in `/UF` and becomes `r?sum?-??.txt` in `/F`.
Name-tree keys are written in sorted order.

Keys must be unique. When an attachment with the same key is already present - either added earlier
in the same operation or carried by the loaded document - saving throws `PdfOperationException`
instead of silently dropping one of the files. Set `Replace = true` to replace the earlier
attachment, including its bytes, metadata and relationship.

`MimeType` is validated when `AddAttachment` is called: it must contain a type and subtype separated
by `/` and consist of printable ASCII without spaces. Parameters such as `; charset=utf-8` are not
supported, because the value is written as a PDF name.

### Relationships

| Value | `/AFRelationship` | Meaning |
| --- | --- | --- |
| `Data` | `/Data` | Data used by the document. |
| `Source` | `/Source` | A source document the PDF was produced from. |
| `Alternative` | `/Alternative` | An alternative representation of the content. |
| `Supplement` | `/Supplement` | Supplementary material. |
| `Unspecified` | `/Unspecified` | No declared relationship. |

Every attachment is listed in `/AF`, so every file specification declares a relationship. `/AF` is
defined by PDF 2.0 and by the associated-files extension to PDF 1.7; readers that do not know it
still find the file through the embedded-files name tree.

### Inputs and ownership

Attachments carried by the primary loaded document are re-created in the output, because the
destination is rebuilt from imported pages and starts without a catalog-level name tree. Their key,
name, description, media type, dates and relationship are kept and their stream bytes are copied
exactly. Attachments from merged, overlaid or underlaid inputs are not copied; only the primary
document contributes attachments, matching how its document information is handled.

A malformed input attachment - a missing file specification or embedded file stream, or an
unsupported stream filter - fails the operation with `PdfOperationException` rather than silently
dropping data. Caller byte arrays and streams are never modified, and `Save(Stream)` still leaves
the output stream open.

Attachments are written before the output is saved, so they are covered by `Encrypt` like any other
stream. `ExtendMetadata` is different; see below.

## XMP metadata extensions

`ExtendMetadata` appends a well-formed XML fragment to the output's XMP packet. The fragment must
contain at least one element and must declare every namespace prefix it uses; invalid fragments are
rejected with `ArgumentException` when the method is called. Several calls append in order.

The fragment is inserted inside the packet's first `rdf:Description` element. When the packet has no
`rdf:Description`, a new one is created inside `rdf:RDF`. When the primary loaded document has an XMP
packet, that packet is the base, so its custom properties survive the operation; otherwise the
packet generated for the output is extended. The packet is parsed and re-serialized, which normalizes
XML formatting but keeps elements, attributes and namespace declarations.

### Incremental update

The PDF writing library regenerates the catalog metadata stream every time a document is saved, so
overriding it before saving would have no effect. Instead the result is written in two steps:

1. The document is saved normally, including its generated XMP packet.
2. A PDF incremental update is appended: the metadata stream object is redefined with the extended
   packet, followed by a one-entry cross-reference section and a trailer with `/Size`, `/Root`,
   `/Info`, `/ID` and `/Prev` pointing at the original cross-reference table.

The original bytes stay in front of the update, so readers that follow `/Prev` see the extended
metadata while every other object is unchanged. The document information dictionary and page
content are preserved. The update requires the document to be written with a classic
cross-reference table, which is what this package's writer produces.

Because the appended stream is not encrypted, `ExtendMetadata` cannot be combined with `Encrypt`:
the combination throws `InvalidOperationException` when saving, regardless of the call order. Call
`Decrypt()` to clear a recorded encryption plan first if the intent is to extend metadata and save
without encryption.

## Failure behavior

| Condition | Result |
| --- | --- |
| `null` attachment configuration | `ArgumentNullException` |
| Empty `FilePath` or a name that cannot be derived | `ArgumentException` |
| Attachment file cannot be read | `PdfOperationException` |
| Invalid `MimeType` | `ArgumentException` |
| Duplicate key without `Replace` | `PdfOperationException` |
| Malformed attachment in the loaded document | `PdfOperationException` |
| `null`, blank or malformed metadata fragment | `ArgumentException` |
| Metadata fragment without an element | `ArgumentException` |
| Loaded XMP packet is malformed or lacks `rdf:RDF` | `PdfOperationException` |
| `ExtendMetadata` combined with `Encrypt` | `InvalidOperationException` |

A failed operation does not create or modify the output file or stream; output is staged in memory
like every other operation.

## Boundaries

- The companion package targets `net9.0` only and the core package does not reference it
  (ADR-0001).
- Attachments from inputs other than the primary loaded document are not copied, and attachments
  are not extracted to disk by this API.
- `/F` is lossy for non-ASCII names by design; use `/UF`, which readers prefer, as the authoritative
  name.
- The XMP extension is appended as an incremental update and cannot be combined with encryption,
  linearization (FP-306) or a PDF/A profile claim (FP-402). PDF/A-3 packaging of attachments and
  invoice XML is FP-404.
- The API does not add document-level JavaScript, portfolio collections or external file
  references.

## Acceptance and example

`PDF-305` is the executable acceptance fixture. On `net9.0` it attaches the fixture's UTF-8
`readme.txt` with the `text/plain` media type and the `Data` relationship, extends the XMP packet
with a namespaced value, and verifies the name-tree key, name, media type, relationship, exact bytes,
`/AF` membership and well-formed metadata. It also checks that the input is unchanged and that both
pages keep their decoded content.

See [Source/FossPDF.Examples/PdfAttachmentExamples.cs](../../Source/FossPDF.Examples/PdfAttachmentExamples.cs)
for runnable examples and [evidence/fp-305-windows.md](evidence/fp-305-windows.md) for the recorded
Windows validation, including independent extraction with `pypdf`.
