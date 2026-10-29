# PDF/A-2 and PDF/A-3 (FP-402)

The optional `FossPDF.PdfOperations` package on **.NET 9** provides `GeneratePdfA`. Select
`PdfA2A`, `PdfA2B`, `PdfA2U`, `PdfA3A`, `PdfA3B` or `PdfA3U` through
`DocumentSettings.PdfAConformance`. The writer embeds fonts, retains ToUnicode mappings, supplies
an sRGB ICC output intent and consistent XMP/Info metadata, and identifies the exact profile.
A levels use the semantic structure captured by FP-401. All profiles enforce complete glyph
coverage, even when the caller disabled the ordinary rendering check.

**Every final output must pass the pinned veraPDF profile before bytes are returned or a file or
stream is written.** Validation includes the output of any requested qpdf optimization. A failure
throws `PdfAConformanceException` with the complete `Report.Json`; no compliance claim or failed
PDF is published. Missing tools and process failures throw actionable errors. No tool is downloaded
at runtime. Core's existing `netstandard2.1`/`net9.0` APIs and dependencies are preserved.

```csharp
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.PdfOperations;

var document = Document.Create(container => container.Page(page =>
{
    page.Content().Column(column =>
    {
        column.Item().SemanticHeading1().Text("Archived report");
        column.Item().Text("Revenue: twelve units. Unicode: café.");
    });
})).WithMetadata(new DocumentMetadata
{
    Title = "Archived report", Language = "en-US"
}).WithSettings(new DocumentSettings { PdfAConformance = PdfAConformance.PdfA3A });

var attachment = new PdfAAttachment(System.Text.Encoding.UTF8.GetBytes("<revenue>12</revenue>"),
    new DocumentOperation.DocumentAttachment
    {
        AttachmentName = "report.xml", MimeType = "application/xml",
        ModificationDate = DateTime.UtcNow,
        Relationship = DocumentOperation.DocumentAttachmentRelationship.Data
    });
var bytes = document.GeneratePdfA(out var report, new PdfAOptions
{
    VeraPdfJarPath = "/tools/verapdf/bin/cli-1.30.3.jar",
    Attachments = new[] { attachment }
});
File.WriteAllBytes("report.pdf", bytes);
File.WriteAllText("report-verapdf.json", report.Json);
```

The runnable `PdfAExamples.CreateValidatedPdfA3AWithAssociatedXml` example demonstrates this
workflow. Byte-array, file and borrowed writable stream overloads are available; streams need
not seek, are written at their current position and remain open. Writer/validator failure leaves
the destination untouched; a failing caller sink can still contain a partial write. Output is
staged in memory, so budget memory for rendering, PDF assembly and complete final bytes.

## Deployment and pinned validation

Install Java 8+ and the official **veraPDF 1.30.3** CLI. The release archive is at
<https://software.verapdf.org/releases/1.30/verapdf-greenfield-1.30.3-installer.zip>.
Use the unmodified `bin/cli-1.30.3.jar`; its SHA-256 is
`19F6EDD5C92841A38D9B3B018A555B7BA173B13E4E0D29EDECC232E5BA6C1D70`.
Runtime checks that hash, which pins the validator engine and all six bundled XML profiles.
The installer/archive and individual profile hashes are recorded in
`Source/FossPDF.UnitTests/Parity/install_verapdf.py` and its generated qualification manifest.

Set `PdfAOptions.VeraPdfJarPath` or `FOSSPDF_VERAPDF_JAR`. `JavaExecutablePath` defaults to `java`
on PATH. `ProcessTimeout` defaults to two minutes per validator invocation. A cancellation token
terminates the validator process tree; staging files are cleaned on success and failure. Unix
temporary directories are created with mode 0700; Windows uses the user's inherited temp ACL.
Arguments use the process argument API without a shell. Documents do not appear in command-line
arguments; only a private temporary path is passed. Java uses a 512 MiB maximum heap.

For development and CI:

```text
python Source/FossPDF.UnitTests/Parity/install_verapdf.py --output artifacts/parity/verapdf
# Set FOSSPDF_VERAPDF_JAR to the path printed by the installer.
dotnet test Source/FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release -f net9.0
```

The installer downloads only after explicit invocation, checks the reviewed archive hash and
selects only the CLI pack. CI uses Temurin 17 and publishes original veraPDF JSON reports,
six-profile hashes, PDFs, expected failures and independent rendering/structure checks on Windows,
Linux and macOS Intel. Java and veraPDF are application-deployed external tools and are never
bundled in FossPDF packages; retain their applicable licenses/notices. See the
[dependency inventory](NATIVE_DEPENDENCIES.md) and [evidence](evidence/fp-402-windows.md).

## Legacy mapping and limits

- Ordinary `GeneratePdf` with `DocumentMetadata.PdfA = true` or `PdfAConformance.Legacy` retains
  Skia's existing behavior: it requests PDF/A-2b identification, font embedding and an sRGB intent.
  It does not validate every document. The qualification fixture passes PDF/A-2b, but unsupported
  glyphs or other invalid content can still fail. `GeneratePdfA` maps either legacy selection to
  PDF/A-2b and validates it. `PdfAValidator.Validate(bytes, PdfAConformance.PdfA2B)` can audit old
  files without converting or changing them.
- Explicit None overrides the boolean. No selection with a false boolean is an error for
  `GeneratePdfA`. Ordinary `GeneratePdf` directs six-profile selections to `GeneratePdfA`; the
  companion is never implicitly loaded and core never references it.
- Arbitrary associated files are supported only with PDF/A-3. `PdfAAttachment` copies bytes and
  metadata immediately and requires ModificationDate. MIME type, Unicode name, both EF entries,
  AFRelationship and catalog AF are written. All five standard relationships are qualified.
  This writer rejects all PDF/A-2 attachments, including potentially conforming nested PDFs.
- `MetadataExtensions` accepts qualified XMP property elements, including PDF/A extension-schema
  declarations. Existing/reserved properties cannot be replaced. Undeclared custom schemas fail
  final validation. This is not arbitrary existing-PDF conversion; author the document through
  FossPDF and select the profile before generation.
- Encryption is absent from this generation API. An encrypted file fails PDF/A validation. Editing,
  attaching or encrypting the result later can invalidate conformance; validate after later edits.
  Ordinary DocumentOperation saves do not preserve semantic trees or guarantee archival status.
- Additional compression, linearization and decompression use the existing qpdf options. The
  archival writer adds `--newline-before-endstream` to preserve the required stream delimiters; see
  [qpdf documentation](https://qpdf.readthedocs.io/en/stable/cli.html#option-newline-before-endstream). Results are
  validated after optimization for every profile. qpdf 11+ must be deployed when selected.
- A levels require document language, figure alternatives and supported semantic associations.
  Existing tagging restrictions, including ZIndex, apply. Content authors remain responsible for
  correct language, reading order and meaningful alternatives. PDF/A-A is not a PDF/UA claim;
  combined PDF/A and PDF/UA requests use the qualified machine checks in
  [GeneratePdfUa](PDF_UNIVERSAL_ACCESSIBILITY.md), with human accessibility review still required.
