# Deferred and cached composition

[FP-106](https://github.com/samuelkim-scz/FossPDF.Net/issues/11) adds Lazy and LazyWithCache sections. The APIs are available in both core targets (netstandard2.1 and net9.0).

## Defer a section

~~~csharp
page.Content().Column(column =>
{
    for (var section = 0; section < 1000; section++)
    {
        var index = section;

        column.Item().Lazy(content => content.Column(inner =>
        {
            inner.Item().Text($"Section {index}").Bold();

            for (var row = 0; row < 100; row++)
                inner.Item().Text($"Row {index}.{row}");
        }));
    }
});
~~~

The document factory only creates the Lazy wrapper. The content builder runs when layout first reaches that section, not during document composition. The composed section participates in normal measurement, page breaks and repeated/partial rendering, so a section may span pages when its child supports paging.

Lazy reconstructs the section for each render pass and releases the composed tree after the section fully renders in a non-initial pass. This keeps glyphs available for the first-pass subsetting inspection while allowing later passes and completed documents to reclaim section elements sooner. LazyWithCache composes once and retains the tree, resetting its internal state between passes; it trades more retained memory for less construction work.

## Cache trade-off

| Variant | Construction count | Retained content | Best use |
| --- | --- | --- | --- |
| Lazy | Once per render pass | Released after the section fully renders (except through the initial subsetting pass) | Large, independent report sections where peak/retained memory matters more than repeated construction. |
| LazyWithCache | Once per document generation | Retained and reset for each pass | Sections that are expensive to compose and fit comfortably in memory. |

The actual memory difference depends on the element tree, fonts, images and native caches in the section. Measure the target report rather than assuming a fixed ratio. Images and other caller-owned resources are borrowed, not owned by Lazy; releasing the section tree does not dispose caller resources.

## Lifecycle and failure rules

- The content builder must be re-runnable for Lazy because each render pass reconstructs the section. It must not mutate closed-over containers or rely on invocation count for correctness. LazyWithCache invokes it once per generation.
- Content receives the inherited text style, content direction, page context and canvas dependencies before its first measure. Nested Lazy elements receive their own style and direction in the same way.
- A construction exception propagates out of generation, clears the partially built section and leaves the wrapper reusable for a later generation attempt.
- Repeated sections retain their first-pass text through layout resets so font subsetting can inspect all glyphs. Multi-column measurement snapshots the deferred tree and descendant progress before speculative drawing, including dynamic content and height balancing.
- A section that never fully fits continues across pages normally. Its child keeps the existing pagination state; Lazy does not duplicate or consume content between measurement and drawing.
- Callers own images, streams and other resources captured by the content builder. Dispose them only after all generations that may replay the section have finished.
- The cache lives with the element tree for one document generation. Repeated GeneratePdf/GenerateImages calls on a Document builder create a fresh tree, so LazyWithCache does not share content across calls.
- Lazy is not a substitute for `PreventPageBreak`; a section larger than a page may split.

## Example and acceptance evidence

From Source:

~~~powershell
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --filter FullyQualifiedName~LazyExamples
~~~

[LazyExamples.cs](../../Source/FossPDF.Examples/LazyExamples.cs) writes `lazy-sections.pdf` and `lazy-with-cache.pdf` under `lazy-example/`.

LAY-106 is active in the [acceptance catalog](acceptance-fixtures.json): eager, Lazy and LazyWithCache layouts of 1000 items must produce identical page counts, pixels and order; composition counters must show deferred and cached work; and a second generation must rebuild cached sections without stale state. LazyTests additionally covers multi-page sections, release after final render, cache retention and reconstruction after an exception. See [the Windows evidence report](evidence/fp-106-windows.md) for the local test record.

The lifecycle suite also covers cancellation during drawing, font cleanup, borrowed-image reuse, and balanced-column dynamic content. Drawing cancellation follows the existing generator convention: `DocumentDrawingException` retains the `OperationCanceledException` as its inner exception. The large-report probe writes `LAY-106/retained-memory.json` beside the parity artifacts. It holds the generated section wrappers alive, measures managed bytes reclaimed when those roots are released, records process working set as a native-memory proxy, and verifies no document-owned font resources remain. These observations describe retained tree memory, not a portable peak-memory ratio or a separate native-allocation measurement.