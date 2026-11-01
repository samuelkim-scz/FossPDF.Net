# Runtime deployment qualification (FP-503)

`FossPDF.RuntimeSmoke` publishes and executes .NET 9 consumers of the public NuGet packages.
Each feature is built separately in self-contained JIT, fully trimmed JIT and NativeAOT modes.
A successful build alone does not qualify a target: the published executable must run on its
native OS/architecture, produce the fixtures and pass independent PDF/raster inspection.

## Current evidence

| Runtime identifier | Self-contained JIT | Full trimming | NativeAOT | Evidence |
| --- | --- | --- | --- | --- |
| `win-x64` | Passed, four profiles | Passed, four profiles | CI qualification pending | [Local Windows record](evidence/fp-503-windows.md) |
| `linux-x64` | CI qualification pending | CI qualification pending | CI qualification pending | `runtime-qualification-linux-x64` CI artifact |
| `osx-x64` | CI qualification pending | CI qualification pending | CI qualification pending | `runtime-qualification-osx-x64` CI artifact |
| `win-arm64` | CI qualification pending | CI qualification pending | CI qualification pending | Native `windows-11-arm` runner |
| `linux-arm64` | CI qualification pending | CI qualification pending | CI qualification pending | Native `ubuntu-24.04-arm` runner |
| `osx-arm64` | Validation required | Validation required | Validation required | Runner accepts this RID; no execution evidence recorded |

These are fixture-scoped results, not a guarantee that every feature or application survives
trimming/AOT. .NET 8/netstandard2.1 compatibility remains covered by the existing build/test suite;
this work makes no new .NET 8 trimming claim. Available native binaries for x86, musl, additional
Linux architectures and mobile/browser targets do not establish support for those deployments.

The dedicated [workflow](../../.github/workflows/runtime-qualification.yml) retains failures as
well as successes. Until actual execution succeeds, a pending matrix cell is not a support claim.
Windows ARM64 qualification checks the consumer's process **and OS** architecture; an x64 Python
validation tool on that host does not substitute for native ARM64 consumer execution.

## Exercised behavior

Every profile renders styled Lato text (`TXT-001`), a quadrant raster image (`IMG-001`) and a
deliberately impossible layout. The last case checks that reflected diagnostic properties survive.
The optional profiles add:

| Profile | Additional behavior |
| --- | --- |
| `Fonts` | Register a consumer-embedded font and execute the built-in HarfBuzz subset callback |
| `Svg` | Parse/replay vector rectangles and SVG text using the optional SVG companion (`IMG-201`) |
| `Pdf` | Load, merge/select pages, preserve page boxes and caller streams, attach XML, extend XMP, encrypt with AES-256 and decrypt (`PDF-302`/`PDF-305` paths) |

The independent readers check extracted text/order, Unicode font mappings, metadata, page boxes,
attachment bytes, encrypted-password interoperability, vector SVG and declared pixel colors.
Trimmed/AOT rasters are compared with the same feature's JIT output on the same host: dimensions
must match and at most 0.002 of pixels may differ beyond 2/255 per channel. Different platforms
are not compared with each other. This small corpus does not replace complex-script qualification.

Previewer/Avalonia, interactive hot reload, arbitrary third-party components, PDF/A, PDF/UA,
e-invoicing, external qpdf/Java validators and XPS are outside this deployment corpus. Their normal
managed tests remain applicable; their NativeAOT deployment requires additional evidence.

## Reproduce

Install the .NET 9 SDK, Python 3.12 and the OS native compiler prerequisites documented by
[Microsoft](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/): Visual Studio C++
build tools on Windows, clang/zlib development files on Linux, or Xcode command-line tools on macOS.
Run on the native target rather than treating cross-publishing as execution evidence.

From the repository root, create an isolated validation environment and local package feed:

```powershell
python -m venv artifacts/runtime-validation-env
artifacts/runtime-validation-env/Scripts/python -m pip install -r Source/FossPDF.RuntimeSmoke/requirements-validation.txt
Push-Location Source
foreach ($project in @('FossPDF', 'FossPDF.FontSubset', 'FossPDF.FontSubset.Glue', 'FossPDF.Svg', 'FossPDF.PdfOperations')) {
    dotnet pack "$project/$project.csproj" -c Release -p:GeneratePackageOnBuild=false --output ../artifacts/runtime-packages
}
Pop-Location
artifacts/runtime-validation-env/Scripts/python Source/FossPDF.RuntimeSmoke/qualify_runtime.py --rid win-x64 --package-source artifacts/runtime-packages --output artifacts/runtime-qualification
```

On Linux/macOS use `python3`, the environment's `bin/python`, a matching RID and the equivalent
shell loop shown in the workflow. The driver runs dotnet from `Source` to honor `global.json`.
Use a fresh output directory each time. `--features Core` and `--modes jit trimmed` narrow a local
run; a narrowed run does not satisfy RUN-503, which requires all twelve profiles.
`--verify-existing` reruns independent inspection without republishing.

`--package-source` creates a cleared source list, maps `FossPDF*` exclusively to that local feed
and restores into a new cache. It rejects project-reference substitution and records package
hashes and resolved versions. Omitting it is useful for source debugging but does not qualify
the packaged distribution. Use `--package-version` when changing the repository's package version.

The startup application explicitly references the pinned Linux Skia NoDependencies and HarfBuzz
native asset packages. Deploy the appropriate native libraries and all applicable license/notices;
NativeAOT compiles managed code but does not remove these shared-library requirements.

## Audit and retained evidence

The .NET 9 core preserves public configuration properties on internal layout elements for
diagnostic reflection, following the [linker's type annotation contract](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-warnings/il2075).
It does not root an entire assembly or suppress linker warnings. The netstandard2.1 target is
unchanged. The subsetter's reflection uses known HarfBuzz `Face`/`Blob` constructors; the font
profile executes those calls. Consumer/core embedded font resources, Skia/HarfBuzz P/Invoke,
SVG dependencies and the pinned PDFsharp `UnsafeAccessor` metadata bridge are exercised by their
respective profiles. No blanket `IsAotCompatible` promise is added to the packages.

Every publish treats compiler, linker and AOT warnings as errors. `qualification.json` records
the SDK, source commit/worktree state, RID, package hashes, commands, timings, exit codes and
per-profile results. `publish.log` and `run.log` preserve tool failures. Each executed profile
records runtime/OS/architecture/dynamic-code capability, fixture outcomes, native-file size/SHA-256,
PDF/PNG outputs, layout diagnostics and `independent-inspection.json`. CI uploads these with its
toolchain inventory and RUN-503 test result. Source-generated JSON avoids reflection serialization.

The validation-only Python tools are pinned to pypdf 6.21.2 and PyMuPDF 1.26.7. PyMuPDF is
AGPL/commercial and runs as an external development tool; it is not referenced or redistributed
by any FossPDF NuGet package. Production dependencies and notices remain as documented in
[NATIVE_DEPENDENCIES.md](NATIVE_DEPENDENCIES.md).
