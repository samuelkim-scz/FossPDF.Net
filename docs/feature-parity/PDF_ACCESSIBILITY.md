# Tagged PDF and semantic structure (FP-401)

FossPDF renders documents through Skia, whose PDF backend has no tagging API. FP-401 therefore
splits the feature in two:

1. The **core package** captures a logical semantic tree and every paint batch in reading order
   while it renders. Semantic wrappers are layout-transparent: ordinary PDF, image, SVG, XPS and
   preview output is unchanged.
2. The optional **`FossPDF.PdfOperations` package** (`net9.0`, PDFsharp 6.2.4) replays each paint
   batch as a form XObject, marks it with its structure role, and assembles the PDF structure tree,
   parent tree and catalog entries. Nothing is rasterized.

The writer lives in the companion package, so the core package keeps no PDF reader/writer
dependency; see [ADR-0001](decisions/ADR-0001-native-backends.md).

> This work item produces a structurally tagged PDF. It is **not** a PDF/UA-1 conformance claim;
> independent accessibility validation and human review belong to [FP-403](https://github.com/samuelkim-scz/FossPDF.Net/issues/28)
> ([PDF/UA contract](ACCEPTANCE.md)). PDF/A profile claims belong to [FP-402](https://github.com/samuelkim-scz/FossPDF.Net/issues/27).

## Quick start

```csharp
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;

var document = Document.Create(container =>
{
    container.Page(page =>
    {
        page.Size(320, 240);
        page.Margin(20);

        // Content without a semantic wrapper is an artifact: assistive technology skips it.
        page.Header().SemanticIgnore().Text("Quarterly report");

        page.Content().Column(column =>
        {
            column.Item().SemanticHeading1().Text("Quarterly totals");
            column.Item().SemanticParagraph().Text("Revenue is twelve units.");
            column.Item().SemanticFigure("Four colored quadrants.").Height(60).Background("#ff0000");
            column.Item().SemanticLink("FossPDF website")
                .Hyperlink("https://www.nuget.org/packages/FossPDF")
                .Text("FossPDF website");
        });
    });
}).WithMetadata(new DocumentMetadata
{
    Title = "Accessible quarterly report",
    Language = "en-US"
});

// Byte, file-path and stream overloads exist; caller-owned streams stay open.
document.GenerateTaggedPdf("report.pdf");
```

The two-stage API is also public, for callers that want to inspect the captured content before
assembling it:

```csharp
var content = document.GenerateTaggedContent(); // semantic tree + ordered paint batches
var pdf = content.ToTaggedPdf();                // structure tree + marked content
```

`GenerateTaggedPdf` and `ToTaggedPdf` are extension methods in `FossPDF.Fluent`, provided by the
`FossPDF.PdfOperations` package. `GenerateTaggedContent` is part of the core package.

## Semantic API

| Wrapper | PDF role | Notes |
| --- | --- | --- |
| `SemanticSection` | `/Sect` | |
| `SemanticArticle` | `/Art` | |
| `SemanticDivision` | `/Div` | |
| `SemanticHeading1` … `SemanticHeading6` | `/H1` … `/H6` | |
| `SemanticParagraph` | `/P` | |
| `SemanticBlockQuotation` | `/BlockQuote` | |
| `SemanticList` / `SemanticListItem` / `SemanticListLabel` / `SemanticListItemBody` | `/L` / `/LI` / `/Lbl` / `/LBody` | Nest list items inside a list and labels/bodies inside an item. |
| `SemanticTable` / `SemanticHorizontalHeader` | `/Table` / `/TH` | |
| `SemanticTableOfContents` / `SemanticTableOfContentsItem` | `/TOC` / `/TOCI` | |
| `SemanticIndex` | `/Index` | |
| `SemanticSpan(alternativeText?)` | `/Span` | The alternative text describes an abbreviation or expansion. |
| `SemanticQuote` / `SemanticCode` | `/Quote` / `/Code` | |
| `SemanticLink(description?)` | `/Link` | Associates URL and section-link annotations with the structure element. |
| `SemanticFigure(alternativeText?)` / `SemanticImage(alternativeText?)` | `/Figure` | |
| `SemanticFormula(alternativeText?)` | `/Formula` | |
| `SemanticCaption` | `/Caption` | |
| `SemanticLanguage("de-DE")` | language attribute | Scopes the content language; the wrapper role is `/Span` unless another semantic wrapper is nested. |
| `SemanticIgnore` | artifact | Marks the whole subtree decorative. Nested semantic wrappers are suppressed. |

Wrappers compose in either order. A wrapper that adds an attribute (`SemanticFigure`, `SemanticSpan`,
`SemanticLanguage`) creates its own structure element, so the attributes stay on the element that
owns them; nesting is written as nested elements.

### Artifact default

Content that is not wrapped in a semantic element is automatically marked as an artifact
(`/Artifact BMC`) and is not placed in the structure tree. This is the safe default: untagged
decoration, page headers/footers and background shapes are never announced as content.
`SemanticIgnore` states that intent explicitly and suppresses nested semantic wrappers as well.

Wrap every piece of meaningful content. A document may legitimately contain only artifacts.

## What the writer produces

- A `/StructTreeRoot` with a `/Document` element and the captured reading order; each semantic
  element has `/S`, optional `/Alt` and `/Lang`, and a `/P` back-reference.
- One form XObject per paint batch, painted by the page content stream exactly once. The form
  stream is wrapped in `<role> << /MCID 0 >> BDC … EMC` (or `/Artifact BMC … EMC`), and the form
  declares `/StructParents`.
- Marked-content references (`/MCR`) with `/Pg`, `/Stm` and `/MCID`, plus object references
  (`/OBJR`) for link annotations. The catalog `/ParentTree` maps each `/StructParents` key and each
  annotation `/StructParent` back to the owning structure element.
- Catalog `/MarkInfo << /Marked true >>`, `/ViewerPreferences << /DisplayDocTitle true >>` and
  `/Lang` from `DocumentMetadata.Language`.
- Document information (title, author, subject, keywords, creator, producer, dates) from
  `DocumentMetadata`.
- URL link annotations and `Section()`/`SectionLink()` page destinations. Named destinations are
  rewritten so `/Dest` points at the destination page rather than at the intermediate fragment.
  Annotations carry `/StructParent` and are associated with their `Link` element.

A semantic element that spans several pages (for example a long paragraph or a repeated component)
stays **one** structure element with one marked-content reference per page. PDFsharp records its
own producer string and preserves the caller's value as `(Original: …)` in `/Producer`.

## Limitations

- `GenerateTaggedContent` rejects documents that use `ZIndex` and documents produced with the qpdf
  extra-compression/linearization settings (`NotSupportedException`). Both features compose content
  in a way that cannot be captured in reading order.
- Only link annotations are associated through `/OBJR`. Other annotation types are left untouched.
- The tag set is the standard structure vocabulary listed above. Custom roles, role maps, XMP
  metadata streams and PDF/A or PDF/UA declarations are not added by this work item.
- `FossPDF.PdfOperations` targets `net9.0`; on `net8.0` only the core capture APIs are available.
- The tagged output is structurally valid and independently checked, but it is not a conformance
  claim. See the FP-402/FP-403 contracts for profile validation.

## Validation

- Core capture: `SemanticStructureTests` (net8.0 and net9.0) proves that semantic wrappers do not
  change rendered pixels, and covers reading order, alternative text, language, artifacts,
  multi-page elements and repeated headers.
- Assembly: `PdfTaggingTests` (net9.0) parses the produced bytes and verifies the structure tree,
  parent tree, marked-content/object references, roles, language, metadata, link destinations and
  the public overloads.
- Parity fixture `ACC-401` renders the catalog document and checks it, including a paragraph split
  across pages and a repeated header. `Parity/verify_pdf_tagging.py` independently verifies the
  structure tree, parent tree, artifacts, text extraction and `qpdf --check` output with pypdf.
- `FossPDF.Examples/AccessiblePdfExamples.cs` is a runnable consumer example.

From `Source`:

```powershell
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=SemanticTagging
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~AccessiblePdfExamples
python Source/FossPDF.UnitTests/Parity/verify_pdf_tagging.py Source/FossPDF.UnitTests/bin/Release/net9.0/parity-artifacts/ACC-401
```

The independent script needs `pypdf` (`Source/FossPDF.UnitTests/Parity/requirements-tagging-validation.txt`)
and `qpdf` on `PATH` (or pass `--qpdf <path>`). See
[fp-401-windows.md](evidence/fp-401-windows.md) for recorded results.
