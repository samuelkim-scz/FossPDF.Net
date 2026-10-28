# Selectable PDF/A-2 and PDF/A-3 conformance (FP-402)

FossPDF can write six PDF/A profiles: **PDF/A-2a, 2b, 2u, 3a, 3b and 3u**. The level is a
document setting or an explicit argument, the written XMP identification always names the resolved
profile, and every level is validated with the pinned veraPDF release in CI.

PDF/A output is produced by the optional **`FossPDF.PdfOperations`** package (`net9.0`, PDFsharp
6.2.4). It reuses the semantic capture introduced by [FP-401](PDF_ACCESSIBILITY.md), so the core
package keeps no PDF reader/writer dependency; see
[ADR-0001](decisions/ADR-0001-native-backends.md). PDF/A-4, PDF/UA-2 and validation of the legacy
boolean as its own profile are outside this work item.

> The profile declaration is only written when a level is selected. `GeneratePdf` (the core Skia
> writer) does not produce PDF/A; it rejects a selectable profile with `NotSupportedException` and
> points at `GeneratePdfA`. A PDF/A artifact is a conformance claim only for the exact part and
> level in its XMP packet.

## Supported levels

| Level | Part | Conformance | What it adds |
| --- | --- | --- | --- |
| `PdfAConformance.PdfA2A` | 2 | A (accessible) | Tagged logical structure and a document language in addition to the visual-fidelity rules. |
| `PdfAConformance.PdfA2B` | 2 | B (basic) | Visual fidelity; no structure or Unicode-extraction requirement. FossPDF still writes the structure it captured. |
| `PdfAConformance.PdfA2U` | 2 | U (Unicode) | Visual fidelity plus Unicode mappings for all text. |
| `PdfAConformance.PdfA3A` | 3 | A (accessible) | PDF/A-2a rules plus embedded files and the associated-files array. |
| `PdfAConformance.PdfA3B` | 3 | B (basic) | PDF/A-2b rules plus embedded files. |
| `PdfAConformance.PdfA3U` | 3 | U (Unicode) | PDF/A-2u rules plus embedded files. |

`PdfAConformance.Legacy` and `DocumentMetadata.PdfA` carry no part or level. Both map to
**PDF/A-2B**, the closest validated profile, and the written XMP states `2` and `B` explicitly.
`PdfAConformance.None` (the default) disables PDF/A output and overrides a legacy `PdfA = true`
flag.

## Quick start

Select the level once in the settings:

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
        });
    });
})
.WithMetadata(new DocumentMetadata
{
    Title = "Quarterly totals",
    Author = "FossPDF",
    Language = "en-US"
})
.WithSettings(new DocumentSettings
{
    PdfAConformance = PdfAConformance.PdfA2B
});

document.GeneratePdfA("report-pdfa-2b.pdf");
```

Or pass the level to the call:

```csharp
var bytes = document.GeneratePdfA(PdfAConformance.PdfA3U, new PdfAOptions
{
    Attachments = new[]
    {
        new DocumentOperation.DocumentAttachment
        {
            FilePath = "invoice.xml",
            AttachmentName = "invoice.xml",
            MimeType = "application/xml",
            Relationship = DocumentOperation.DocumentAttachmentRelationship.Data
        }
    }
});
```

The overloads mirror the tagged-PDF API: `GeneratePdfA()` returning `byte[]`, writing to a file
path, or writing to a caller-owned `Stream`. Every target accepts an explicit level; the level and
`PdfAOptions` can be combined for byte arrays, file paths and streams, and the byte overload also
accepts options alone. The stream is written once and is neither closed nor seeked.

The two-stage API exposes the captured content and the PDF/A assembly separately:

```csharp
var content = document.GenerateTaggedContent();         // core: semantic tree + paint batches
var pdf = content.ToPdfA(PdfAConformance.PdfA2U);       // companion: PDF/A assembly

