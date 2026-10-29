# Validated e-invoice packaging (FP-404)

The optional .NET 9 `FossPDF.PdfOperations` package provides `GenerateEInvoice` for externally
supplied **UTF-8 CII EN16931 XML**, with exactly two qualified selections:

| API selection | Release and profile |
| --- | --- |
| `EInvoiceProfile.FacturX1092En16931` | Factur-X 1.09.2, EN 16931 |
| `EInvoiceProfile.Zugferd252En16931` | ZUGFeRD 2.5.2, EN 16931 |

These aligned releases use the same packaging and validation rules. The enum records the caller's
declared release; the shared XML guideline and standard XMP identify the EN16931 profile, without
distinguishing the two brand names or minor releases. Other profiles, UBL, credit-note workflows
and older/newer releases are outside this API's qualified contract. Unknown enum values fail.
See [FeRD's release information](https://www.ferd-net.de/aktuelles-veranstaltungen/aktuelles/news/zugferd-252-veroeffentlicht)
and the [pinned Mustang release history](https://github.com/ZUGFeRD/mustangproject/blob/core-2.26.0/History.md).

```csharp
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.PdfOperations;

// The application supplies both the visible invoice and the accounting-system XML.
var xml = File.ReadAllBytes("accounting-invoice.xml");
document.WithSettings(new DocumentSettings { PdfAConformance = PdfAConformance.PdfA3B });
var bytes = document.GenerateEInvoice(xml, EInvoiceProfile.FacturX1092En16931,
    out var report, new EInvoiceOptions
    {
        MustangJarPath = "/tools/Mustang-CLI-2.26.0.jar",
        VeraPdfJarPath = "/tools/verapdf/bin/cli-1.30.3.jar"
    });
File.WriteAllBytes("invoice.pdf", bytes);
File.WriteAllText("invoice-verapdf.json", report.ArchivalReport.Json);
File.WriteAllText("invoice-xml-report.xml", report.InvoiceXmlReport.Xml);
File.WriteAllText("invoice-package-report.xml", report.PackageReport.Xml);
```

The runnable `EInvoiceExamples.PackageExternallySuppliedInvoiceXml` uses an independently
hand-authored XML fixture containing two units at EUR 6.00, EUR 12.00 net, EUR 2.28 VAT and
EUR 14.28 payable. No XML invoice generator is used or shipped.

## Validation and responsibilities

The library copies XML at invocation and rejects empty/oversized input, non-UTF-8 encodings,
DTD/external entities, wrong roots and nonmatching guidelines. Input is limited to 16 MiB.
It runs the pinned Mustang XSD, Schematron and arithmetic checks before composition. It embeds
the original bytes as `factur-x.xml` with `text/xml`, `/AFRelationship /Alternative`, shared
name-tree/catalog `/AF` references and a modification date from `DocumentMetadata.ModifiedDate`.
It supplies all four invoice XMP properties and the PDF/A extension-schema declaration. The
standard XMP `Version` is `1.0`, and `ConformanceLevel` is `EN 16931`; these are not the minor
release number or the XML guideline URI.

Select PDF/A-3a, PDF/A-3b or PDF/A-3u explicitly. The existing writer retains document metadata,
semantics, image settings and content-position capture. qpdf additional compression,
linearization and uncompressed output run before validation. Combined PDF/UA-1 settings are
honored, with an accessibility report and the existing requirement for author accessibility
review. Additional `PdfAAttachment` files and declared custom XMP schemas are preserved;
reserved invoice attachment names/keys and invoice namespace properties cannot be overridden.

Every final PDF must pass strict structural packaging checks, its selected veraPDF archival
profile, optional PDF/UA machine profile, and Mustang PDF packaging/XML checks. Final structural
checks verify the exact payload, required relationship, filename, MIME/date, shared name-tree/AF
references and all four exact XMP properties after optimization. Both Mustang PDF and XML summaries are checked
separately, because an overall XML result alone cannot establish correct packaging.
`EInvoiceConformanceException.Report.Xml` retains Mustang diagnostics; archival/accessibility
failures retain their existing exception reports. A failed structural check throws
`PdfOperationException` identifying the failed requirement. Warnings and notices are retained; validator
errors withhold output. Positive EN16931 validation can include notices about additional
XRechnung requirements, which this API does not claim to meet.

The pinned Mustang validator accepts `/Unspecified` instead of the required `/Alternative`, and
accepts an allowed XMP profile such as BASIC alongside EN16931 XML. The final structural checks
reject both; independent negative fixtures retain Mustang's actual passing reports to document
these coverage gaps. See the [pinned validator implementation](https://github.com/ZUGFeRD/mustangproject/blob/core-2.26.0/validator/src/main/java/org/mustangproject/validator/PDFValidator.java).

Callers remain responsible for accurate parties, dates, taxation, amounts and agreement between
the visible PDF and XML. The library performs no cross-check of visible invoice text against XML,
provides no general accounting/tax certification and does not generate invoice XML. National
mandates, transmission, signatures and arbitrary existing-PDF conversion are outside this API.

Byte-array, file and writable borrowed-stream overloads stage all checks before returning/writing.
Streams need not seek, are written at their current position and remain open. Validation failures
leave the destination untouched; caller I/O failure can leave a partial write. Generation stages
complete PDFs and invoice data in memory. Later document edits can invalidate conformance.

## Deployment and reproducible evidence

Applications deploy **Java 17+**, Mustang **2.26.0** and veraPDF **1.30.3**. Both JARs are checked
against fixed SHA-256 hashes. Nothing is downloaded by the library. Set `FOSSPDF_MUSTANG_JAR`
and `FOSSPDF_VERAPDF_JAR`, or use the corresponding options. `JavaExecutablePath` is shared by
both validators; `ProcessTimeout` defaults to two minutes per invocation. Processes use argument
lists without a shell, a 512 MiB heap and private temporary directories, cleaned after success,
failure, timeout or cancellation. Mustang file logging is disabled.

Mustang JAR SHA-256: `42d7868cb68264874a7b8cab4c3587b03b23ccc7cd72373da917f66758bb9736`.
The explicit installer verifies the complete JAR and records hashes of the four EN16931 XSDs,
profile Schematron/code list and CEN Schematron in `qualification-manifest.json`. The bundled
`ZF_250` directory name is Mustang's resource identifier; the release history identifies its
2.5.2/1.09.2 rule updates. veraPDF deployment and hashes are in [PDF_ARCHIVAL.md](PDF_ARCHIVAL.md).

```powershell
python Source/FossPDF.UnitTests/Parity/install_mustang.py --output artifacts/parity/mustang
python Source/FossPDF.UnitTests/Parity/install_verapdf.py --output artifacts/parity/verapdf
# Set FOSSPDF_MUSTANG_JAR and FOSSPDF_VERAPDF_JAR to the printed paths; add Java 17+ and qpdf to PATH.
dotnet test Source/FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release -f net9.0 --filter 'FullyQualifiedName~EInvoiceTests|Name~Parity_ACC-404'
python -m pip install -r Source/FossPDF.UnitTests/Parity/requirements-tagging-validation.txt
python Source/FossPDF.UnitTests/Parity/verify_einvoice.py Source/FossPDF.UnitTests/bin/Release/net9.0/parity-artifacts/ACC-404
dotnet test Source/FossPDF.Examples/FossPDF.Examples.csproj -c Release --filter FullyQualifiedName~EInvoiceExamples
```

The acceptance runner produces twelve packages covering both declared releases, all three
PDF/A-3 levels and four optimization modes on 3b, plus one combined PDF/UA-1 example. Independent
pypdf and PyMuPDF checks compare exact extracted bytes, file references, metadata and rendered
pixels against ordinary generation. A bad filename, schema and totals fail Mustang; a wrong
relationship and mismatched allowed XMP profile fail the structural checks. CI is configured to repeat these checks on Windows/Linux/macOS
Intel and upload original reports and fixture PDFs. See [local evidence](evidence/fp-404-windows.md).

Mustang is an application-deployed Apache-2.0 tool, never bundled in FossPDF packages. Its
bundled validator components retain their own licenses, including veraPDF's GPLv3+/MPLv2+
choices; obtain the official distribution with its licenses and notices. Java is deployed
separately under the selected runtime's terms. No new NuGet or native dependency is added.
