# FP-404 Windows qualification

The optional .NET 9 companion provides `GenerateEInvoice` for externally supplied UTF-8 CII XML.
The explicitly supported selections are **Factur-X 1.09.2 EN16931** and **ZUGFeRD 2.5.2 EN16931**.
See the [contract, deployment and limits](../EINVOICE.md).

## Local results

- Release solution build passes with zero warnings/errors, with Avalonia build telemetry disabled.
- Full .NET 8 suite: **499 passed**, 13 framework/planned skips. Full .NET 9 suite:
  **688 passed**, 3 planned skips. The final focused invoice suite additionally checks
  relative Java paths and correct UTF-8 diagnostic decoding on Windows.
- ACC-404 emits **twelve** packages: both declared releases with PDF/A-3a/b/u, plus compressed,
  linearized and uncompressed PDF/A-3b variants. A **thirteenth** package combines PDF/UA-1,
  PDF/A-3a, an additional CSV associated file and caller XMP. All finished outputs pass their
  exact veraPDF profile(s) and both Mustang PDF/XML checks.
- Pinned independent pypdf 6.21.2 and PyMuPDF 1.26.7 inspection passes
  **301 checks**, including byte-exact extraction through both readers,
  names, MIME/date, shared name-tree/AF references, XMP and extension schema, linearization,
  visible invoice text and unchanged pixels within 2/255 of ordinary generation.
- **Five deliberate failures** are identified: pinned Mustang rejects a wrong XMP filename,
  invalid currency/schema and incorrect totals. Strict runtime and independent structural checks
  reject the wrong associated-file relationship and a mismatched allowed XMP profile; Mustang
  2.26.0 accepts those two cases, and its actual reports are retained as coverage-gap evidence.
  The .NET suite also
  checks unsafe/malformed/non-UTF-8 XML, unsupported profiles/archival levels, reserved names/XMP,
  missing or modified tools, cancellation, caller byte mutation, settings/metadata preservation,
  borrowed nonseeking streams and untouched file/stream destinations on failure.
- The FP-003 quality gate passes all four Windows visual scenarios and all performance budgets.
  Its optional validator checks are separate from the mandatory invoice/PDF/A validators above.
- Native prototype inspection passes 28 checks. The dependency/notice audit verifies 20 locked
  packages and six native packages; no new NuGet or native dependency is introduced.
- `EInvoiceExamples.PackageExternallySuppliedInvoiceXml` passes and retains the PDF and original
  validator reports. The XML fixture is independently hand-authored, with documented example data.

## Tool pins and reproducibility

Runtime requires application-deployed Java 17+, Mustang 2.26.0 and veraPDF 1.30.3. Mustang JAR
SHA-256 is `42d7868cb68264874a7b8cab4c3587b03b23ccc7cd72373da917f66758bb9736`.
`install_mustang.py` records the four EN16931 XSD hashes, profile Schematron/code list and CEN
Schematron hashes. The veraPDF JAR/profile pins remain those recorded for FP-402/FP-403.
Tool installation is explicit; the library never downloads or bundles validators.

Reproduction commands are in the [invoice guide](../EINVOICE.md). Local logs and artifacts are
under `Source/artifacts/fp404-*` and
`Source/FossPDF.UnitTests/bin/Release/net9.0/parity-artifacts/ACC-404/`. CI is configured to install
the pinned tools, execute acceptance and independent checks, and upload original evidence on
Windows/Linux/macOS Intel. This file records local Windows qualification; the pull request CI
checks provide the current cross-platform results and downloadable artifacts.

The XML and standard XMP identify a common EN16931 profile; they cannot distinguish the aligned
brand names or minor releases. Applications still own invoice correctness, visible/XML agreement,
national requirements and any accessibility author review. This qualification establishes the
declared packaging workflow, not general invoice business correctness or transmission compliance.
