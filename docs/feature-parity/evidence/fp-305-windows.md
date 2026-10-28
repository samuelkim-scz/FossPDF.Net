# FP-305 Windows evidence

Recorded October 28, 2026 for [issue #24](https://github.com/samuelkim-scz/FossPDF.Net/issues/24). The dedicated branch builds on the FP-303 and FP-304 operations work. Environment: Windows x64, .NET SDK 9.0.305, .NET 8 and .NET 9 runtimes; existing PDFsharp 6.2.4 and native cohort unchanged.

| Check | Result |
| --- | --- |
| Release solution build, `--warnaserror` | Zero warnings/errors |
| Full .NET 8 suite | 463 passed, 13 skipped, zero failed |
| Full .NET 9 suite | 588 passed, 8 skipped, zero failed |
| Attachment feature tests plus active PDF-305 | 22 passed on .NET 9 |
| Runnable attachment example | 1 passed |
| Independent pypdf inspection | 56 checks passed; 2 explicit identity-filter reading limitations |
| Independent inspection with optional MuPDF path | 71 checks passed, including both identity-filter cases |
| Four visual scenarios and performance budgets | Passed |
| qpdf / veraPDF | Unavailable locally; explicitly skipped |

`PDF-305` uses the declared PAG-001 source and exact UTF-8 payload, display name, MIME type and XMP namespace/value from the acceptance catalog. Five output files represent Data, Source, Alternative, Supplement and Unspecified relationships. Independent inspection confirms exact extracted bytes and MIME/name values, a common file-specification reference from the name tree and catalog AF array, the declared XMP property, unchanged page boxes and decoded painting. MuPDF also renders those pages identically to the source.

The feature suite covers Unicode filenames/descriptions, fixed timestamps, input/configuration capture, file naming defaults, distinct keys sharing a display name, duplicate-key failure without output writes, replacement and preservation of other attachments, borrowed-stream ownership, empty attachments, invalid options, malformed name trees, malformed XML, duplicate qualified properties and existing namespace preservation. Repeated saves remain usable.

The encrypted corpus covers 40-bit RC4, 128-bit RC4 and 256-bit AES, including metadata deliberately left clear for the latter two. It verifies plaintext absence when protected, exact XMP reading, attachment extraction and later decrypted round trips. The metadata writer appends one revision to its own staged PDFsharp output and delegates protected stream encryption to three pinned managed entry points using .NET 9 UnsafeAccessor. No reflection, runtime code generation, new cipher implementation or vendor source is added.

All five encrypted variants are read correctly by PDFsharp and MuPDF. pypdf 6.21.2 independently reads the three protected-XMP cases and extracts attachments from every variant. Its explicit identity-filter metadata decoding is limited, so the two clear-XMP cases are listed as limitations rather than pypdf passes; the optional MuPDF checks cover those cases. Independent reports record versions, hashes and generic assertions without passwords.

The example emits `report-with-data.pdf` under the examples output's `pdf-operations-attachments/` directory. Unit/parity artifacts include relationship PDFs, encrypted variants, exact expected bytes, source PDFs and JSON inspection reports beneath `parity-artifacts/PDF-305/`. Existing CI artifact upload patterns include these files. The Python validator runs in all three OS build jobs; platform results are the final merge gate.

Local logs are under `Source/artifacts/fp305-final/`. Reproduction commands and implementation boundaries are in [ATTACHMENTS_XMP.md](../ATTACHMENTS_XMP.md). These checks establish packaging and supported interoperability, not PDF/A, PDF/UA, invoice-profile or NativeAOT conformance.

### Strict compression and encryption interoperability review

Attachments now use the shared .NET ZLibStream writer introduced by FP-303, avoiding the pinned backend's omitted Adler-32 trailer. CI independently validates complete zlib streams and checksums. The final net9.0 suite passes 588 tests with 8 planned skips, and the Release build has zero warnings/errors. The independent attachment/XMP inspector passes 91 checks with MuPDF enabled. qpdf 12.4.2 --check passes all 21 PDFs across PDF-303/304/305 with exit code 0; decrypting each of the five protected/Identity-filter PDF-305 variants with qpdf and then parsing it with pypdf confirms the exact extended XMP value, covering the two documented direct-pypdf Identity-filter limitations.
