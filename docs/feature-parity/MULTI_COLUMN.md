# Multi-column flow and balancing

[FP-104 / issue #9](https://github.com/samuelkim-scz/FossPDF.Net/issues/9) adds newspaper-style
multi-column layout to the QuestPDF 2026.9.1 parity target. The APIs are additive and available in
both core targets, `netstandard2.1` and `net9.0`. The .NET 8 tests consume the `netstandard2.1`
build; .NET 9 tests and examples consume `net9.0`.

## Configure the layout

~~~csharp
var document = Document.Create(container => container.Page(page =>
{
    page.Size(240, 180);
    page.Margin(10);
    page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(12));

    page.Content().MultiColumn(multiColumn =>
    {
        multiColumn.Columns(2);
        multiColumn.Spacing(12);
        multiColumn.BalanceHeight();

        multiColumn.Spacer().AlignCenter().LineVertical(1).LineColor("#808080");

        multiColumn.Content().Column(column =>
        {
            foreach (var paragraph in paragraphs)
                column.Item().Text(paragraph);
        });
    });
}));
~~~

| Member | Behavior |
| --- | --- |
| `Columns(int value = 2)` | Number of vertical columns. Rejects values below one. |
| `Spacing(float value, Unit unit = Unit.Point)` | Horizontal gap between columns. Rejects `NaN` and infinite values; negative values overlap columns. |
| `BalanceHeight(bool enable = true)` | Distributes content so that every used column has approximately the same height instead of filling the available height. Enabled by default when called. |
| `Content()` | The single content stream that flows through the columns. Calling it twice throws `DocumentComposeException`. |
| `Spacer()` | Content drawn in every gap where the flow continues. It receives a slot as wide as `Spacing` and as tall as the columns. Calling it twice throws `DocumentComposeException`. |

`MultiColumn` is a regular container element and composes with the rest of the layout: padding,
backgrounds, sizing constraints and page-level flow all apply normally.

## Flow semantics

- Content fills the first column from top to bottom, continues at the top of the next column, and
  finally reports a partial render so the document generator continues on the next page.
- Column width is `(available width - spacing x (columns - 1)) / columns`. Fractional widths are
  allowed and are not rounded.
- In right-to-left content the first column is the rightmost one; each column's content is aligned
  to its logical start edge.
- The spacer is drawn in a gap only when the preceding column reports a partial render, that is,
  only when content continues into the following column. A layout whose content ends in the first
  column draws no separator.
- Balanced mode reports the smallest height that still fits all content, so the element does not
  claim the full space offered by its parent. This also applies while drawing, which is why the
  fluent API wraps the element in a vertical shrink layer; without it, a parent that passes the full
  page height at draw time would re-flow the content at the full height and produce unbalanced
  columns.
- Unbalanced mode occupies the height it actually consumed. When the first column is partial, that
  is the available height, so content continues on the next page.

## Shrink helpers

Balanced multi-column uses the same shrinking helpers as QuestPDF: `Shrink()` renders its content
at its natural size along both axes, `ShrinkVertical()` only shrinks the height, and
`ShrinkHorizontal()` only the width. They are additive public API for both core targets. FP-107's
remaining scope (`MinimalBox` semantics, generic `ZIndex` and inline alignment) is unchanged.

## Speculative layout and state

Balancing needs to know how the content behaves at a candidate height, but drawing the content
would emit output and consume stateful content such as `ShowOnce`, dynamic components and tables.
The element therefore measures by drawing into a `FreeCanvas` (through `ProxyCanvas`) and rewinds
the content afterwards:

- Every stateful child implementing `IStateSnapshot` is wrapped in a drawing observer. On its first
  speculative draw the observer captures the child's state; afterwards the state is restored.
- Caching and debugging proxies implement `IStateSnapshot` by forwarding to their child. Observers
  are applied *after* proxies, so without forwarding the real element would never be captured and
  speculative draws would permanently consume it.
- Cache and debug proxies are transparent, so no additional configuration is required.

The result is observable: a `ShowOnce` element inside a balanced multi-column layout renders once,
across all speculative measurement passes and the real draw pass, and a table continued into the
next column does not repeat rows.

## Cost

Balanced mode performs up to eight additional measure-and-draw passes per page (the binary search
over `[0, available height]`), each speculatively drawing every column. Unbalanced mode measures
and draws each column once. The binary search resolves the balanced height to
`available height / 256`, roughly 0.6 points on an 160-point content area and 2.8 points on a full
720-point page.

Measured on Windows 11, win-x64, .NET 9.0.9, Release, 30 iterations after three warm-up runs
(median; see [evidence/fp-104-windows.md](evidence/fp-104-windows.md)):

| Scenario | Mode | PDF time | PDF allocations |
| --- | --- | --- | --- |
| Three short paragraphs, one page | unbalanced | 2.9 ms | 0.57 MB |
| Three short paragraphs, one page | balanced | 9.9 ms | 2.59 MB |
| Three long paragraphs, two pages | unbalanced | 4.3 ms | 0.99 MB |
| Three long paragraphs, two pages | balanced | 10.0 ms | 3.29 MB |

Raster export is dominated by text rendering and was 9.8 ms versus 13.4 ms for the one-page
scenario and 66.7 ms versus 64.9 ms for the two-page scenario. Peak working set stayed between
51 MB and 57 MB in every mode.

## Limits

- Content must be splittable between columns. A child that cannot render in a column, for example a
  fixed-height block taller than the column, causes `DocumentLayoutException`; the layout never
  clips silently.
- A single content element that is smaller than a column is not split within itself beyond what the
  element supports; multi-column flow composes with the element's own pagination, as any page-based
  content does.
- Balancing optimizes height only. It does not balance line counts or whitespace distribution.
- The fixture and tests cover two columns. The implementation is width-generic (`Columns(n)`), but
  the separator geometry and the documented evidence use two columns.
- Nested multi-column layouts inherit the same speculative state rules; they are not part of this
  work item's acceptance evidence.

## Verification

- `Source/FossPDF.UnitTests/MultiColumnTests.cs` - 19 tests covering argument validation, unbalanced
  and balanced flow, page continuation, determinism, right-to-left ordering, separators, `ShowOnce`,
  tables, dynamic components, text overflow, empty content and unbreakable children.
- Parity fixture `LAY-104` - colored block flow with exact ink counts, balanced 4+4 placement,
  right-to-left order, separator geometry, raster images, paragraph reading order compared against a
  single-column reference, empty content and oversized children; see [ACCEPTANCE.md](ACCEPTANCE.md)
  and [evidence/fp-104-windows.md](evidence/fp-104-windows.md).
- `Source/FossPDF.Examples/MultiColumnExamples.cs` - runnable balanced and unbalanced PDF/PNG
  examples.
