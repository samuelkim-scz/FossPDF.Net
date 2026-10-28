# Layout helpers: shrinking, z-index and inline alignment

[FP-107](https://github.com/samuelkim-scz/FossPDF.Net/issues/12) fills three layout API gaps: independent-axis shrinking, numeric stacking depth, and typography-aware alignment for content injected into a text line. The APIs are available in both core targets (netstandard2.1 and net9.0).

## Shrinking one or both axes

~~~csharp
page.Content().ShrinkHorizontal().Background("#ff0000").Width(20).Height(10);
page.Content().ShrinkVertical().Background("#00ff00").Width(20).Height(10);
page.Content().Shrink().Background("#0000ff").Width(20).Height(10);
~~~

The wrapper measures its child and then draws it using the smaller target size:

| Call | Target width | Target height | Existing equivalent |
| --- | --- | --- | --- |
| ShrinkHorizontal() | Child width | Available height | None |
| ShrinkVertical() | Available width | Child height | None |
| Shrink() | Child width | Child height | MinimalBox() and obsolete Box() |
| MinimalBox() | Child width | Child height | Supported legacy name for Shrink() |

Shrinking only one axis retains the allocated size on the other axis. Right-to-left content aligns a horizontally shrunk child to the right edge. Shrink is a normal layout element: nested shrinking, page breaks and content direction continue to work.

## Stacking with ZIndex

~~~csharp
page.Content().Layers(layers =>
{
    layers.PrimaryLayer().Background("#ff0000");
    layers.Layer().ZIndex(2).Width(30).Height(30).Background("#0000ff");
    layers.Layer().ZIndex(-1).Width(40).Height(40).Background("#00ff00");
});
~~~

ZIndex(int indexValue) wraps any element. During the final drawing pass, content at each depth is recorded into its own page layer. Layers are then composited in ascending numeric order, so larger values render above smaller values. Equal depths retain composition order: later content stays above earlier content. Negative depths are valid.

The table-cell ZIndex property is unchanged and remains the way to order table cells. The fluent ZIndex can be used around table content as part of the wider page layering.

Layer composition is implemented by the shared Skia canvas path, so it applies to PDF, XPS, raster image export, SVG export and previewer pictures. Layout passes do not record pixels. The renderer pre-scans the composed element tree for ZIndex elements; the wrapper must therefore be present before rendering starts. For content created later by a dynamic/lazy component, place the ZIndex wrapper outside that component or use the natural Layers order instead.

## Aligning content inside text

~~~csharp
page.Content().Text(text =>
{
    text.Span("Status: ");
    text.Element(TextInjectedElementAlignment.Middle)
        .Width(12).Height(12)
        .Background("#2196f3");
    text.Span(" ready");
});
~~~

TextDescriptor.Element now accepts an optional TextInjectedElementAlignment. The handler overload is also available:

~~~csharp
text.Element(container => container.Width(12).Height(12).Background("#4caf50"),
    TextInjectedElementAlignment.BelowBaseline);
~~~

| Alignment | Vertical placement relative to the surrounding text |
| --- | --- |
| AboveBaseline (default) | Element bottom edge on the baseline; element occupies space above the baseline. |
| BelowBaseline | Element top edge on the baseline; element hangs below the baseline. |
| Top | Element top edge at the font top. Tall elements extend downward. |
| Bottom | Element bottom edge at the font top. Tall elements extend upward. |
| Middle | Element center at the vertical center of the font ascent/descent. |

The line uses the font ascent and descent from the surrounding spans as its typographic reference. Injected elements do not change span order or glyph shaping. Instead, the line ascent/descent grows when needed so the element and all spans remain inside the reported line box. An element with zero height does not contribute vertical size. Elements must measure as a full render within one line; content that wraps or spans pages is not supported inside TextDescriptor.Element.

Font top/bottom here means the font ascent/descent supplied by HarfBuzz/Skia, not the ink bounds of a particular glyph. A capital letter therefore usually starts a little below the Top reference.

## Validation

From Source:

~~~powershell
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~LayoutHelperTests
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~LayoutHelperExamples
~~~

[LayoutHelperExamples.cs](../../Source/FossPDF.Examples/LayoutHelperExamples.cs) renders each helper and checks decoded pixels. LayoutHelperTests covers per-axis shrinking (including right-to-left placement), equal and different z-index depths, PDF/SVG/preview composition, deterministic alignment math and rendered top/middle/bottom/baseline positions.

LAY-107 is active in the [acceptance catalog](acceptance-fixtures.json): a 100x80 area with a 20x10 child verifies 20x80, 100x10 and 20x10 targets; overlapping markers verify depths 2/0/1; 8x8 markers verify top/middle/bottom/baseline placement and logical order. See [the Windows evidence report](evidence/fp-107-windows.md).
