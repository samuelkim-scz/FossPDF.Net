# FossPDF.Previewer third-party notices

This file supplements `THIRD-PARTY-NOTICES.md` for the `FossPDF.Previewer` .NET tool package. The
shared file contains the MIT licence text and the common notices; this file records the complete
set of components bundled into the previewer tool and points to the exact upstream licence/notice
files shipped under `licenses/`.

## Bundled managed components

| Component | Version | Licence | Copyright / attribution |
| --- | --- | --- | --- |
| Avalonia runtime suite (Avalonia, Base, Controls, DesignerSupport, Desktop, Dialogs, Fonts.Inter, FreeDesktop, Markup, Markup.Xaml, Metal, MicroCom, Native, OpenGL, Remote.Protocol, Skia, Themes.Fluent, Vulkan, Win32, Win32.Automation, X11) | 11.3.10 | MIT | Copyright 2013-2025 © The AvaloniaUI Project |
| Avalonia.ReactiveUI | 11.3.8 | MIT | Copyright 2013-2025 © The AvaloniaUI Project |
| ReactiveUI | 20.1.1 | MIT | Copyright (c) .NET Foundation and Contributors |
| Splat | 15.1.1 | MIT | Copyright (c) .NET Foundation and Contributors |
| DynamicData | 8.4.1 | MIT | Copyright (c) Roland Pheasant 2011-2024 |
| System.Reactive | 6.0.1 | MIT | Copyright (c) .NET Foundation and Contributors |
| Tmds.DBus.Protocol | 0.21.3 | MIT | Copyright Tom Deseyn |
| MicroCom.Runtime | 0.11.0 | MIT | Copyright 2021 © Nikita Tsukanov |
| SkiaSharp (managed wrapper) | 3.119.4 | MIT | Copyright (c) 2015-2016 Xamarin, Inc.; Copyright (c) 2017-2018 Microsoft Corporation |
| HarfBuzzSharp (managed wrapper) | 8.3.1.1 | MIT | Copyright (c) 2015-2016 Xamarin, Inc.; Copyright (c) 2017-2018 Microsoft Corporation |
| Inter font family (embedded in Avalonia.Fonts.Inter) | — | SIL Open Font License 1.1 | Copyright (c) 2016 The Inter Project Authors (https://github.com/rsms/inter) |

## Bundled native components

| Component | Runtime identifiers | Licence | Attribution | Full text |
| --- | --- | --- | --- | --- |
| `libSkiaSharp` (SkiaSharp.NativeAssets.Win32/macOS/Linux) | win-x86/x64/arm64, osx, linux-x86/x64/arm/arm64 and musl/bionic variants | SkiaSharp wrapper MIT; Skia native BSD-3-Clause and third-party terms | Xamarin/Microsoft; Skia copyright and third-party notices | `licenses/SkiaSharp-3.119.4-LICENSE.txt`, `licenses/SkiaSharp-HarfBuzzSharp-THIRD-PARTY-NOTICES.txt` |
| `libHarfBuzzSharp` (HarfBuzzSharp.NativeAssets.Win32/macOS/Linux) | win-x86/x64/arm64, osx, linux-x86/x64/arm/arm64 and musl variants | HarfBuzzSharp wrapper MIT; HarfBuzz native MIT and third-party terms | Xamarin/Microsoft; HarfBuzz copyright and third-party notices | `licenses/HarfBuzzSharp-8.3.1.1-LICENSE.txt`, `licenses/SkiaSharp-HarfBuzzSharp-THIRD-PARTY-NOTICES.txt` |
| `libAvaloniaNative.dylib` (Avalonia.Native) | osx | MIT | Copyright 2013-2025 © The AvaloniaUI Project | `THIRD-PARTY-NOTICES.md` (MIT text) |
| `av_libglesv2.dll` (Avalonia.Angle.Windows.Natives) | win-x86/x64/arm64 | BSD-3-Clause (ANGLE) | Copyright 2018 The ANGLE Project Authors | `licenses/Avalonia.Angle.Windows.Natives-2.1.25547.20250602-LICENSE.txt` |

The tool archive also contains the Avalonia native components distributed inside the Avalonia
runtime packages and the `av_libglesv2.dll` ANGLE binaries listed above. The 36 native files are
covered by the four native groups in this table.

## Build-time and non-bundled references

The project graph also references Avalonia.BuildServices 11.3.2 (MIT, AvaloniaUI) as a build
dependency, and WebAssembly native asset packages for SkiaSharp/HarfBuzzSharp that are not copied
into the desktop tool archive. They are recorded here so the dependency inventory is complete;
they are not redistributed in this package.

## Full licence texts

- `licenses/SkiaSharp-3.119.4-LICENSE.txt`
- `licenses/HarfBuzzSharp-8.3.1.1-LICENSE.txt`
- `licenses/SkiaSharp-HarfBuzzSharp-THIRD-PARTY-NOTICES.txt`
- `licenses/Avalonia.Angle.Windows.Natives-2.1.25547.20250602-LICENSE.txt`
- `licenses/Inter-OFL.txt`
- `THIRD-PARTY-NOTICES.md` for the MIT text and the shared core/font notices

The notices are provided for attribution and licence compliance. They do not imply endorsement by
the named projects or copyright holders.
