# Third-party notices

FossPDF is a fork of [QuestPDF](https://github.com/QuestPDF/QuestPDF) at the last commit released
under an open-source licence. FossPDF itself is MIT-licensed; see [LICENSE.md](LICENSE.md).

This file records the third-party components used by the FossPDF packages and examples. It is
included in the `FossPDF`, `FossPDF.Previewer`, `FossPDF.FontSubset` and `FossPDF.FontSubset.Glue`
NuGet packages.

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

| Component | Licence | Copyright / attribution |
| --- | --- | --- |
| Avalonia 11.3.10 (Desktop, Fonts.Inter, Skia, Themes.Fluent) | MIT | Copyright 2013-2025 © The AvaloniaUI Project |
| Avalonia.ReactiveUI 11.3.8 | MIT | Copyright 2013-2025 © The AvaloniaUI Project |
| Tmds.DBus.Protocol 0.21.3 | MIT | Copyright Tom Deseyn |
| SkiaSharp 3.119.4 and platform native assets | MIT | Copyright (c) 2015-2016 Xamarin, Inc.; Copyright (c) 2017-2018 Microsoft Corporation |

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
| Svg.Skia 1.0.0.3 | MIT | Copyright © Wiesław Šoltés 2023 |
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
