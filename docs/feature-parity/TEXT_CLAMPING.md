# Text line clamping

[FP-102 / issue #7](https://github.com/samuelkim-scz/FossPDF.Net/issues/7) adds a paragraph-level
line limit and a custom ellipsis. The API is available in both core targets, `netstandard2.1` and
`net9.0`.

```csharp
page.Content().Text(text =>
{
    text.ClampLines(2, "[more]");
    text.Span("A long paragraph that would otherwise continue for several lines.").FontSize(12);
});
```

`ClampLines(int maxLines, string ellipsis = "…")` applies to the paragraph. The default ellipsis is
`…` (U+2026); pass an empty string to truncate without one. A null ellipsis selects the default.

## Behavior

- The paragraph renders at most `maxLines` lines. If the content fits within the limit, no ellipsis
  is appended.
- The ellipsis is appended to the last visible line and the line is re-laid out with space reserved
  for the ellipsis, so the result stays within the paragraph width for ordinary widths.
- The ellipsis uses the text style of the last visible span, preserving color, weight, size and
  decoration of the truncated text.
- The line budget is counted across pages. If a multi-page paragraph reaches the limit on a later
  page, only that page's final visible line receives the ellipsis.
- Rich-text spans, hyperlinks and section links keep their styling. Any content after the clamp is
  not rendered.
- When font subsetting is enabled, the ellipsis glyphs for the used styles are included in the
  subset.
- `maxLines` must be greater than zero; otherwise `ClampLines` throws
  `ArgumentOutOfRangeException` during composition. Empty text remains an empty paragraph.
- The line clamp is part of the `TextBlock` state and resets for every layout/render pass, so
  repeated generation is deterministic.

## Width and style rules

The ellipsis is measured with the style of the last visible span. The final line is re-measured with
that width reserved. If the available width is too small for both text and ellipsis, the ellipsis is
rendered on its own line when possible; otherwise it may be clipped by the available width. The
clamp never silently raises the paragraph above the configured line count.

Complex grapheme-cluster and script shaping limits remain with
[FP-109 / issue #14](https://github.com/samuelkim-scz/FossPDF.Net/issues/14). Line clamping uses the
same wrapping boundaries as normal text layout; it does not introduce a separate Unicode
segmentation path.

## Acceptance and example

`TXT-102` is the executable acceptance fixture:
- an 80-point paragraph at Lato 12 is limited to two lines;
- the last line contains `[more]` and does not exceed 80 points;
- a short paragraph has no ellipsis;
- a non-positive limit is rejected.

See [Source/FossPDF.Examples/TextClampExamples.cs](../../Source/FossPDF.Examples/TextClampExamples.cs)
for a runnable example and [evidence/fp-102-windows.md](evidence/fp-102-windows.md) for the recorded
validation.
