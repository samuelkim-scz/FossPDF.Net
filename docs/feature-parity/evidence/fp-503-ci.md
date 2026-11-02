# FP-503 native CI qualification

On November 1, 2026, [run 41359918379](https://github.com/samuelkim-scz/FossPDF.Net/actions/runs/41359918379)
published and executed all **60** packaged .NET 9 consumers: four feature graphs, three deployment
modes and five native targets. The qualified implementation head is
[`9ab8be8`](https://github.com/samuelkim-scz/FossPDF.Net/commit/9ab8be8), checked out by CI as PR merge
`ad39fbd789f44e059ef1b4ad51f5f60f5ce0b789`. Every profile passes independent inspection; every
NativeAOT executable reports dynamic code unavailable and the native process/OS architecture
matching its requested RID. Each job also checks RUN-503 against its real published outputs.

| Target | Runner | JIT | Full trimming | NativeAOT |
| --- | --- | --- | --- | --- |
| Windows x64 | `windows-latest` | 4 passed | 4 passed | 4 passed |
| Windows ARM64 | `windows-11-arm` | 4 passed | 4 passed | 4 passed |
| Linux x64 | `ubuntu-latest` | 4 passed | 4 passed | 4 passed |
| Linux ARM64 | `ubuntu-24.04-arm` | 4 passed | 4 passed | 4 passed |
| macOS x64 | `macos-26-intel` | 4 passed | 4 passed | 4 passed |

[The committed compact record](fp-503-ci.json) retains the SDK, framework, OS, source provenance,
all resolved dependency versions, local package hashes, native library sizes/hashes and per-profile
inspection counts/raster differences. The workflow artifacts `runtime-qualification-<rid>` retain
the full publish/run logs, toolchain inventory, generated PDFs/PNGs, runtime reports, independent
inspection reports and RUN-503 TRX. Use the linked run's Artifacts panel, or:

```sh
gh run download 41359918379 --repo samuelkim-scz/FossPDF.Net --dir artifacts/fp-503-ci
```

Publish treats compiler/linker/AOT warnings as errors. Package source mapping and isolated restore
caches ensure these are consumers of the locally packed distribution. The four graphs exercise
core text/images/layout diagnostics, explicit embedded-font registration and native subsetting,
vector SVG/text, and PDF page selection/merge, AES-256, XMP, attachments and decryption. Independent
readers are pypdf 6.21.2 and PyMuPDF 1.26.7. Raster checks compare with JIT on the same host under
the declared 0.002 changed-pixel fraction and 2/255 channel tolerance.

## Reproduced gaps and fixes

The [initial run](https://github.com/samuelkim-scz/FossPDF.Net/actions/runs/41359246022) passed
55 of 60 profiles. All five AOT font profiles failed at `Assembly.GetCallingAssembly()`, before
subsetting. The runtime does not support that discovery in NativeAOT. Both font managers now
offer an explicit resource-assembly overload; the fixture uses `typeof(Program).Assembly` and
asserts that the built-in subset callback actually executes. The one-argument legacy API remains
available for managed applications and explains the explicit alternative when discovery is
unavailable. Known-type HarfBuzz constructor reflection subsequently passes native execution.

Local full trimming had already reproduced IL2075 in layout diagnostic reflection. Targeted
public-property preservation fixes it, and the published impossible-layout case verifies the
element identity and constraints remain visible. No broad assembly roots or linker suppressions
were introduced. The AES-256 fixture also exercises the pinned PDFsharp `UnsafeAccessor` bridge
and independently verifies the encryption revision/strength, XMP and attachment bytes.

These results cover the recorded hosts and [declared fixture scope](../RUNTIME_SUPPORT.md).
macOS ARM64, other OS distributions/versions, musl, x86 and mobile/browser deployments remain
unqualified. Interactive previewer, conformance/invoice validators and other unexercised APIs
are not implied NativeAOT capabilities. Reproduce on another native host before extending claims.
