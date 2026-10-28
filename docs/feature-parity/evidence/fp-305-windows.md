# FP-305 Windows validation

Recorded October 28, 2026, for [FP-305 / issue #24](https://github.com/samuelkim-scz/FossPDF.Net/issues/24), on `feature/fp-305-pdf-attachments` based on `feature/fp-304-pdf-encryption` at commit `c8c0ab7`.

Environment: Windows x64, .NET SDK 9.0.305 (SDKs 8.0.319/9.0.102/9.0.305/10.0.301 installed), .NET 9.0.9 runtime, SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.5, PDFsharp 6.2.4 (MIT, exact pin). Independent inspection used an isolated Python 3.13 virtual environment with pypdf 6.21.3 and cryptography 50.0.2, run with `-I` from a separate script directory.

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Zero warnings, zero errors |
| Complete .NET 8 suite | 463 passed, 0 failed, 13 skipped (8 planned contracts + 5 net9.0-scoped fixtures) |
| Complete .NET 9 suite | 581 passed, 0 failed, 8 skipped (planned contracts) |
| Parity category | 28 passed / 13 skipped on .NET 8; 33 passed / 8 skipped on .NET 9 |
| `PDF-305` fixture | Active and passing on .NET 9; skipped on .NET 8 by declared framework scope |
| `PdfAttachmentTests` | 14 passed on .NET 9 |
| PDF attachment examples | 2 passed |
| `FossPDF.Quality --mode all` | All requested checks passed; qpdf and veraPDF not installed, so those conformance checks were skipped |
| Independent pypdf inspection | Exact attachment bytes, name and `text/plain` subtype; `/AF` with `/AFRelationship /Data`; well-formed XMP with the declared namespace and value; document information preserved |

## Verified behavior

- `AddAttachment` writes an embedded file stream with the exact bytes, a file specification with `/F`, `/UF`, `/Desc`, `/EF` and `/AFRelationship`, an entry in the catalog's embedded-files name tree and a reference in the catalog's `/AF` array. The fixture's `readme.txt` reads back with its 23 exact UTF-8 bytes, name, `text/plain` subtype, `/Data` relationship and `/Params /Size 23`.
- `AttachmentName` and `Key` default to the file name; `MimeType` defaults to `application/octet-stream`; `Relationship` defaults to `Unspecified`. Configured creation/modification dates are written into `/Params` and read back.
- Unicode names are preserved exactly in `/UF`; `/F` is the PDFDocEncoding fallback with `?` for characters outside printable ASCII (`résumé-附件.txt` becomes `r?sum?-??.txt`). Name-tree keys are sorted.
- A duplicate key fails with `PdfOperationException` unless `Replace` is set; replacement overwrites the earlier entry's bytes, metadata and relationship.
- Attachments carried by the primary loaded document are re-created in the output, including their relationship and media type; a save without additions keeps them. Attachments from merged inputs are not copied.
- The attachment file is read when `AddAttachment` is called and closed immediately: deleting the file before `Save` does not affect the operation. The input byte array is not modified.
- Invalid configuration is rejected at call time: `null` configuration (`ArgumentNullException`), empty path (`ArgumentException`), invalid media type (`ArgumentException`), unreadable file (`PdfOperationException`). A malformed input attachment fails saving rather than being dropped.
- Attachments are written before saving, so they survive `Encrypt`; the fixture-style test extracts the attachment from a 256-bit AES output.
- `ExtendMetadata` appends a validated XML fragment inside the first `rdf:Description`. Saving the fixture output twice preserves the first packet and both fragments, including the input `dc:title`.
- The XMP extension is a real incremental update: the output contains a second `%%EOF`, `/Subtype /XML`, a one-entry cross-reference section and a trailer with `/Prev`. `/Info`, `/Root`, `/Size` and `/ID` are carried forward, so reopening keeps the title and page count.
- Malformed fragments (`null`, blank, plain text, undeclared prefix, comment-only) throw `ArgumentException`; a fragment combined with `Encrypt` throws `InvalidOperationException` in either call order.

## Independent validation

The `PDF-305` artifacts were inspected with pypdf, a separate implementation from PDFsharp:

```text
encrypted: False
pages: 2
attachment name: 'readme.txt'
attachment bytes: 23
attachment matches payload: True
attachment data: b'FossPDF parity fixture\n'
has /AF: True
AFRelationship: /Data
UF name: readme.txt
F name: readme.txt
subtype: /text/plain
params size: 23
metadata bytes: 1458
namespace present: True
value present: True
metadata xml well-formed: True
Info title: FossPDF independent parity fixture PAG-001
```

pypdf independently reads the embedded file from the name tree with the exact payload, confirms the
associated-files entry and relationship, and parses the extended XMP packet as well-formed XML while
still reporting the original document information title.

## Artifacts

`PDF-305` writes beneath
`Source/FossPDF.UnitTests/bin/Release/net9.0/parity-artifacts/PDF-305/`: `source.pdf`,
`attachment-payload.txt` and `attachments.pdf`.

The examples write beneath
`Source/FossPDF.Examples/bin/Release/net9.0/pdf-operations-attachments/` (`invoice.pdf`,
`terms.txt`, `invoice-with-attachment.pdf`, `invoice-copied.pdf`) and
`Source/FossPDF.Examples/bin/Release/net9.0/pdf-operations-metadata/` (`invoice.pdf`,
`invoice-with-metadata.pdf`).

## Reproduce

From `Source`:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore -f net9.0 --filter FullyQualifiedName~PdfAttachmentTests
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PdfAttachmentExamples
dotnet run --project FossPDF.Quality/FossPDF.Quality.csproj -c Release -- --mode all --output ../artifacts/quality
```

The pypdf inspection was run outside the repository against the generated artifact directory, with
the script and virtual environment in separate temporary directories, the paths passed as arguments
and Python started with `-I`.

## Boundaries

- The companion package targets `net9.0` only and the core package does not reference it, per
  ADR-0001.
- Only the primary loaded document contributes attachments; merged, overlaid and underlaid inputs do
  not. The API does not extract attachments to disk.
- `/F` is lossy for non-ASCII names by design; `/UF` is the authoritative Unicode name.
- The XMP extension is an incremental update and is rejected with encryption. Linearization is
  FP-306; PDF/A-3 attachment packaging, PDF/A validation and invoice profiles are FP-402/FP-404 and
  are not claimed here.
- qpdf and veraPDF were unavailable, so no conformance claim is made. Independent inspection is
  limited to pypdf's extraction of the embedded file, associated-files entry, XMP packet and
  document information.
