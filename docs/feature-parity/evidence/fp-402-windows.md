# FP-402 PDF/A qualification

The optional .NET 9 companion implements six explicit archival profiles through `GeneratePdfA`.
The final bytes must pass the pinned independent veraPDF validator before output is returned.
See [API, deployment and limits](../PDF_ARCHIVAL.md).

## Reproduction and artifacts

`ACC-402` generates all six profiles in four modes: default, additional compression, linearized
and uncompressed. Each PDF/A-3 file contains all five associated-file relationships, Unicode
filenames, MIME types and modification dates. A-level fixtures include headings, a paragraph,
a figure alternative, a table and a link. U-level text includes café, résumé and office. Legacy
Skia output and the validated legacy mapping are checked separately as PDF/A-2b.

The CI main job on Windows/Linux/macOS Intel installs the exact reviewed archive using
`install_verapdf.py`, verifies the CLI and all six profile hashes, runs ACC-402 and publishes
the original JSON reports and PDFs. `verify_pdfa.py` independently checks the PDF catalog,
identification XMP, fonts/ToUnicode, RGB ICC output intent, semantic tree, attachment bytes and
metadata, strict qpdf parsing/hint tables, and rendering against ordinary output within the
existing 2/255 antialiasing tolerance. It creates seven corruptions (missing output intent,
font program, Unicode map, tags, identification, encryption, and AFRelationship) and requires veraPDF to reject
them, retaining the reports. Unit tests cover preflight failures, caller-owned sinks, missing
glyphs, undeclared XMP extensions, snapshots and real process timeout/cancellation.

```powershell
$env:FOSSPDF_VERAPDF_JAR = '...\bin\cli-1.30.3.jar'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet build Source/FossPDF.sln -c Release --no-restore --warnaserror
dotnet test Source/FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
python Source/FossPDF.UnitTests/Parity/verify_pdfa.py Source/FossPDF.UnitTests/bin/Release/net9.0/parity-artifacts/ACC-402
dotnet test Source/FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PdfAExamples
```

## Qualified tool identity

- veraPDF **1.30.3**, official greenfield CLI, Java 8+ (local Java 8u291; CI Temurin 17).
- Release signature verified using key `13DD102B4DD69354D12DE5A83184863278B17FE7`.
- Archive SHA-256: `B512BF24945503A33FC10738ECFFC1F2DCCAAF8593A448E94EE9AC86A9559A59`.
- CLI SHA-256: `19F6EDD5C92841A38D9B3B018A555B7BA173B13E4E0D29EDECC232E5BA6C1D70`.
- Six XML profile SHA-256 values are pinned in `install_verapdf.py` and emitted in the uploaded
  qualification manifest. Runtime verifies the complete CLI hash before processing a document.
- qpdf 12.4.2 locally; CI uses installed qpdf 11+. pypdf 6.21.2 and PyMuPDF 1.26.7 independently
  inspect/render files using the existing development-only validation requirements.

## Local qualification results (2026-10-28)

- Release solution: zero warnings/errors.
- Full unit suites: .NET 8 **499 passed**, 13 framework/planned skips; .NET 9 **664 passed**, five
  planned skips. The final separately executable merged-pagination regression also passed.
- All **24** profile/optimization combinations, **four** merged A/U cases and **two** original/
  validated legacy cases pass the pinned veraPDF profiles. The original legacy flag fixture
  passes 2b; this does not establish conformance for arbitrary legacy documents.
- **675** independent checks pass, including strict qpdf parsing, linearization hints, embedded
  fonts/Unicode, ICC, tags, all associated-file payloads, original/continuous page numbering and
  rendering within 2/255. Ligature presentation forms are NFKC-normalized for text comparison.
- All **seven** deliberate corruptions fail validation. The API also rejects a missing glyph and
  an undeclared custom XMP property while retaining caller destinations.
- The PDF/A-3a associated-XML example passes. Existing Windows visual (four scenarios),
  performance and qpdf quality checks pass without changed budgets or tolerances. The generic
  optional quality-tool probe has no veraPDF executable on PATH; ACC-402 explicitly invokes the
  pinned JAR and is the conformance evidence.
- The CI workflow repeats the same checks on Windows/Linux/macOS Intel, uploading the original
  JSON reports, PDFs, expected failures and validator/profile manifest as `parity-evidence-*`.
  Merge is gated on all three main, quality and native-prototype jobs plus CodeQL. The feature
  PR's Checks tab links the exact CI runs and artifacts.
No PDF/UA, NativeAOT or arbitrary existing-PDF conversion claim is made by this work.
