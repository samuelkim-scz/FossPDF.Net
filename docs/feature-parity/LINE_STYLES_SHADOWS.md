# Advanced line styles and shadows

This guide covers [FP-205](https://github.com/samuelkim-scz/FossPDF.Net/issues/19): dash patterns
and phases, gradient line paints and box shadows. The APIs are additive. A solid, non-gradient
line keeps its historical rectangle drawing path and pixel output.

## Dashed lines

```csharp
page.Content()
    .LineHorizontal(2)
    .LineDashPattern(new[] { 6f, 3f });                 // 6-point dash, 3-point gap
```

`LineDashPattern` takes dash/gap pairs, converted from the requested unit. The array must have an
even, non-zero length; every value must be finite and non-negative, and at least one dash must be
positive, which keeps the draw loop finite. Zero-length dashes or gaps are allowed and are skipped
immediately. The line width stays the second argument of `LineHorizontal`/`LineVertical`.

A phase offset starts the pattern inside the first dash:

```csharp
.LineDashPattern(new[] { 12f, 6f }, phase: 5);
```

A positive phase starts five points into the pattern, so the first painted dash is seven points
long. Negative phases wrap modulo the pattern length, and so do phases larger than the pattern.
The phase is converted from the same `Unit` as the pattern.

Dashes are geometry, not a Skia path effect: the renderer emits one filled quad per dash along the
line. That is why they stay vector in PDF (`l` path operators) and SVG (`<path>`) and why the
dash lengths are exactly the requested point values. A canvas transform scales the pattern with
the drawing; a `2x` scale doubles dash and gap lengths and the phase, matching a stroked dash.

## Gradient lines

```csharp
.LineGradient("#ff0000", "#0000ff");                   // two stops
.LineGradient("#ff0000", "#00ff00", "#0000ff");        // evenly distributed stops
```

At least two validated colors are required. Stops are distributed evenly from the start of the
line to its end, along the line's direction, for both horizontal and vertical lines. When a
gradient is set, it replaces `LineColor` for the painted quads. Stops and alpha are interpolated
**without premultiplication**, exactly like the FP-204 container gradients: a transparent black
stop fades its RGB toward black. Use a transparent tint of the target color (`#00ff0000` to
`#ffff0000`) when only opacity should change.

A gradient line keeps its paint as vector output: a PDF shading (`/ShadingType`) and an SVG
`<linearGradient>`. The paint is applied to the dash quads too, so a line can be gradient and
dashed at the same time.

## Box shadows

```csharp
page.Content()
    .Background("#ffffff")
    .CornerRadius(8)
    .Shadow(new BoxShadowStyle
    {
        OffsetX = 3,
        OffsetY = 4,
        Spread = 2,
        Blur = 5,
        Color = "#80000000"
    });
```

`BoxShadowStyle` values are in points. `OffsetX`/`OffsetY` move the shadow and may be negative;
`Spread` expands (or contracts, when negative) the shadow shape; `Blur` must be non-negative, and
zero produces a sharp shadow. The color is any validated color string and defaults to black.
Invalid (non-finite) values throw `ArgumentOutOfRangeException` from the fluent call.

The shadow follows the container's corner radii: every radius grows by `Spread` and is clamped at
zero. It is painted **behind** the container's background, border and content, so a covering
background hides the shadow inside the box and it only shows where it is offset or spread out.
The shadow is a decoration property, not layout: it does not change measured size, pagination or
overlap decisions, and it shares the FP-204 decoration element with `Background`, `Border`,
`CornerRadius` and `Clip()`.

Blur uses a Gaussian mask with sigma = `Blur / 2`. The visible falloff therefore extends roughly
three sigma (1.5 x `Blur`) outside the sharp shape; make sure a parent clip does not cut it off.

Only one shadow is supported per container. Inner shadows and stacked shadows are not provided.

## Backend behavior

| Output | Dashes | Gradient lines | Sharp shadow | Blurred shadow |
| --- | --- | --- | --- | --- |
| PDF | Vector quads in the content stream | Vector shading (`/ShadingType`) | Vector path | Raster image sized by `DocumentMetadata.RasterDpi` |
| PNG/JPEG/WebP | Native Skia geometry | Native Skia shader | Native Skia path | Native Skia mask filter |
| SVG | `<path>` quads | `<linearGradient>` paint server | `<path>` | Embedded 72-DPI `<image>`; no `<filter>` is claimed |

The PDF and SVG fallbacks exist because those backends cannot express a blurred vector shadow.
The PDF image is rasterized at the document metadata's `RasterDpi` (default 72; configurable
through `DocumentSettings.ImageRasterDpi`), so raising the DPI increases the embedded shadow
resolution and file size. The SVG fallback is always 72 DPI. Temporary bitmaps are disposed after
each draw, and repeated generation of the same document produces identical output.

Sharp shadows, dashes and gradient lines never rasterize.

## Limitations

- The SVG blurred-shadow fallback is a raster image at 72 DPI; it does not scale losslessly when
  the SVG is zoomed.
- A blurred shadow's PDF image resolution follows the document raster DPI, not the page's
  display size; very large pages at low DPI show a soft, low-resolution shadow.
- Dashes have no configurable cap or join; each dash is a butt-ended quad.
- Shadow painting happens outside layout bounds, so an ancestor `Clip()` can crop it.
