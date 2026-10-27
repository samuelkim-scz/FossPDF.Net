# FossPDF.Svg

Optional SVG input support for [FossPDF](https://www.nuget.org/packages/FossPDF).

The package parses self-contained SVG documents once into a reusable picture and replays them
through the FossPDF layout and drawing pipeline. Vector output is the default, so SVG paths stay
vector geometry in PDF output. The package targets `net9.0` and is not referenced by the core
package.

## Usage

```csharp
using FossPDF.Fluent;
using FossPDF.Svg;

// Parse once, draw on any number of pages.
using var logo = SvgImage.FromFile("logo.svg");

Document.Create(container =>
{
    container.Page(page =>
    {
        page.Size(300, 200);
        page.Margin(20);
        page.Content().Width(120).Svg(logo);
    });
});
```

`SvgImage` owns the parsed picture; dispose it when it is no longer needed. The convenience
overloads below parse a new image per call and leave it to the garbage collector, like the raster
image overloads.

The fluent overloads also accept text, bytes and borrowed streams:

```csharp
container.Svg("<svg xmlns=\"http://www.w3.org/2000/svg\" ...>...</svg>");
container.Svg(svgBytes);
container.Svg(stream); // read from the current position, left open
```

All overloads accept `ImageScaling` (`FitWidth`, `FitHeight`, `FitArea`, `Resize`). The default is
`FitWidth`, matching the image APIs. SVG user units are treated as points, so an SVG with
`width="80" height="40"` occupies 80 by 40 points at its natural size.

## Input policy

SVG input is treated as untrusted:

- the document must be well-formed XML with an `<svg>` root in the SVG namespace;
- the root must declare a positive `viewBox`, or absolute `width` and `height` attributes, so the
  intrinsic size is defined (percentage-only sizes are rejected);
- DTDs, XML stylesheets, `<script>` elements, `@import` rules and `javascript:` URLs are rejected;
- `href`/`src` values and CSS `url(...)` may reference only fragments (`#id`) or `data:` URIs;
  external file and network references are disabled;
- the maximum input size is 32 MiB.

Rejected input raises `SvgParseException`. The parser (Svg.Skia) is a rendering backend, not a
full SVG sanitizer; feed it only content you are willing to render.

## Fonts

SVG `<text>` is resolved through the fonts registered with FossPDF, so fonts loaded with
`FontManager.RegisterFont` (or registered under a custom name) render without being installed on
the system. The default FossPDF font (Lato) is used for text without a `font-family` or with the
`sans-serif`/`system-ui` generic families. Installed system fonts are still available through the
renderer's normal font lookup. Fonts that cannot be resolved fall back according to the renderer's
font matching and are not reported as errors.

## Raster fallback

Vector output is the default and is never silently replaced with a bitmap. To render an SVG that
uses effects the vector path cannot preserve, opt in explicitly:

```csharp
container.SvgRasterized(logo, new SvgRasterizationOptions
{
    Dpi = 300,
    Background = "#FFFFFFFF"
});
```

## Notices

This package distributes Svg.Skia 4.9.1 (MIT) and Svg.Custom 4.9.1 (MS-PL). Their full license
texts are shipped in the `licenses/` folder of the package. See `THIRD-PARTY-NOTICES.md` in the
package root for the complete dependency list.
