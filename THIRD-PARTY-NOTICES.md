# Third-party notices

FossPDF is a fork of [QuestPDF](https://github.com/QuestPDF/QuestPDF) at the last commit released
under an open-source licence. FossPDF itself is MIT-licensed; see [LICENSE.md](LICENSE.md).

This file records the third-party components used by the FossPDF packages and examples. It is
included in the `FossPDF`, `FossPDF.Previewer`, `FossPDF.FontSubset`, `FossPDF.FontSubset.Glue`,
`FossPDF.Svg` and `FossPDF.PdfOperations` NuGet packages.

## Distributed with the FossPDF package

| Component | Licence | Copyright / attribution |
| --- | --- | --- |
| QuestPDF (original project) | MIT | Copyright (c) 2021 QuestPDF |
| SkiaSharp 3.119.4 | MIT | Copyright (c) 2015-2016 Xamarin, Inc.; Copyright (c) 2017-2018 Microsoft Corporation |
| SkiaSharp.HarfBuzz 3.119.4 | MIT | Copyright (c) 2015-2016 Xamarin, Inc.; Copyright (c) 2017-2018 Microsoft Corporation |
| HarfBuzzSharp 8.3.1.5 (transitive) | MIT | Copyright (c) 2015-2016 Xamarin, Inc.; Copyright (c) 2017-2018 Microsoft Corporation |
| Lato font family (embedded) | SIL Open Font License 1.1 | Copyright (c) 2010-2014 by tyPoland Lukasz Dziedzic (team@latofonts.com) with Reserved Font Name "Lato" |

The full SIL Open Font License for the embedded Lato fonts is distributed with the package as
`OFL.txt` and is also available at
[Source/FossPDF/Resources/DefaultFont/OFL.txt](Source/FossPDF/Resources/DefaultFont/OFL.txt).

## Distributed with the FossPDF.Previewer package

The previewer is a self-contained .NET tool and therefore bundles its full runtime graph. The
table below covers every managed assembly and native group in the tool archive; exact upstream
licence/notice files, including the full Inter OFL and ANGLE/BSD texts, are shipped in the package
under `licenses/` and summarized in `PREVIEWER-THIRD-PARTY-NOTICES.md`.

| Component | Licence | Copyright / attribution |
| --- | --- | --- |
| Avalonia 11.3.10 runtime suite (Avalonia, Base, Controls, DesignerSupport, Desktop, Dialogs, Fonts.Inter, FreeDesktop, Markup, Markup.Xaml, Metal, MicroCom, Native, OpenGL, Remote.Protocol, Skia, Themes.Fluent, Vulkan, Win32, Win32.Automation, X11) | MIT | Copyright 2013-2025 © The AvaloniaUI Project |
| Avalonia.ReactiveUI 11.3.8 | MIT | Copyright 2013-2025 © The AvaloniaUI Project |
| ReactiveUI 20.1.1 | MIT | Copyright (c) .NET Foundation and Contributors |
| Splat 15.1.1 | MIT | Copyright (c) .NET Foundation and Contributors |
| DynamicData 8.4.1 | MIT | Copyright (c) Roland Pheasant 2011-2024 |
| System.Reactive 6.0.1 | MIT | Copyright (c) .NET Foundation and Contributors |
| Tmds.DBus.Protocol 0.21.3 | MIT | Copyright Tom Deseyn |
| MicroCom.Runtime 0.11.0 | MIT | Copyright 2021 © Nikita Tsukanov |
| SkiaSharp 3.119.4 (managed wrapper + Win32/macOS/Linux native assets) | MIT (wrapper); Skia native BSD-3-Clause and third-party terms | Copyright (c) 2015-2016 Xamarin, Inc.; Copyright (c) 2017-2018 Microsoft Corporation; Skia and third-party notices in `licenses/SkiaSharp-HarfBuzzSharp-THIRD-PARTY-NOTICES.txt` |
| HarfBuzzSharp 8.3.1.1 (managed wrapper + Win32/macOS/Linux native assets) | MIT (wrapper); HarfBuzz native MIT and third-party terms | Copyright (c) 2015-2016 Xamarin, Inc.; Copyright (c) 2017-2018 Microsoft Corporation; HarfBuzz and third-party notices in `licenses/SkiaSharp-HarfBuzzSharp-THIRD-PARTY-NOTICES.txt` |
| Avalonia.Angle.Windows.Natives 2.1.25547.20250602 | BSD-3-Clause (ANGLE) | Copyright 2018 The ANGLE Project Authors; full text in `licenses/Avalonia.Angle.Windows.Natives-2.1.25547.20250602-LICENSE.txt` |
| Inter font family (embedded in Avalonia.Fonts.Inter 11.3.10) | SIL Open Font License 1.1 | Copyright (c) 2016 The Inter Project Authors (https://github.com/rsms/inter); full text in `licenses/Inter-OFL.txt` |

Build-time only and not bundled in the tool archive: Avalonia.BuildServices 11.3.2 (MIT, The
AvaloniaUI Project). WebAssembly native asset packages for SkiaSharp/HarfBuzzSharp are also not
included in the desktop tool archive. The previewer-specific notice file and the files under
`licenses/` are shipped only with the `FossPDF.Previewer` package.

## Distributed with the FossPDF.Svg package

The optional SVG companion package targets `net9.0` and ships the reviewed Svg.Skia cohort. Its
full graph is recorded in [docs/feature-parity/NATIVE_DEPENDENCIES.md](docs/feature-parity/NATIVE_DEPENDENCIES.md);
the table below lists the components that require attribution in binary distributions.

| Component | Licence | Copyright / attribution |
| --- | --- | --- |
| Svg.Skia 4.9.1 | MIT | Copyright (c) 2020 Wiesław Šoltés; full text in `licenses/Svg.Skia-4.9.1-LICENSE.txt` |
| Svg.Custom 4.9.1 (transitive) | **MS-PL** | Copyright (c) Wiesław Šoltés and Svg.Skia contributors; full text in `licenses/Svg.Custom-4.9.1-LICENSE.txt` |

Svg.Custom is **Microsoft Public License (MS-PL)**, not MIT. Its complete licence text is packed
with `FossPDF.Svg` because binary distributions must retain the applicable notices. No Svg.Custom
source is copied into or re-licensed by FossPDF. Svg.Model, Svg.SceneGraph, Svg.Animation,
ShimSkiaSharp and ExCSS are MIT-licensed transitive dependencies of the same graph.

## Distributed with the FossPDF.PdfOperations package

The optional existing-PDF operations companion package targets `net9.0` and pins PDFsharp `6.2.4`
exactly. Its full graph is recorded in
[docs/feature-parity/NATIVE_DEPENDENCIES.md](docs/feature-parity/NATIVE_DEPENDENCIES.md).

| Component | Licence | Copyright / attribution |
| --- | --- | --- |
| PDFsharp 6.2.4 | MIT | Copyright (c) 2001-2026 empira Software GmbH; full text in `licenses/PDFsharp-6.2.4-LICENSE.txt` |
| Microsoft.Extensions.Logging.Abstractions 8.0.3 (transitive) | MIT | Copyright (c) .NET Foundation and Contributors |
| Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2 (transitive) | MIT | Copyright (c) .NET Foundation and Contributors |
| System.Security.Cryptography.Pkcs 8.0.1 (transitive) | MIT | Copyright (c) .NET Foundation and Contributors |

PDFsharp **6.5.0 declares AGPL-3.0-only** and is not shipped; the exact `[6.2.4]` range prevents an
unreviewed upgrade.

## Distributed with the FossPDF font-subsetting packages

| Component | Licence | Copyright / attribution |
| --- | --- | --- |
| HarfBuzzSharp 8.3.1.5 | MIT | Copyright (c) 2015-2016 Xamarin, Inc.; Copyright (c) 2017-2018 Microsoft Corporation |
| SkiaSharp.HarfBuzz 3.119.4 | MIT | Copyright (c) 2015-2016 Xamarin, Inc.; Copyright (c) 2017-2018 Microsoft Corporation |
| HarfBuzzSharp.NativeAssets.Linux 8.3.1.5 | MIT | Copyright (c) 2015-2016 Xamarin, Inc.; Copyright (c) 2017-2018 Microsoft Corporation |

## Build, test and example dependencies

These components are not redistributed in the FossPDF packages:

| Component | Licence | Copyright / attribution |
| --- | --- | --- |
| NUnit 3.13.x | MIT | Copyright (c) 2022 Charlie Poole, Rob Prouse |
| FluentAssertions 6.12.0 | Apache-2.0 | Copyright Dennis Doomen 2010-2023 |
| BenchmarkDotNet 0.13.9 / 0.15.8 | MIT | .NET Foundation and contributors |
| Avalonia.BuildServices 11.3.2 | MIT | Copyright 2023-2025 © The AvaloniaUI Project |
| Microcharts 0.9.5.9 | MIT | Copyright 2020 |
| Microsoft.NET.Test.Sdk | MIT | Copyright (c) .NET Foundation and contributors |
| NUnit3TestAdapter | MIT | Copyright (c) 2022 Charlie Poole, Rob Prouse |

The Apache-2.0 licence text for FluentAssertions is available at
<https://www.apache.org/licenses/LICENSE-2.0>.

## MIT licence text

The MIT-licensed components above are provided under the following terms:

```text
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

Each component's copyright notice is listed in the tables above and is retained in this file as
required by the licence.