// With a level selected in the settings, both of these emit that profile:
document.GenerateTaggedPdf("report.pdf");
document.GeneratePdfA("report.pdf");
```

`GeneratePdfA`, `ToPdfA` and `PdfAOptions` are extension/public types in `FossPDF.Fluent` and
`FossPDF.PdfOperations`, provided by the optional package.

## Selecting the level in one place

The level must be unambiguous:

- An explicit `GeneratePdfA(conformance)` may be passed with or without a settings selection; when
  both are present they must agree.
- If the settings select `PdfA2B` and the call requests `PdfA3B`, the call throws
  `InvalidOperationException` naming both levels. Choose the level in the settings **or** in the
  call, not in both places with different values.
- Passing `PdfAConformance.None` or an undefined enum value throws `ArgumentOutOfRangeException`;
  `None` means "no PDF/A", so there is nothing to write.
- Calling `GeneratePdfA()` with no selection and no legacy flag throws `InvalidOperationException`
  listing the six supported values.
- Settings selected a profile and `GenerateTaggedPdf` was used: the tagged writer emits the same
  selectable profile, because the capture records the setting.

## What the writer produces

Every profile writes:

- **XMP identification.** A well-formed XMP packet in the catalog `/Metadata` with
  `pdfaid:part` (`2` or `3`) and `pdfaid:conformance` (`A`, `B` or `U`) in
  `http://www.aiim.org/pdfa/ns/id/`, plus the document title from `DocumentMetadata`.
- **Output intent.** A catalog `/OutputIntents` array with `/S /GTS_PDFA1` and an embedded sRGB ICC
  profile.
- **Embedded fonts and Unicode mappings.** Text stays vector; fonts are embedded and carry
  `/ToUnicode` maps, so independent readers extract the heading and paragraph text.
- **Tagged structure.** `/StructTreeRoot` with `/Document` and the captured reading order,
  `/MarkInfo << /Marked true >>`, the catalog `/ParentTree` and `/ViewerPreferences
  << /DisplayDocTitle true >>`.
- **Language.** Catalog `/Lang` from `DocumentMetadata.Language`. The language is removed when the
  metadata does not declare one; the writer never invents a default language.
- **Document information.** Title, author, subject, keywords, creator, producer and timestamps from
  `DocumentMetadata`, as for tagged PDF.

