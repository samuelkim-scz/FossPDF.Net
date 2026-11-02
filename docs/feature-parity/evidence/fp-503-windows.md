# FP-503 local Windows evidence

Recorded November 1, 2026 on Windows 11 x64 (build 26300), SDK 9.0.305.
This is evidence from the FP-503 working tree based on merge `318f5af`; the manifest truthfully
records `dirty_worktree: true`. It does not assert NativeAOT or cross-platform success.

All four feature profiles (`Core`, `Fonts`, `Svg`, `Pdf`) passed self-contained JIT and full
trimming from **locally packed NuGet packages**, with compiler/linker warnings treated as errors.
The runner restored FossPDF 2026.5.1 and its optional companions exclusively from the local feed
into an isolated package cache. A separate source-reference run also passed those eight profiles.

The pinned pypdf 6.21.2 / PyMuPDF 1.26.7 inspections passed extracted text/order, ToUnicode maps,
metadata, raster colors, vector SVG, imported page order/boxes, XML attachment bytes, AES-256
reader authentication, encrypted XMP and decrypted metadata. Every trimmed raster met the
same-host JIT tolerance. Native DLL sizes and SHA-256 are recorded per executable.

Full trimming initially produced IL2075 in layout diagnostic reflection. Preserving public
configuration properties on `Element` and keeping that type at the reflection helper boundary
removed the warning without broad assembly roots. The impossible-layout consumer verifies the
element name and `10000` size constraints remain in the published diagnostic trace.

A local NativeAOT publish was attempted. The installed Visual Studio instances lack the C++
linker workload, and the SDK stopped with **Platform linker not found**. This is a local
toolchain gap, not evidence of NativeAOT compatibility or an application runtime failure. The
dedicated native-runner workflow must supply the missing execution evidence.

The subsequent [native CI qualification](fp-503-ci.md) supplies that evidence on all five
configured RIDs, including Windows x64/ARM64. Its initial run found an AOT caller-assembly
discovery failure; the explicit resource-assembly overload fixes the supported font workflow.
Both core targets build cleanly after the fix, with ten font/catalog tests passing per framework
and the evidence-dependent RUN-503 check explicitly skipped locally. Newly packed Windows font
consumers also pass JIT/full trimming with the explicit assembly and subset-callback assertion.

Local ignored artifacts:

- `artifacts/fp-503/windows-packaged-qualification/qualification.json`
- `artifacts/fp-503/windows-packaged-qualification/{core,fonts,svg,pdf}/{jit,trimmed}/`
- `artifacts/fp-503/windows-trim-qualification/` (source-reference results)
- `artifacts/fp-503/core-aot-publish.log` (missing-linker diagnostic)

The [runtime guide](../RUNTIME_SUPPORT.md) gives reproducible commands and the exact fixture scope.
CI evidence will be linked separately after native execution succeeds.
