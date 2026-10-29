# FP-403 machine qualification and human review

The .NET 9 companion provides `GeneratePdfUa`, with final veraPDF 1.30.3 machine validation and
combined PDF/A-2/3 validation. [API and responsibilities](../PDF_UNIVERSAL_ACCESSIBILITY.md).

## Machine evidence

- The representative document has two pages, H1/H2 hierarchy, a two-item labelled list, a table
  with column headers, a chart alternative, external/inline/section links and a French paragraph.
  Decorative header/footer content is excluded from its logical reading sequence.
- Four PDF/UA-only optimization modes and six combined PDF/A profiles in default/optimized modes
  produce **sixteen** positive documents. Combined optimized cases use additional compression
  and linearization together. PDF/A-3 cases retain their associated XML payload exactly.
- Each final PDF/UA result must pass the exact pinned profile; combined results must also pass
  their archival profile. The same 1.30.3 JAR/archive pins from FP-402 apply, plus PDFUA-1.xml
  SHA-256 `794c9f6bf53c8cb89e532807e74fea58331a0d9a935c52cb054b63853bdd8b2a`.
- Independent pypdf/qpdf/PyMuPDF 1.26.7 inspection passes **1,905 checks**, including complete
  structure parent chains and reverse MCID/annotation associations, annotation descriptions/tab
  order, title/language, labels/bodies, header scope, alternatives, Unicode, attachment bytes and
  strict parsing/linearization. Rendering matches ordinary output within 2/255. Semantic content
  for every combination matches the immutable human-review file, with whitespace and equivalent
  Unicode presentation forms normalized for extraction.
- **Nine** deliberate failures are rejected: missing structure-root parent, XMP title, language,
  figure alternative, link Contents, tab order, identification, Unicode map and header scope.
- Local Release solution build passes with zero warnings/errors. Full suites pass **499** tests
  on .NET 8 (13 framework/planned skips) and **674** on .NET 9 (five planned skips). Additional
  focused checks cover the locked review fixture, language syntax and runnable example.
- CI repeats machine qualification on Windows/Linux/macOS Intel, publishing original validator
  reports, PDF files, structured transcripts, negatives and the exact tool/profile manifest.

```powershell
$env:FOSSPDF_VERAPDF_JAR = '...\bin\cli-1.30.3.jar'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Source/FossPDF.sln -c Release --no-restore --warnaserror
dotnet test Source/FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
python Source/FossPDF.UnitTests/Parity/verify_pdfua.py Source/FossPDF.UnitTests/bin/Release/net9.0/parity-artifacts/ACC-403
dotnet test Source/FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PdfUaExamples
```

## Human evidence: user-reported Acrobat review

The immutable review file is
`Source/FossPDF.UnitTests/Resources/AccessibilityReview/quarterly-service-report-r1.pdf`, SHA-256
`7f954761d56222cc8c8cb42f61c9d17041451e923ea75ece88436ef7c8189b68`.
It independently passes PDF/UA-1 machine checks, PDF/A-3a and qpdf. Rendered pages were inspected
by the coding agent; that is not a human accessibility-tool review.

The repository user replied 'its fine' to the exact-file, eight-point review request, then
identified the tool as 'acrobat'. The [review record](../../../Source/FossPDF.UnitTests/Resources/AccessibilityReview/human-review.json)
records all eight items as user-reported passes against the exact PDF hash. It identifies the
reviewer as the repository user and timestamps receipt of the report. The Acrobat version and
exact inspection time were not supplied and remain explicitly unavailable; no version, checker
output or agent-performed screen-reader result is invented.

This is a user-reported human review of the qualification fixture. It is not an Acrobat automated
checker report or a guarantee about other authored documents. The
[checklist](../../../Source/FossPDF.UnitTests/Resources/AccessibilityReview/README.md) records the
observations requested. ACC-403 is active and verifies the evidence fields, eight reported checks
and exact PDF hash. Machine qualification remains a separate test and CI artifact set.