PDF/A-3 additionally writes embedded files into the name tree and the catalog `/AF` array, with the
media type (`/Subtype`), modification date (`/Params /ModDate`), Unicode name (`/UF`) and
associated-file relationship (`/AFRelationship`). See [attachments](#attachments-in-pdfa-3).

### A, U and B levels

- **A levels** require a logical structure tree and a natural language. `GeneratePdfA` throws
  `PdfOperationException` naming the level when `DocumentMetadata.Language` is blank. Structure is
  already produced by the tagged writer; wrap meaningful content in semantic elements and leave
  decoration as artifacts.
- **U levels** require every font to have a Unicode mapping. The Skia/PDFsharp pipeline embeds the
  fonts and writes `/ToUnicode` maps; the validation suite asserts that mapping for every font in
  every profile.
- **B levels** require only visual fidelity. FossPDF still writes the captured structure and Unicode
  maps; selecting a B level does not remove them.

Set `Title` and `Language` for every PDF/A document. The title is part of the XMP and the
`DisplayDocTitle` viewer preference; the fixture and verifier assert both.

## Attachments in PDF/A-3

Pass embedded files through `PdfAOptions.Attachments` using the same
`DocumentOperation.DocumentAttachment` type as [FP-305](PDF_ATTACHMENTS.md):

```csharp
document.GeneratePdfA(PdfAConformance.PdfA3U, new PdfAOptions
{
    Attachments = new[]
    {
        new DocumentOperation.DocumentAttachment
        {
            FilePath = "invoice.xml",
            AttachmentName = "invoice.xml",
            MimeType = "application/xml",
            Description = "Invoice data",
            Relationship = DocumentOperation.DocumentAttachmentRelationship.Data
        }
    }
});
```

- Files are read when the PDF/A output is generated, so later file changes do not affect an
  already-written document. The option list is copied at generation time.
- When an attachment does not declare a modification date, the document's modification date is
  used, because PDF/A-3 requires one for every embedded file.
- Set the media type and a relationship that describes the file's role. `Unspecified` is allowed by
  the format but carries no meaning for downstream invoice or attachment consumers.
- Attachments are rejected for PDF/A-2 (including 2a/2b/2u) and when no profile is selected:

| Combination | Result |
| --- | --- |
| `PdfAOptions.Attachments` with PDF/A-3 | Written to the name tree and `/AF`. |
| `PdfAOptions.Attachments` with PDF/A-2 | `ArgumentException` before any output, message names PDF/A-3. |
| `PdfAOptions.Attachments` without a profile | `ArgumentException` before any output. |

## Rejected or unavailable combinations

| Combination | Behavior |
| --- | --- |
| `GeneratePdf` with a selectable `PdfAConformance` | `NotSupportedException`; use `GeneratePdfA` (or `GenerateTaggedPdf` with the setting selected). |
| Different levels set in settings and the call | `InvalidOperationException` before rendering. |
| `GeneratePdfA()` with no selected profile | `InvalidOperationException` before rendering. |
| `None` or an undefined value passed explicitly | `ArgumentOutOfRangeException`. |
| A level without `DocumentMetadata.Language` | `PdfOperationException` before assembly. |
| Attachments with PDF/A-2 or without a profile | `ArgumentException` before assembly. |
| `EnableAdditionalCompression`, `Linearize` or `DisableCompression` | `NotSupportedException`; the qpdf pass may rewrite the structure tree and conformance metadata. Optimize in a separate step, if at all. |
| `ZIndex` content | `NotSupportedException`; inherited from tagged capture, because overlapping layers have no single reading order. |
| Encryption | Not part of this API. PDF/A forbids encryption; the writer produces an unencrypted document. |

PDF/A-2 and PDF/A-3 permit transparency, so ordinary backgrounds, images and alpha blending are
allowed. Text and vector graphics stay vector; raster images are embedded as images rather than
re-rendered by the PDF/A writer.

## Framework and package availability

- `FossPDF.PdfOperations` targets `net9.0` only, per ADR-0001. On `net8.0` the selectable-profile
  settings are visible but `GeneratePdfA` is not compiled, and `GeneratePdf` rejects the settings.
- The core package still captures semantic content and exposes `GenerateTaggedContent` on both core
  targets.
- The package references PDFsharp 6.2.4 (MIT) and is not referenced by the core package.

## Implementation notes and limitations

- PDFsharp's PDF/A setup writes a PDF/A-1 identification (`pdfaid:part 1`, conformance `A`). The
  writer replaces those single-character values with the selected part and conformance after saving.
  Both replacements are equal-length ASCII, so no object offset changes. If the packet does not
  contain the expected values, assembly throws `PdfOperationException` instead of emitting a
  misidentified document.
- The output intent is the sRGB ICC profile supplied by PDFsharp. PDF/A does not require a specific
  output intent; documents that must reproduce a particular print condition may need a different
  profile, which this work item does not expose.
- The semantic vocabulary and artifact behavior are those of [FP-401](PDF_ACCESSIBILITY.md).
  Custom structure roles and role maps are not written. The writer always produces tagged
  structure, including for B levels.
- `Legacy` is a compatibility mapping, not a validated level: it always resolves to PDF/A-2B and the
  XMP says so. Code that needs another level must select it explicitly.
- PDF/A-4, PDF/UA-1 and e-invoice packaging are separate work items (FP-403 and FP-404); a PDF/A
  file is not automatically PDF/UA conforming.
- The writer does not validate third-party content imported from other tools; every page is
  rendered by FossPDF from the captured semantic content.

## Validation

- `PdfATests` (`net9.0`) covers all six profiles, settings and explicit selection, the legacy
  mapping, `None`, invalid values, conflicts, missing selection, stream ownership, attachments,
  rejection paths and the two-stage API.
- Parity fixture `ACC-402` renders the ACC-401 semantic document at all six levels, writes the
  attachment variants for PDF/A-3 and asserts the incompatible-combination failures.
- `Parity/verify_pdfa.py` independently re-reads every artifact with pypdf, checks the XMP
  identification, output intent, ICC profile, structure tree, parent tree, language, display
  preference, alternative text, font mappings, extracted heading, attachment name tree and
  associated files, and runs the pinned veraPDF 1.30.3 release against the exact flavour of each
  level. The installer is pinned by URL and SHA-256; the CLI jar is extracted from the installer pack
  and the installer itself is never executed.
- `FossPDF.Examples/PdfAExamples.cs` is a runnable consumer example covering the settings path, the
  explicit PDF/A-3 attachment path, the PDF/A-2 rejection and the legacy mapping.

From `Source`:

```powershell
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PdfAExamples
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PdfATests
python -m pip install -r FossPDF.UnitTests/Parity/requirements-pdfa-validation.txt
python FossPDF.UnitTests/Parity/verify_pdfa.py FossPDF.UnitTests/bin/Release/net9.0/parity-artifacts/ACC-402 --reports ../artifacts/pdfa
```

The verifier downloads the pinned veraPDF release on first use (Java 17+ is required) unless
`--verapdf <path>` points at an existing executable; `--skip-verapdf` runs only the independent
pypdf checks. See [fp-402-windows.md](evidence/fp-402-windows.md) for recorded results and
[PDF_ACCESSIBILITY.md](PDF_ACCESSIBILITY.md) for the semantic API.
