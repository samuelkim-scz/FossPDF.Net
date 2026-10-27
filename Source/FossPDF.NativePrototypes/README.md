# Native backend prototypes (FP-002)

Isolated .NET 9 mechanism proofs for [FP-002](https://github.com/samuelkim-scz/FossPDF.Net/issues/3).
This project is outside `FossPDF.sln`, is not packed, and adds no production dependencies.

From `Source`:

```powershell
dotnet restore FossPDF.NativePrototypes/FossPDF.NativePrototypes.csproj --locked-mode
dotnet build FossPDF.NativePrototypes/FossPDF.NativePrototypes.csproj -c Release --no-restore --warnaserror
dotnet run --project FossPDF.NativePrototypes/FossPDF.NativePrototypes.csproj -c Release --no-build --no-restore -- --output ../artifacts/fp-002
python -m pip install -r FossPDF.NativePrototypes/requirements-validation.txt
python FossPDF.NativePrototypes/verify_artifacts.py ../artifacts/fp-002
python FossPDF.NativePrototypes/audit_dependencies.py FossPDF.NativePrototypes/obj/project.assets.json ../artifacts/fp-002
```

Use an isolated Python virtual environment if preferred. All commands fail nonzero when their checks
fail. CI runs the same commands on Windows/Linux/macOS and uploads the entire artifact directory.

| Proof | Observable checks |
| --- | --- |
| SVG | Pixel-checked raster fallback and direct vector drawing through FossPDF.Canvas; independent path/image-XObject inspection |
| PDF operations | Import/reorder actual Skia PDF pages, preserve media boxes, borrowed non-seekable stream support |
| Tagging | Tag actual FossPDF pages through the low-level object model; role/page/MCID/parent-tree associations, Unicode, unchanged paint/font bytes |
| Semantic paint batches | Compose two FossPDF/Skia fragments as vector forms on one page and tag their invocations with distinct MCIDs; explicit sidecar associations |
| Ownership/errors | Native picture release, borrowed stream survival after backend disposal/failures, malformed/external SVG and missing/duplicate associations rejected |
| Independent inspector | Pinned pypdf 6.21.2 checks serialized objects, marked content, text extraction and retained font/ToUnicode streams |
| Dependency audit | Locked versions/licenses, redistributable native assets and notice hashes match reviewed files |

Artifacts include source/vector/raster/tagged PDFs, SVG/PNG, semantic sidecars,
`prototype-results.json`, `environment.json`, `independent-inspection.json` and `dependency-assets.json`.

The tag writer consumes **known** paint boundaries. It cannot automatically infer semantics from an
arbitrary flattened PDF. Production semantic capture, full SVG/font behavior and PDF operation APIs
remain in FP-201/302-306/401-403. These proofs make **no PDF/UA or PDF/A conformance claim**.

Svg.Skia and most dependencies are MIT; **Svg.Custom 4.9.1 is MS-PL**. PDFsharp is pinned to MIT 6.2.4;
6.5.0 declares AGPL-3.0-only and is not accepted. See [ADR-0001](../../docs/feature-parity/decisions/ADR-0001-native-backends.md),
[dependency inventory](../../docs/feature-parity/NATIVE_DEPENDENCIES.md),
[notices](../../docs/feature-parity/notices/README.md) and [evidence](../../docs/feature-parity/evidence/fp-002-windows.md).
