# PDF/UA-1 generation and review (FP-403)

`GeneratePdfUa` in the optional **.NET 9** `FossPDF.PdfOperations` package produces a tagged PDF
with PDF/UA-1 identification and requires the final bytes to pass veraPDF 1.30.3 machine checks.
Authors must also check reading order, meaningful alternatives, language and assistive usability.
The [validator documents the distinction between machine and human checkpoints](https://docs.verapdf.org/validation/).
An automated pass cannot establish those human outcomes.

## Generate and retain evidence

```csharp
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.PdfOperations;

var document = Document.Create(container => container.Page(page =>
{
    page.Header().Text("Decorative report header");
    page.Content().Column(column =>
    {
        column.Item().SemanticHeading1().Text("Service report");
        column.Item().Text("Twelve requests completed; four requests await a response.");
        column.Item().SemanticLink("Read the report documentation")
            .Hyperlink("https://example.org/report").Text("Report documentation");
    });
})).WithMetadata(new DocumentMetadata { Title = "Service report", Language = "en-US" })
  .WithSettings(new DocumentSettings
  {
      PdfUaConformance = PdfUaConformance.PdfUa1,
      PdfAConformance = PdfAConformance.PdfA3A // Optional combined archival output.
  });

var bytes = document.GeneratePdfUa(out var report, new PdfUaOptions
{
    VeraPdfJarPath = "/tools/verapdf/bin/cli-1.30.3.jar"
});
File.WriteAllBytes("report.pdf", bytes);
File.WriteAllText("report-ua1.json", report.Json);
File.WriteAllText("report-pdfa3a.json", report.ArchivalReport!.Json);
// Review the document in a screen reader/accessibility inspection tool before publication.
```

Use `PdfAConformance.None` for PDF/UA alone. All six PDF/A-2/3 profiles can be combined with
PDF/UA-1; the result is tagged even when the archival selection is B or U. Both profiles must
pass independently. `GeneratePdfA` also honors a simultaneous PDF/UA-1 selection and checks both.
`GeneratePdfUa(out report)` exposes both reports. A failed archival profile throws
`PdfAConformanceException`; failed PDF/UA machine checks throw `PdfUaConformanceException`.
The actual report is available on either exception. No failed PDF is returned or written.

File and borrowed writable stream overloads stage before writing. Streams need not seek and stay
open. A failing caller sink can still contain a partial write. `PdfUaValidator.Validate(bytes)`
audits an existing PDF without modifying it; `MachineChecksPassed` deliberately describes only
the automated result. The runnable `PdfUaExamples` example includes associated XML and both reports.

## Supply accessibility information

- Set a meaningful document `Title` and a `Language` in BCP 47 form, such as `en-US`. Private-use
  and grandfathered syntax are accepted; authors select the correct language. `SemanticLanguage`
  overrides language for a subtree. Missing declarations or malformed language syntax fail.
- Use `SemanticHeading1` through `SemanticHeading6` for meaningful heading hierarchy. Visual font
  styling alone does not create heading semantics. Text, images, Canvas/SVG, links and tables use
  the [automatic semantic capture](PDF_ACCESSIBILITY.md), with explicit wrappers overriding it.
- Give meaningful figures/formulas alternatives through `SemanticFigure(text)` or
  `SemanticFormula(text)`. Use `SemanticIgnore()` for genuinely decorative content. Missing
  figure/formula alternatives fail before serialization; an unhelpful alternative needs human review.
- Container links require `SemanticLink(description)`. Inline text links use their supplied text.
  Link annotations receive Contents descriptions, structure associations and structure tab order.
  Do not put interactive content inside an ignored subtree; invalid untagged annotations fail validation.
- Lists use `SemanticList`, `SemanticListItem`, `SemanticListLabel` and `SemanticListItemBody`.
  Tables capture rows, cells and header scope automatically. `SemanticHorizontalHeader` and
  `SemanticVerticalHeader` select column and row scope. Complex tables that require explicit
  header-ID associations are not supported by this API; simplify the table or use another
  qualified workflow. Machine validation and a human table-navigation check remain necessary.
- Headers, footers and page layers are decorative by default; explicit semantics opt into the tree.
  Check that this matches their actual meaning. Body content must not be hidden from the logical
  structure merely to make a validator pass.

All glyphs must be available, fonts remain embedded with Unicode mappings, and the structure root
has a complete parent chain. Skia painting stays intact; the writer does not infer semantics from
flattened text. The original document is rendered directly, preserving merged pagination and
content-position capture. `ZIndex` remains unsupported for semantic capture.

## Tools, optimization and limits

The deployment contract is shared with [PDF/A](PDF_ARCHIVAL.md): application-deployed Java 8+,
the unmodified veraPDF **1.30.3** CLI JAR, and `PdfUaOptions.VeraPdfJarPath` or
`FOSSPDF_VERAPDF_JAR`. Runtime checks the qualified JAR hash. `JavaExecutablePath` and
`ProcessTimeout` are inherited from `PdfAOptions`; cancellation terminates the process tree.
The installer manifest also pins the PDF/UA-1 XML profile hash
`794c9f6bf53c8cb89e532807e74fea58331a0d9a935c52cb054b63853bdd8b2a`.
No validator or new native engine is bundled with FossPDF.

Additional compression, linearization and decompression run before validation. qpdf 11+ must be
deployed when selected. For combined archival output the writer adds the required EOL stream
delimiters and validates both profiles after qpdf. PDF/A-3 associated files use the same owned
`PdfAAttachment` snapshots as the archival writer; other conformance selections reject attachments.
`MetadataExtensions` can add qualified XMP properties and schema declarations; the PDF/UA
identification schema is preserved when additional schemas are merged.

Ordinary `GeneratePdf` and `GenerateTaggedPdf` do not silently claim PDF/UA and direct explicit
PDF/UA selections to this API. Ordinary `DocumentOperation` editing does not preserve the semantic
tree or guarantee conformance. Do not edit/encrypt the finished file through that path and assume
it remains accessible. This is not general existing-PDF remediation, PDF/UA-2, signing or AcroForms.

## Acceptance status and human review

Machine qualification covers sixteen output combinations, exact reading-order transcripts,
language, figures, list/table structure, links, fonts, rendering and deliberate negative fixtures.
See [evidence](evidence/fp-403-accessibility.md) and the immutable
[review PDF and checklist](../../Source/FossPDF.UnitTests/Resources/AccessibilityReview/README.md).

The repository user reported the immutable fixture was satisfactory and identified Acrobat
as the review tool. Its version and exact inspection time were not reported; those limitations
are explicit in the evidence. ACC-403 checks the recorded human observations and exact PDF hash.
Machine tests execute independently and do not manufacture a human pass. Applications using the
API own meaningful-content and usability review for their documents.
