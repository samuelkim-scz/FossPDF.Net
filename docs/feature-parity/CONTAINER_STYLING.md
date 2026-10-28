# Rounded containers, border alignment and gradients

This guide covers [FP-204](https://github.com/samuelkim-scz/FossPDF.Net/issues/18): rounded
containers, border alignment, gradients and rounded clipping. All of it is additive; existing
`Background(...)` and `Border(...)` calls keep their defaults and their historical draw order.

## One decoration per container

`Background(...)`, `Border(...)`, `CornerRadius(...)`, `BorderAlignment(...)`,
`BackgroundGradient(...)`, `BorderGradient(...)` and `Clip()` all edit the **same** decoration
element. Chaining order therefore does not matter: a background added after a border still fills
the border's rectangle, and a radius applies to fill, border and clip together.

```csharp
using FossPDF.Fluent;
using FossPDF.Infrastructure;

page.Content()
    .Background("#f1faee")
    .Border(4)
    .BorderColor("#1d3557")
    .BorderAlignment(BorderAlignment.Outside)
    .CornerRadius(12);
```

The first call installs the decoration; later calls retarget it. `Clip()` is the only call that can
create a decoration when none exists yet.

## Corner radius

```csharp
.CornerRadius(8)                                   // points
.CornerRadius(0.25f, Unit.Inch)                    // unit conversion
.CornerRadius(4, 8, 8, 4)                          // top-left, top-right, bottom-right, bottom-left
.CornerRadius(new BorderRadius(4, 8, 8, 4))        // explicit value object
.CornerRadiusTopLeft(4)
.CornerRadiusTopRight(8)
.CornerRadiusBottomRight(8)
.CornerRadiusBottomLeft(4)
```

`BorderRadius` validates that every radius is finite and non-negative. The radius only affects
painting; it never changes measured or arranged space, so layout and pagination are unaffected.

## Border alignment

`BorderAlignment` selects where the border ring sits relative to the layout bounds:

| Value | Ring extent |
| --- | --- |
| `Inside` | The border occupies the outer `width` points of the container. |
| `Center` | The border is centered on the edge (the legacy behavior, default). |
| `Outside` | The border extends `width` points past the container. |

Per-side widths are supported through `BorderTop/Right/Bottom/Left`. Corner radii follow the
alignment: the outer radius grows by the expansion and the inner radius shrinks, clamped at zero.
Borders are drawn as a single even-odd path when a radius, a gradient or a non-center alignment is
involved. Solid, square, center-aligned borders still emit the four historical rectangles.

`BorderAlignment` is a layout-painting property only: the container's measured size is unchanged.
An outside border can therefore paint into padding or a gap; give it room if it must not overlap
neighbors.

## Gradients

```csharp
.BackgroundGradient(LinearGradient.Horizontal("#ff0000", "#0000ff"))
.BackgroundGradient("#00ff00", "#ffff00", GradientDirection.Vertical)
.BorderGradient("#6a00ff", "#ff8800", GradientDirection.DiagonalTopLeftToBottomRight)
```

`GradientDirection` provides `Horizontal`, `Vertical`, `DiagonalTopLeftToBottomRight` and
`DiagonalTopRightToBottomLeft`. Both stops are validated colors. Gradients are painted in the
backend's sRGB space; stops and alpha are interpolated **without premultiplication**, so a
transparent black stop also fades its RGB toward black. Use a transparent tint of the target color
(`#00ff0000` to `#ffff0000`) when only opacity should change.

Gradients stay vector in PDF (`/ShadingType`) and SVG (`<linearGradient>`) output instead of being
rasterized.

## Clipping

```csharp
page.Content()
    .CornerRadius(16)
    .Clip()
    .Image(photoBytes);          // clipped to the rounded bounds
```

`Clip()` clips the **content below it** to the container bounds, rounded by the configured radii.
The container's own background and border are painted outside the clip, so a rounded card can have
an overflowing image and a fully visible border. The clip is emitted as a saved canvas state,
intersected with any outer clip, and restored afterward; nested and transformed content keeps the
clip in PDF, PNG and SVG output.

## Output coverage

| Output | Behavior |
| --- | --- |
| PDF | Borders are vector paths, gradients are shadings and clips use the content-stream clip operator. Nothing is rasterized. |
| PNG/JPEG/WebP | Rendered through the same Skia canvas pipeline as PDF, so geometry and colors match. |
| SVG | `<linearGradient>` / `<clipPath>` elements are preserved; transforms stay inside the clip. |

## Compatibility notes

- Solid square backgrounds use the original `DrawRectangle` call; rounded or gradient backgrounds
  use the new style operation.
- Solid, square, center-aligned borders keep the four-rectangle sequence. Any radius, gradient or
  non-center alignment switches to one even-odd ring path.
- `BorderColor` and `Background` still accept any validated color string, including transparent
  colors.
- Two-stop linear gradients only; multi-stop gradients, dashed borders and shadows belong to
  FP-205.
- The decoration paints after measuring, so changing styles during the render pass does not affect
  layout.

## Acceptance

`GFX-204` is active in the parity catalog. It asserts, on a 160x120 page at 288 DPI:

- inside/center/outside border ink extents within one pixel (0.25 point) of the requested box;
- a rounded corner cutout and the transparent hole inside an inside-aligned ring;
- horizontal gradient start, midpoint and end colors within eight sRGB levels, plus vector
  shading in PDF and SVG;
- nested and rotated clipped content: white outside the rounded corner, painted at the center, a
  PDF clip operator and an SVG `clipPath`.

The runner writes PDF, PNG and SVG artifacts under `parity-artifacts/GFX-204/`. See
[fp-204-windows.md](evidence/fp-204-windows.md) for the recorded local run.
