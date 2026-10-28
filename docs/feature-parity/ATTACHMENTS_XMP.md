# Attachments and XMP extensions

[FP-305 / issue #24](https://github.com/samuelkim-scz/FossPDF.Net/issues/24) adds document attachments and XMP property extensions to the optional `FossPDF.PdfOperations` package. The companion targets .NET 9; the core package and its existing target frameworks are unchanged.

```csharp
DocumentOperation.LoadFile("report.pdf")
    .AddAttachment(new DocumentOperation.DocumentAttachment
    {
        FilePath = "support.csv",
        Key = "supporting-data",
        AttachmentName = "report-data.csv",
        MimeType = "text/csv",
        Description = "Figures used in the report",
        Relationship = DocumentOperation.DocumentAttachmentRelationship.Data,
        Replace = true
    })
    .ExtendMetadata("<report:Identifier xmlns:report='urn:example:reports'>REPORT-001</report:Identifier>")
    .Save("report-with-data.pdf");
```

## Attachment capture and ownership

`AddAttachment(options)` reads the file when called. The display name defaults to the file's basename; the replacement key independently defaults to that basename, even when `AttachmentName` overrides the display name. Missing timestamps default to the source file's creation and modification times in UTC. Supply explicit `DateTime` values to make those fields reproducible.

`AddAttachment(byte[], options)` copies the bytes. `AddAttachment(Stream, options)` reads from the current position and leaves the borrowed stream open; it does not require seeking. These overloads require `AttachmentName` or `FilePath` to supply a name. Their timestamps remain absent unless supplied. File paths serve as naming defaults for these overloads; they are not read. Empty attachment files are valid.

Configuration is copied when the attachment is added. Subsequent changes to the configuration, original byte array or source file do not change the recorded attachment. Caller streams may be disposed after capture. The default MIME type is `application/octet-stream`; explicit MIME types must have valid type/subtype syntax.

Keys use ordinal, case-sensitive comparison. An existing key fails during save unless `Replace` is true. Replacement updates the document's embedded-file name tree and associated-file collection together. Different keys can share a display name. Unicode keys, filenames and descriptions are stored as Unicode PDF strings; `/UF` retains the full display name, and `/F` supplies an ASCII fallback.

The five supported relationships are `Data`, `Source`, `Alternative`, `Supplement` and `Unspecified`. New attachments default to `Unspecified`. Each new file specification includes `/AFRelationship` and appears in the document catalog's `/AF` array. Ordinary existing attachments without an association remain ordinary attachments.

Existing attachments from the primary input are preserved, including their bytes, replacement keys, supported descriptions, names, MIME types, timestamps and relationship values. Secondary merged documents contribute pages, not document-level attachments. Page annotations continue to follow the existing page-import behavior; this API manages document-level attachments rather than rewriting attachment annotations or arbitrary vendor-specific file-specification fields.

## XMP extension rules

`ExtendMetadata` accepts one or more namespace-qualified XML property elements, without an XML declaration, `rdf:RDF`, `rdf:Description` or `x:xmpmeta` wrapper. Declare namespaces on the properties or use a default namespace. Properties are appended to a description of the primary document; existing descriptions, namespaces and document information remain available.

Duplicate property names are compared by namespace URI and local name, so changing a prefix does not avoid a collision. A collision with an existing property or another recorded extension fails during save instead of overwriting metadata. XML is parsed with DTD processing and external resolution disabled. Parsing is limited to 16 MiB of characters per packet/fragment, and an extended output packet is limited to 16 MiB of UTF-8 bytes.

Existing XMP is preserved as opaque bytes when no extension is requested. Extending it requires a valid supported XMP/RDF packet; malformed XML and unsupported wrappers fail clearly. When the source has no XMP, a packet is created from the retained document information using XML escaping.

Metadata is preserved when attachments are added and across subsequent load/save operations. The writer's normal producer handling still applies to the PDF information dictionary. Attachment bytes and XML are staged with the PDF before caller output is written. Parse, selection and packaging failures leave caller output untouched; an I/O failure in the caller's destination itself can still produce a partial write.

## Encryption and implementation boundary

Attachments and extended XMP work with the three supported encryption strengths. The metadata encryption flag is honored: protected XMP uses the configured security handler; deliberately clear metadata uses the identity crypt filter. Decryption preserves both attachments and XMP. Permission flags keep their documented advisory behavior.

The pinned PDFsharp 6.2.4 writer regenerates XMP during save. The companion therefore appends a single metadata revision to its own freshly serialized output, retaining page painting, resources, encryption and identifiers. For protected metadata, a small .NET 9 `UnsafeAccessor` bridge invokes three exact managed security-handler methods in the pinned backend. Password derivation and ciphers remain backend-owned. Changes to that backend or bridge require rerunning the encrypted metadata corpus on every supported OS. This does not establish NativeAOT support; that qualification remains FP-503.

Associated-file dictionaries and metadata identifiers do not establish PDF/A, PDF/UA or invoice-profile conformance. This feature supplies packaging primitives; profile validation belongs to FP-402/FP-403/FP-404. Metadata copied from an input does not prove that a modified or encrypted output retains its original conformance.

## Reproduce the evidence

From `Source`:

```powershell
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --filter 'FullyQualifiedName~PdfAttachmentTests|Name~Parity_PDF-305'
python -m pip install -r FossPDF.UnitTests/Parity/requirements-security-validation.txt
python FossPDF.UnitTests/Parity/verify_pdf_attachments.py FossPDF.UnitTests/bin/Release/net9.0/parity-artifacts/PDF-305
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --filter FullyQualifiedName~PdfAttachmentExamples
```

The active, net9.0-scoped `PDF-305` fixture emits all five relationship variants. The independent reader verifies exact payload bytes, names, MIME types, shared file-specification references, XMP properties and unchanged painting. The feature suite adds Unicode names, snapshotting, replacement, ownership, malformed input and encrypted round trips.

With PyMuPDF installed locally, `--mupdf` additionally checks XMP with a second reader and compares rendered pages with the source. pypdf 6.21.2 has a documented reading limitation for clear XMP with an explicit identity crypt filter; those two cases are checked by the .NET suite and the optional MuPDF path rather than reported as independent pypdf passes. JSON reports include versions, hashes and limitations.

See [the runnable example](../../Source/FossPDF.Examples/PdfAttachmentExamples.cs) and [Windows evidence](evidence/fp-305-windows.md).
