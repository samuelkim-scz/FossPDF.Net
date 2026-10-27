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

Svg.Skia 4.9.1 declares SkiaSharp 3.119.2/HarfBuzzSharp 8.3.1.3 minimums. The proof explicitly pins
the compatible core cohort 3.119.4/8.3.1.5, including all three HarfBuzz native asset packages.
The production SVG companion must use the reviewed cohort. Upgrading to current Svg.Skia versions
that require SkiaSharp 4 is a separate ABI/native-cohort decision.

The shipped `FossPDF.Svg` companion (FP-201) uses this exact cohort: `Svg.Skia` is pinned to
`[4.9.1]`, the project targets `net9.0` only, and the package ships `THIRD-PARTY-NOTICES.md` plus
the copied Svg.Skia (MIT) and Svg.Custom (MS-PL) licence texts under `licenses/`. Svg.Model,
Svg.SceneGraph, Svg.Animation, ShimSkiaSharp and ExCSS travel as MIT-licensed package dependencies
with their own package licence metadata intact.

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
   remains an FP-306 candidate requiring a separate deployment/license/error-handling proof.
6. Conformance remains gated by independent validators and the owning acceptance issues; marker
   checks or PDFsharp's high-level helper are not PDF/UA or PDF/A evidence.
