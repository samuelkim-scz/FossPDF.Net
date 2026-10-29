# Native backend dependency and ownership inventory

This is the versioned inventory for [ADR-0001](decisions/ADR-0001-native-backends.md) and
[FP-002](https://github.com/samuelkim-scz/FossPDF.Net/issues/3). The isolated .NET 9 proof project is
outside `FossPDF.sln`, has `IsPackable=false`, and changes no production package dependency graph.

## Locked cohort and licenses

`Source/FossPDF.NativePrototypes/packages.lock.json` pins the complete graph and package content
hashes. Direct references use exact ranges; restore runs in locked mode. `audit_dependencies.py`
compares restored versions with that lock and rejects unexpected license/native-cohort changes.

| Package | Version | License | Role |
| --- | --- | --- | --- |
| SkiaSharp | 3.119.4 | MIT | Core drawing |
| SkiaSharp.HarfBuzz | 3.119.4 | MIT | Core shaping bridge |
| SkiaSharp.NativeAssets.Win32 | 3.119.4 | MIT + bundled notices | Windows assets |
| SkiaSharp.NativeAssets.macOS | 3.119.4 | MIT + bundled notices | macOS assets |
| SkiaSharp.NativeAssets.Linux.NoDependencies | 3.119.4 | MIT + bundled notices | Linux assets |
| HarfBuzzSharp | 8.3.1.5 | MIT | Core shaping |
| HarfBuzzSharp.NativeAssets.Win32 | 8.3.1.5 | MIT + bundled notices | Windows assets |
| HarfBuzzSharp.NativeAssets.macOS | 8.3.1.5 | MIT + bundled notices | macOS assets |
| HarfBuzzSharp.NativeAssets.Linux | 8.3.1.5 | MIT + bundled notices | Linux assets |
| Svg.Skia | 4.9.1 | MIT | Optional SVG parser/drawing adapter |
| Svg.Animation | 4.9.1 | MIT | SVG transitive dependency |
| Svg.Custom | 4.9.1 | **MS-PL** | SVG parser transitive dependency |
| Svg.Model | 4.9.1 | MIT | SVG transitive dependency |
| Svg.SceneGraph | 4.9.1 | MIT | SVG transitive dependency |
| ShimSkiaSharp | 4.9.1 | MIT | SVG transitive dependency |
| ExCSS | 4.3.1 | MIT | CSS parser |
| PDFsharp | 6.2.4 | MIT | PDF import and low-level object/structure writer |
| Microsoft.Extensions.Logging.Abstractions | 8.0.3 | MIT | PDFsharp transitive dependency |
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.2 | MIT | Logging transitive dependency |
| System.Security.Cryptography.Pkcs | 8.0.1 | MIT | PDFsharp transitive dependency |
| IDisposableAnalyzers | 4.0.8 | MIT | Existing build-time analyzer |

The independently installed inspector is **pypdf 6.21.2**, BSD-3-Clause, Python >=3.9, pure Python;
it is validation tooling only, never a FossPDF runtime dependency. Its exact version is in
`requirements-validation.txt`; its distribution includes its BSD license. Bundled Lato remains
SIL OFL 1.1; see `Source/FossPDF/Resources/DefaultFont/OFL.txt`.

**qpdf** (measured with 12.4.2, Apache-2.0) is an optional *external executable* for the FP-306
compression and linearization controls, not a package dependency: it is not referenced by any
project, not restored by NuGet, and not distributed by FossPDF. Applications install qpdf 11.0 or
newer themselves and point FossPDF at it through `DocumentSettings.QpdfExecutablePath`,
`FossPDF.Settings.QpdfExecutablePath`, the `DocumentOperation.UseQpdfExecutable` overload or `PATH`.
Absence of qpdf is reported as `NotSupportedException` before any output is written. CI installs
qpdf from the distribution package manager (apt, Homebrew, Chocolatey) for coverage only. See
[PDF_OPTIMIZATION.md](PDF_OPTIMIZATION.md).

Svg.Skia 4.9.1 declares SkiaSharp 3.119.2/HarfBuzzSharp 8.3.1.3 minimums. The proof explicitly pins
the compatible core cohort 3.119.4/8.3.1.5, including all three HarfBuzz native asset packages.
The production SVG companion must use the reviewed cohort. Upgrading to current Svg.Skia versions
that require SkiaSharp 4 is a separate ABI/native-cohort decision.

The shipped `FossPDF.Svg` companion (FP-201) uses this exact cohort: `Svg.Skia` is pinned to
`[4.9.1]`, the project targets `net9.0` only, and the package ships `THIRD-PARTY-NOTICES.md` plus
the copied Svg.Skia (MIT) and Svg.Custom (MS-PL) licence texts under `licenses/`. Svg.Model,
Svg.SceneGraph, Svg.Animation, ShimSkiaSharp and ExCSS travel as MIT-licensed package dependencies
with their own package licence metadata intact.

The shipped `FossPDF.PdfOperations` companion (FP-302) pins `PDFsharp` to `[6.2.4]`, targets
`net9.0` only, and ships `THIRD-PARTY-NOTICES.md` plus the copied PDFsharp MIT licence text under
`licenses/`. Microsoft.Extensions.Logging.Abstractions,
Microsoft.Extensions.DependencyInjection.Abstractions and System.Security.Cryptography.Pkcs travel
as MIT-licensed package dependencies with their own package licence metadata intact.

## License and notice decisions

FossPDF's own code remains MIT. Select unchanged Svg.Skia binaries in an optional SVG companion
with their original licenses intact. **Svg.Custom is MS-PL, not MIT.** Its license preserves the
copyright/patent/trademark/attribution notices; redistribution of its source retains MS-PL and a
complete license copy. Binary distributions must comply with its terms. No vendor source is being
relicensed or incorporated into FossPDF source in this decision. See the
[full copied license](notices/Svg.Custom-4.9.1-LICENSE.txt) and
[steward/OSI text](https://opensource.org/license/ms-pl).

PDFsharp **6.2.4 is MIT** (copyright 2001-2026 empira Software GmbH). Published **6.5.0 declares
AGPL-3.0-only**; it is not an accepted upgrade. The package nuspec is the evidence, rather than a
project-level marketing/license page. The lock prevents a dependency from silently selecting it.

[Copied notices and provenance](notices/README.md) include SkiaSharp/HarfBuzzSharp licenses and
bundled third-party notices, Svg.Skia/Svg.Custom licenses and PDFsharp's pinned MIT license.
The native asset packages for all three operating systems contain identical versions of the
committed native notices; the audit checks each package's license/notice SHA-256 against those
copies. The JSON manifest also records package copyright metadata, every native asset's RID,
size/hash and notice hashes. Preserve all bundled notices, not only the top-level MIT license.

The first production companion, `FossPDF.Svg` (FP-201), follows this rule with the reviewed cohort
rather than a blanket FossPDF notice. Future companions must do the same for their entire graph
(including ExCSS, the SVG projects, Microsoft packages and Lato if redistributed). Use those exact
versions' upstream/package notices; do not replace them with a blanket FossPDF MIT notice. These
proof projects are not packed or shipped as FossPDF packages.

## Runtime assets and packaging

| Asset family | Available package assets | Qualification boundary |
| --- | --- | --- |
| Windows | `win-x64`, `win-x86`, `win-arm64` | Windows CI's actual RID is recorded in `environment.json` |
| macOS | `osx` native libraries (universal distribution) | macOS CI's actual architecture/RID is recorded |
| Linux | `linux-x64/x86/arm/arm64/loongarch64/riscv64`, musl and Android-bionic variants in the asset packages | Ubuntu CI qualifies only its actual desktop RID |

`dependency-assets.json` is the exact restored-file inventory; availability of a binary is not a
support claim. FP-503 owns the wider architecture/OS, NativeAOT, trimming and publish qualification.
Browser/iOS/Android/bionic, musl, Windows ARM/x86 and additional Linux architectures are not accepted
as qualified production targets by this proof. Missing native libraries or unsupported RIDs fail
explicitly, with no silent rasterization/output substitution. A consumer may deploy the optional
feature in a supported service, but there is no automatic remote fallback.

SVG and PDFsharp add managed assemblies, not another native PDF engine. The prototype adds Linux
Skia `NoDependencies`/HarfBuzz assets as the executable startup project, matching the unit-test setup.
Do not drop the existing core native binaries from deployment. PDFsharp never reshapes/repaints
FossPDF text in the selected path, so it needs no platform font resolver. Embedded FossPDF Lato
fonts and their ToUnicode mappings are preserved and independently checked after tagging/import.

A future application/companion package must document its startup native asset requirements and
verify publish output for its declared RIDs. `IsPackable=false` and absence from the solution keep
these experiments out of production packages.

## Ownership, cleanup and errors

- Caller streams are borrowed and remain open after backend objects are disposed, including on
  malformed input, missing/duplicate semantic associations and output-write failure.
- Proof adapters read from the current input position into owned seekable memory (32 MiB proof
  limit). They serialize into an owned buffer, then append to a writable caller output. Both
  borrowed input and output can be non-seekable. Production limits/positions belong to feature APIs.
- Staging prevents input/serialization errors from writing output; a failing caller sink may contain
  a partial write and remains the caller's responsibility. Backend/write errors propagate.
- `SKSvg` owns its picture. Disposing the source releases the native picture; it must outlive all
  replays. Bitmaps/canvases/images/encoded data and all temporary PDF documents/streams use deterministic
  disposal. Imported pages belong to their destination documents; they are not separate caller resources.
- Existing PDF import does not promise preservation of signatures, conformance or arbitrary features.
  Unsupported/encrypted/malformed inputs have explicit feature-owned policies; passwords are never logged.
- Tagging requires a semantic tree and unambiguous paint associations captured before flattening.
  `UAManager` rejects page import, so it is deliberately not the post-processing integration API.
- The runtime proof verifies disposal by checking released picture handles and caller stream state
  after success/failure. Independent inspection checks structural links and unchanged paint/font bytes.

## Upgrade and maintenance policy

1. Update exact package ranges and the lock deliberately; review all direct/transitive licenses,
   notices, security advisories, target frameworks, native assets and ABI compatibility.
2. Skia/HarfBuzz bindings and native assets move as a cohort. No unreviewed major/license change.
3. Run all three prototype/inspection/audit CI jobs, the parity corpus and FP-003 quality baselines.
   Keep environment and package/native/notice hashes as artifacts.
4. A fix unavailable in MIT PDFsharp 6.2.4 requires a reviewed maintained permissive fork or a newly
   qualified replacement. Neither an AGPL update nor ignoring a security issue is automatic policy.
5. Revisit ADR-0001 when a replacement, framework/RID addition or native bridge is selected. qpdf
   is qualified as an optional external executable by FP-306; see PDF_OPTIMIZATION.md and its evidence for deployment, licensing and error handling.
6. Conformance remains gated by independent validators and the owning acceptance issues; marker
   checks or PDFsharp's high-level helper are not PDF/UA or PDF/A evidence.

## FP-305 managed metadata serialization bridge

The net9.0 operations companion retains the exact PDFsharp 6.2.4 package and existing licenses. Its writer regenerates XMP during save, so `PdfXmpMetadata` appends one metadata revision to the companion's own serialized output. A .NET `UnsafeAccessor` bridge binds the pinned handler's `EnterObject`, `EncryptStream` and `LeaveObject` methods for protected XMP. This uses backend password derivation and ciphers; no cryptographic implementation or vendor source is copied into FossPDF. A backend upgrade must qualify those exact managed entry points and the incremental metadata corpus again.

The .NET corpus covers 40/128/256-bit encryption, deliberately clear metadata, extraction and later decrypted round trips on each CI OS. Independent pypdf/MuPDF inspection checks packaging and metadata interoperability. No NativeAOT claim follows from this managed bridge; publish-and-run evidence remains FP-503. The mechanism follows the pinned [save preparation](https://github.com/empira/PDFsharp/blob/v6.2.4/src/foundation/src/PDFsharp/src/PdfSharp/Pdf/PdfDocument.cs) and [security handler](https://github.com/empira/PDFsharp/blob/v6.2.4/src/foundation/src/PDFsharp/src/PdfSharp/Pdf.Security/PdfStandardSecurityHandler.cs) contracts.
The qpdf staging directory uses owner-only mode 0700 on Unix. The net9.0 core target uses the framework UnixFileMode API; netstandard2.1 calls the system libc mkdir function on Linux/macOS because that target lacks the managed overload. No libc binary is distributed. Both paths are exercised by the net8.0/net9.0 CI suites. Windows uses the user temporary directory's inherited ACL.

Tagged-PDF visual regression validation uses PyMuPDF 1.26.7 (AGPL/commercial) as an external development-only Python tool, pinned in requirements-tagging-validation.txt. It is never referenced, packed or distributed by the core or companion .NET packages. This version supplies Windows/Linux/macOS Intel and ARM wheels: https://pypi.org/project/pymupdf/1.26.7/ . Production tagging continues to use the exact MIT PDFsharp 6.2.4 pin.


## FP-402 external PDF/A validator

`GeneratePdfA` in the optional .NET 9 operations companion uses the existing Skia/PDFsharp writer
and requires application-deployed Java 8+ and the unmodified veraPDF 1.30.3 CLI JAR. These are
external executables, never NuGet dependencies or files distributed by FossPDF. veraPDF offers
[GPLv3+/MPLv2+ licensing](https://github.com/veraPDF/veraPDF-apps#licensing); deployments obtain it directly with its notices and choose an applicable
license. CI uses Temurin 17 via actions/setup-java and installs only the CLI pack.

The official 1.30.3 release archive's detached signature was verified against release key
`13DD102B4DD69354D12DE5A83184863278B17FE7`. The reviewed archive, CLI JAR and six bundled profile
hashes are pinned in `Source/FossPDF.UnitTests/Parity/install_verapdf.py`; runtime verifies the JAR
hash, so another validator build cannot silently change acceptance. Upgrade the runtime hash,
installer manifest and qualification corpus together after reviewing the official release and
licenses. No vendor code or profile XML is copied into the MIT library. See
[deployment and limitations](PDF_ARCHIVAL.md) and [qualification evidence](evidence/fp-402-windows.md).


FP-403 reuses the same unmodified veraPDF 1.30.3 CLI and external Java deployment for PDF/UA-1
machine checks. The installer additionally pins PDFUA-1.xml SHA-256
`794c9f6bf53c8cb89e532807e74fea58331a0d9a935c52cb054b63853bdd8b2a`; the full JAR hash already
covers that profile. No new engine, binary dependency or license cohort is introduced. The
machine qualification does not replace the human checkpoints required for accessibility; see
[PDF/UA responsibilities and status](PDF_UNIVERSAL_ACCESSIBILITY.md).

## Application-deployed invoice validator (FP-404)

GenerateEInvoice invokes the unmodified Mustang CLI 2.26.0 (Apache-2.0), deployed by the
application alongside Java 17+ and veraPDF 1.30.3. Mustang, its bundled dependencies, Java
and veraPDF are never redistributed in FossPDF packages. Retain the upstream distribution
licenses/notices, including the bundled veraPDF components' GPLv3+/MPLv2+ terms.
The complete Mustang JAR and EN16931 XSD/Schematron/code-list hashes are pinned in
`Source/FossPDF.UnitTests/Parity/install_mustang.py`; runtime checks the complete JAR hash.
Deployment, supported release/profile pairs and responsibilities are documented in
[the invoice guide](https://github.com/samuelkim-scz/FossPDF.Net/blob/main/docs/feature-parity/EINVOICE.md).
