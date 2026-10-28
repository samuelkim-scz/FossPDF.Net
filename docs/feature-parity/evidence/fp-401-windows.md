# FP-401 Windows validation

Recorded October 28, 2026, for [FP-401 / issue #26](https://github.com/samuelkim-scz/FossPDF.Net/issues/26), on `feature/fp-401-tagged-pdf` based on `feature/fp-306-pdf-compression` at commit `c6efa50`.

Environment: Windows 11 Pro x64 (10.0.26200), .NET SDK 10.0.301, .NET 9.0.9 runtime, SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.5, PDFsharp 6.2.4 (MIT, exact pin). Independent inspection used Python 3.12.10 with pypdf 6.21.2 and qpdf 12.4.2 (`C:\Program Files\qpdf 12.4.2\bin\qpdf.exe`, not on `PATH`); the script imports no artifact paths and every path is passed as an argument.

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Zero warnings, zero errors |
| Complete .NET 8 suite | 493 passed, 0 failed, 13 skipped (6 planned contracts + 7 net9.0-scoped fixtures) |
| Complete .NET 9 suite | 632 passed, 0 failed, 6 skipped (planned contracts) |
| Parity category | 28 passed / 13 skipped on .NET 8; 35 passed / 6 skipped on .NET 9 |
| `ACC-401` fixture | Active and passing on .NET 9; skipped on .NET 8 by declared framework scope |
| `SemanticStructureTests` (core) | 11 cases passed on both frameworks |
| `PdfTaggingTests` (companion) | 11 passed on .NET 9 |
| Accessibility examples | 3 passed |
| `FossPDF.Quality --mode all` | All requested checks passed; the tool's optional qpdf/veraPDF conformance checks remain skipped because it does not discover those executables |
| Independent pypdf + qpdf inspection | 77 checks passed |

## Verified behavior

- `GenerateTaggedContent()` renders the document into the semantic tree and ordered paint batches entirely in the core package. `GenerateTaggedPdf()` (bytes, file path or stream), `ToTaggedPdf()` and the two-stage `GenerateTaggedContent()`/`ToTaggedPdf()` pair live in `FossPDF.PdfOperations`; the core package references no PDF library. Caller-owned output streams stay open, and null/non-writable arguments are rejected.
- Semantic wrappers (`Sect`, `Art`, `Div`, `H1`–`H6`, `P`, `BlockQuote`, `L`, `LI`, `Lbl`, `LBody`, `Table`, `TH`, `TOC`, `TOCI`, `Index`, `Span`, `Quote`, `Code`, `Link`, `Figure`, `Formula`, `Caption`) are layout-transparent: the pixel output of a semantic document equals the plain document, page by page.
- The produced PDF has `/StructTreeRoot` with a `/Document` element and the captured reading order. Each structure element carries `/S`, optional `/Alt` and `/Lang`, and `/P` back-references. Catalog entries `/MarkInfo << /Marked true >>`, `/ViewerPreferences << /DisplayDocTitle true >>` and `/Lang` are present; title, author, subject, keywords, creator, producer and dates come from `DocumentMetadata`.
- Every paint batch is imported as a form XObject and painted exactly once by the page content stream. Tagged forms are wrapped in `<role> << /MCID 0 >> BDC … EMC`, carry `/StructParents`, and are mapped through the catalog `/ParentTree`. Artifact forms are wrapped in `/Artifact BMC … EMC`, carry no `/StructParents`, and never appear in the structure tree. Content that is not wrapped by a semantic element is automatically an artifact; `SemanticIgnore` states it explicitly and suppresses nested semantic wrappers.
- A semantic element split across pages stays one structure element with one `/MCR` per page (`/Pg`, `/Stm`, `/MCID`), including a long paragraph and a repeated header/footer component.
- Nested lists produce `L` → `LI` → `Lbl`/`LBody` and nested tables produce `Table` → `TH`/`P`. Alternative text is exact (`Four colored quadrants.`), and `SemanticLanguage` scopes the language on the wrapper that owns it.
- URL link annotations and `Section()`/`SectionLink()` page destinations keep their annotations, gain `/StructParent`, and are associated with the `Link` element through `/OBJR`. The fixture's internal named destination is rewritten to reference the destination page, not the intermediate fragment.
- Nested `SemanticLanguage` inside `SemanticSpan` stays nested (outer `Span` with `/Alt`, inner `Span` with `/Lang`) instead of merging attributes onto one element; the tests and guide document this behavior.
- `ZIndex` and the qpdf extra-compression/linearization settings are rejected with `NotSupportedException` before any output is produced, because their composition cannot be captured in reading order.
- An empty-semantic document still produces a valid tagged PDF: a `/Document` element with no children, `/MarkInfo /Marked true` and an empty parent tree.

## Independent validation

`verify_pdf_tagging.py` re-reads the fixture with pypdf (a different implementation from PDFsharp) and runs qpdf as an independent structural checker:

```text
PASS ACC-401: the structure tree holds the fixture content in reading order
PASS ACC-401: the figure alternative text is exact
PASS ACC-401: artifacts are stored as /Artifact BMC forms without a parent-tree entry
PASS ACC-401: a paragraph split across pages stays one structure element
PASS ACC-401: every marked-content reference resolves through the parent tree
PASS ACC-401: heading and paragraph text extract correctly
PASS ACC-401: qpdf --check reports a structurally valid PDF
PASS ACC-401 (77 independent checks)
```

qpdf reports `No syntax or stream encoding errors found` and exit code 0. The script also verifies that form streams contain `BDC`/`EMC` and `/MCID 0`, that each `/Pg` resolves to a real page, and that tagged link annotations map back to their structure element.

## Artifacts

`ACC-401` writes beneath `Source/FossPDF.UnitTests/bin/Release/net9.0/parity-artifacts/ACC-401/`: `tagged.pdf` (three pages: the fixture content, the spanning paragraph and the repeated header artifact).

The examples write beneath `Source/FossPDF.Examples/bin/Release/net9.0/accessible-pdf/`: `tagged-report.pdf`, `inline-language.pdf` and `tagged-links.pdf`.

`PdfTaggingTests.OverloadsWriteTheSameTaggedDocument` also writes `parity-artifacts/FP-401/overloads.pdf`.

## Reproduce

From `Source`:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=SemanticTagging
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~AccessiblePdfExamples
dotnet run --project FossPDF.Quality/FossPDF.Quality.csproj -c Release -- --mode all --output ../artifacts/quality
python -m pip install -r FossPDF.UnitTests/Parity/requirements-tagging-validation.txt
python FossPDF.UnitTests/Parity/verify_pdf_tagging.py FossPDF.UnitTests/bin/Release/net9.0/parity-artifacts/ACC-401
```

The independent script accepts `--qpdf <path>` when qpdf is not on `PATH`. CI installs qpdf 11+ on Ubuntu, macOS and Windows before running the verification step.

## Boundaries

- The output is structurally tagged. This is not a PDF/UA-1 or PDF/A conformance claim; independent profile validation and human accessibility review belong to FP-403 and FP-402. No XMP accessibility declaration is written.
- Only link annotations are associated through `/OBJR`; other annotation types are preserved but not linked to the structure tree.
- The standard structure vocabulary is supported; custom roles and role maps are not.
- `FossPDF.PdfOperations` targets `net9.0` only per ADR-0001. The core capture APIs exist on both core targets, and no core package reference was added.
- PDFsharp records its own producer string in `/Producer` and preserves the caller's value as `(Original: …)`; this is PDFsharp behavior, asserted by the metadata test.
