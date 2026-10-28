# Pinned third-party notices

These are unchanged upstream license/notice files for the FP-002 cohort. They document the decision
and are the exact copies packed with the `FossPDF.Svg` companion package under `licenses/` when the
component is distributed; the prototype itself is still not a shipped package.

| Files | Provenance |
| --- | --- |
| `SkiaSharp-3.119.4-LICENSE.txt` | `SkiaSharp.NativeAssets.Win32` 3.119.4 NuGet package root |
| `HarfBuzzSharp-8.3.1.5-LICENSE.txt` | `HarfBuzzSharp.NativeAssets.Win32` 8.3.1.5 NuGet package root |
| `SkiaSharp-HarfBuzzSharp-THIRD-PARTY-NOTICES.txt` | Identical `THIRD-PARTY-NOTICES.txt` in all six native asset packages at the pinned versions |
| `PDFsharp-6.2.4-LICENSE.txt` | https://github.com/empira/PDFsharp/blob/v6.2.4/LICENSE (MIT; empira 2001-2026) |
| `Svg.Skia-4.9.1-LICENSE.txt` | Svg.Skia commit `8b23ee1e01b1f2ba90eb74b71ab8c7f27d49f037`, root `LICENSE.TXT` |
| `Svg.Custom-4.9.1-LICENSE.txt` | Same Svg.Skia commit, `src/Svg.Custom/LICENSE.TXT` (MS-PL) |

Svg.Skia-family NuGet packages identify copyright Wieslaw Soltes 2026; Svg.Custom keeps MS-PL.
Microsoft dependencies identify Microsoft Corporation; ExCSS retains its upstream MIT attribution.
`audit_dependencies.py` records the exact nuspec copyright/license metadata alongside pinned versions.
The six native packages carry matching copies of these native license/notice files; each is checked
against the committed SHA-256 during CI. Every restored native binary's hash/RID is also recorded.

For binary/source redistribution, preserve the relevant packages' complete copyright, license,
attribution and third-party notices. In particular, MS-PL source is not relicensed as FossPDF MIT.
Use the exact upstream version's notices when building a companion package; consult
[NATIVE_DEPENDENCIES.md](../NATIVE_DEPENDENCIES.md) for the complete graph and upgrade policy.
