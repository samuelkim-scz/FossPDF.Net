# Paragraph layout: justification, logical alignment, indentation and word spacing

[FP-101 / issue #6](https://github.com/samuelkim-scz/FossPDF.Net/issues/6) adds paragraph-level
layout controls to the QuestPDF 2026.9.1 parity target. The APIs are additive and available in both
core targets, `netstandard2.1` and `net9.0`. The .NET 8 tests consume the `netstandard2.1` build;
.NET 9 tests and examples consume `net9.0`.

## Configure a paragraph

~~~csharp
var document = Document.Create(container => container.Page(page =>
{
    page.Size(300, 200);
    page.Margin(20);
    page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(11));

    page.Content().Text(text =>
    {
        text.Justify();
        text.ParagraphFirstLineIndentation(16);
        text.Span("A justified paragraph whose first line is indented by 16 points.");
    });
}));
~~~

`TextDescriptor` exposes:

| Member | Behavior |
| --- | --- |
| `AlignStart()` | Aligns to the logical start edge: left in left-to-right content, right in right-to-left content. |
| `AlignEnd()` | Aligns to the logical end edge: right in left-to-right content, left in right-to-left content. |
| `AlignLeft()`, `AlignCenter()`, `AlignRight()` | Physical alignment, unchanged in meaning. |
| `Justify()` | Expands the gaps between words on every line except the final line of each paragraph. Glyphs are not stretched. |
| `ParagraphFirstLineIndentation(float value, Unit unit = Unit.Point)` | Indents the first line of every paragraph from the logical start edge. Rejects negative, `NaN` and infinite values. |

The public enum `FossPDF.Infrastructure.TextHorizontalAlignment` (`Left`, `Center`, `Right`,
`Justify`, `Start`, `End`) names the supported values; fluent methods are the supported way to set
them. Existing `HorizontalAlignment` values remain unchanged and are mapped to the corresponding
physical text alignment when an aligned container is converted to text.

## Paragraph semantics

- A paragraph is one `TextBlock`. Each `text.Line(...)` or embedded newline starts a new paragraph,
  so each receives the configured first-line indentation and paragraph spacing.
- Only the first line is indented. Lines created by wrapping, and a paragraph that continues on the
  next page, keep the paragraph origin.
- Indentation reduces the line box at the logical start edge. For right-to-left content, the first
  line ends earlier at the right edge instead of starting further right at the left edge.
- Justification resolves to the logical start when a line has no spaces or is the final line of the
  paragraph. It fills the line box exactly, including when the first line is indented.

## Word spacing

~~~csharp
page.Content().Text(text =>
{
    text.Justify();
    text.Span("one two three").WordSpacing(1.5f); // 1.5 x font size per gap
});
~~~

`WordSpacing(float)` is available on `TextStyle` and on every `TextSpanDescriptor`. The value is a
factor of the font size, matching `LetterSpacing`; `1` adds one font-size worth of space to each
space glyph. It participates in measurement, line breaking, drawing, link rectangles, decorations and
backgrounds. Inheritance, fallback styles and the library default (`0`) follow the normal style
resolution path.

## Behavior changes in this work item

- Alignment is now resolved once per line. Previously, each span on a centered or right-aligned line
  was offset as if it were alone, so multi-span lines could overlap or overflow the content area.
  A right-aligned multi-span footer, for example, previously extended beyond the page margin.
- Text is no longer shifted by the first glyph's left side bearing. Alignment uses the line box
  origin; glyph ink may therefore sit within roughly one point of the declared edge.
- Right-to-left drawing now normalizes glyph positions from the logical glyph sequence. Previously
  the normalization sliced the visual glyph array with logical indices, which could push a
  continuation line outside its content area.
- Leading spaces are trimmed using the shaped glyph's space flag rather than comparing a UTF-16
  space code to a glyph id.

## Limits

- Justification expands word gaps only; it does not stretch, kern or letter-space glyphs.
- There is no automatic hyphenation, and CJK inter-character justification is out of scope here
  (tracked by FP-109).
- The final line of a paragraph is never justified, including when it happens to fill the width.

## Verification

- `Source/FossPDF.UnitTests/TextParagraphLayoutTests.cs` - deterministic stub spans and exact
  translations for indentation, page continuation, justification, multi-span alignment, LTR/RTL
  start/end, decoration width and argument validation.
- `Source/FossPDF.UnitTests/TextWordSpacingTests.cs` - real Lato measurement, wrapping, RTL,
  inheritance and fallback.
- Parity fixture `TXT-101` - five rendered scenarios with exact line geometry and raster ink
  comparison; see [ACCEPTANCE.md](ACCEPTANCE.md) and
  [evidence/fp-101-windows.md](evidence/fp-101-windows.md).
- `Source/FossPDF.Examples/ParagraphLayoutExamples.cs` - runnable PDF/PNG example.
