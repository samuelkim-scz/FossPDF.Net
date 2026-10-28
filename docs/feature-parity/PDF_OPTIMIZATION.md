# Explicit PDF compression and linearization

[FP-306 / issue #25](https://github.com/samuelkim-scz/FossPDF.Net/issues/25) exposes optional
post-processing of generated and existing PDFs: additional stream compression, an intentionally
uncompressed form, and linearization (web optimization). The work is performed by the external
[qpdf](https://qpdf.readthedocs.io/) executable; FossPDF does not bundle it.

## Generated documents

```csharp
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;

var pdf = Document.Create(container => /* ... */)
    .WithSettings(new DocumentSettings
    {
        EnableAdditionalCompression = true,
        Linearize = true,
        QpdfExecutablePath = @"C:\Program Files\qpdf 12.4.2\bin\qpdf.exe"
    })
    .GeneratePdf();
```

`CompressDocument = false` produces an uncompressed, inspectable document instead. It cannot be
combined with `EnableAdditionalCompression`.

## Existing documents

The `FossPDF.PdfOperations` companion (`net9.0`) records the same request on `DocumentOperation`:

```csharp
using FossPDF.Fluent;

DocumentOperation
    .LoadFile("invoice.pdf")
    .AddAttachment(new DocumentOperation.DocumentAttachment { FilePath = "terms.txt" })
    .EnableAdditionalCompression()
    .Linearize()
    .UseQpdfExecutable(@"C:\Program Files\qpdf 12.4.2\bin\qpdf.exe")
    .Save("invoice-optimized.pdf");
```

The optimizer runs after every other recorded operation (page selection, merging, overlays,
attachments, XMP extensions, decryption), so the output is the optimized form of the final
document. `DisableCompression()` is the alternative to `EnableAdditionalCompression()`; calling
both fails immediately with `InvalidOperationException`.

## qpdf requirement and discovery

- qpdf **11.0 or newer** is required. The major version is read from `qpdf --version`.
- FossPDF does not distribute qpdf. It is licensed Apache-2.0; see
  [NATIVE_DEPENDENCIES.md](NATIVE_DEPENDENCIES.md).
- Discovery order: `DocumentSettings.QpdfExecutablePath` (or
  `DocumentOperation.UseQpdfExecutable`), then the process-wide `FossPDF.Settings.QpdfExecutablePath`,
  then the first `qpdf`/`qpdf.exe` on `PATH`.
- A configured path that does not exist fails with `NotSupportedException`; it is never silently
  replaced by a different executable.
- The executable is launched directly with an argument list, without a shell.

## What the flags do

| Request | qpdf arguments |
| --- | --- |
| additional compression | `--object-streams=generate --recompress-flate --compression-level=9` |
| disable compression | `--stream-data=uncompress --object-streams=disable` |
| linearize | `--linearize` |

Additional compression usually reduces size, but not always: object streams, hint tables and the
minimum structure cost bytes, so very small documents can grow. Linearization always adds a hint
table and the first-page object ordering. Sizes are therefore measured, never promised.

Measured on Windows with qpdf 12.4.2 (see the evidence report for full output):

| Document | Before | After | Delta |
| --- | --- | --- | --- |
| Generated three-page text report (no embedded images) | 90,820 B | 88,865 B | -1,955 B |
| Image-heavy report (three 600x400 images) | 67,098 B | 65,360 B | -1,738 B |
| Existing one-page invoice with an attachment | 76,841 B | 77,520 B | +679 B |
| One-page invoice saved uncompressed | 76,841 B | 153,854 B | +77,013 B |
| `PDF-306` parity fixture, generated path | 1,233 B | 2,031 B | +798 B |
| `PDF-306` parity fixture, existing path | 1,233 B | 3,645 B | +2,412 B |

The parity fixture is deliberately tiny (two 120x100 pages), which shows the overhead case rather
than a typical optimization win. The existing-PDF path also passes through PDFsharp, which rewrites
the object structure before qpdf sees it.

## Interactions

- **Page content, page boxes and metadata** are equivalent before and after optimization; qpdf
  rewrites the object graph and does not rasterize. The `PDF-306` runner compares decoded content
  streams and page dimensions within 0.01 point, and the title.
- **Attachments and XMP extensions** survive optimization. The companion test case attaches a file,
  optimizes the document and reads the exact bytes and relationship back; extended XMP metadata is
  preserved as well.
- **Overlays and underlays** are applied first and then optimized with the rest of the document.
- **Encryption**: optimization cannot be combined with `DocumentOperation.Encrypt(...)` because this adapter does not forward passwords to the external process. The plan throws `InvalidOperationException` before producing output. Decrypt and optimize as separate steps.
- **PDF/A and PDF/UA**: no conformance claim is made. Optimization does not validate a profile, and
  the conformance guards remain in place until FP-402/FP-403 implement their backends.
- **Image and preview output**: the options are PDF-only. `GenerateImages`, SVG, XPS and previewer
  output are unaffected and never require qpdf.

## Failure behavior

| Condition | Result |
| --- | --- |
| No qpdf found, or the configured path does not exist | `NotSupportedException` before composition; the caller stream receives no bytes |
| qpdf older than 11.0, or an unrecognized version string | `NotSupportedException` |
| `EnableAdditionalCompression` and `DisableCompression` both selected | `ArgumentException` before composition |
| `CompressDocument = false` with `EnableAdditionalCompression = true` | `ArgumentException` before composition |
| qpdf exits with 1 or 2 | `PdfOptimizationException` with the (truncated) stderr diagnostics |
| qpdf exits with 3 (warnings) | treated as success; the document is complete |
| qpdf exceeds `ProcessTimeout` (default two minutes) | `PdfOptimizationException`; the process is killed |
| Cancellation is requested | `OperationCanceledException`; the process is killed |
| Optimization combined with encryption (companion) | `InvalidOperationException` at save |

Because optimized generation renders into memory first, a qpdf failure leaves the destination
stream or file untouched. `Save(Stream)` on `DocumentOperation` stages output in memory before
writing as well.

## Security and resource contract

- The document is written to a uniquely named directory under the system temporary directory and
  passed to qpdf as a file path; qpdf writes the result to standard output. Input is never passed
  through stdin because qpdf requires a seekable input file.
- On Unix, the temporary directory is created atomically with owner-only mode 0700 before writing document bytes. On Windows it inherits the user temporary directory ACL. It is removed before the call returns, including on failure.
- Document content and paths are passed as process arguments, never through a shell command string,
  so no shell metacharacter interpretation occurs.
- Passwords are not passed to qpdf; optimization and encryption are mutually exclusive.
- Diagnostic output is truncated to 2,000 characters before it is included in an exception.
- Concurrent generations use independent temporary directories and processes.

## Framework and package availability

`DocumentSettings.EnableAdditionalCompression`, `Linearize`, `CompressDocument` and
`QpdfExecutablePath`, `FossPDF.Settings.QpdfExecutablePath` and the public
`FossPDF.Infrastructure.PdfOptimizationSettings`/`FossPDF.Drawing.PdfOptimizer` types are part of the
core package and are available in both core targets (`netstandard2.1` and `net9.0`).
`DocumentOperation.EnableAdditionalCompression()`, `DisableCompression()`, `Linearize()` and
`UseQpdfExecutable()` belong to the optional `FossPDF.PdfOperations` package (`net9.0`).

## Acceptance and examples

`PDF-306` is the executable acceptance fixture. It generates PAG-001 with additional compression and
linearization, optimizes the same document through `DocumentOperation`, and independently verifies
both outputs with qpdf (`--check`, `--check-linearization`) and pypdf (page boxes, decoded content
and metadata equality). It also checks that contradictory and encryption combinations fail clearly.
The fixture is scoped to `net9.0`; the runner skips when qpdf is not installed and CI installs qpdf
on every runner.

See [Source/FossPDF.Examples/PdfCompressionExamples.cs](../../Source/FossPDF.Examples/PdfCompressionExamples.cs)
for runnable examples and [evidence/fp-306-windows.md](evidence/fp-306-windows.md) for the recorded
Windows validation.
