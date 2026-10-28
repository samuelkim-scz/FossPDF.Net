# FP-104 Windows validation

Recorded October 27, 2026, for [FP-104 / issue #9](https://github.com/samuelkim-scz/FossPDF.Net/issues/9),
based on main commit `c4473ef` (FP-101) plus the `feature/fp-104-multi-column` branch.

Environment: Windows 11 Pro 10.0.26200, win-x64, 12th Gen Intel Core i7-12700T, .NET SDK 9.0.305,
.NET 8.0.22 and .NET 9.0.9 runtimes, SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.5, bundled Lato fonts.
No package or native dependency changed.

## Results

| Check | Result |
| --- | --- |
| Release solution build with `--warnaserror` | Zero warnings, zero errors |
| Complete .NET 8 suite | 297 passed, 0 failed, 23 planned/skipped |
| Complete .NET 9 suite | 298 passed, 0 failed, 23 planned/skipped |
| New `MultiColumnTests` per framework | 19 passed |
| Parity category per framework | 18 passed, 0 failed, 23 planned/skipped |
| Shared layout fixture | `LAY-104` active and passing (8 rendered scenarios) |
| .NET 9 multi-column examples | 2 passed |
| Quality tool, `--mode all` | visual and performance passed; conformance skipped (validators unavailable) |

The activation of `LAY-104` reduced the planned/skipped count from 24 to 23 and keeps
`Catalog_AccountsForEveryFeatureWorkItemAndExecutableFixture` green. The quality tool's visual ink
counts (`report` 108288 px, `text` 37067 px, `graphics` 104260 px, `image` 67815 px) are unchanged
from the FP-101 baseline, so no visual baseline was re-measured: existing scenarios do not use the
new element and the rendering stack itself was not modified.

## Implemented behavior

- `IContainer.MultiColumn(Action<MultiColumnDescriptor>)` flows one content stream through a fixed
  number of vertical columns. `Columns`, `Spacing`, `BalanceHeight`, `Content` and `Spacer` form the
  descriptor; duplicate `Content`/`Spacer` calls throw `DocumentComposeException`, and invalid column
  counts or non-finite spacing throw `ArgumentOutOfRangeException`.
- Columns fill top to bottom, continue at the top of the next column, then report a partial render so
  the generator continues on the next page.
- `BalanceHeight()` reports the smallest height that fits all content and draws at that height. The
  fluent layer wraps the element in a vertical `Shrink` layer, matching QuestPDF's balanced mode, so a
  parent that offers the full page height does not re-flow the content at that height.
- `Spacer()` content is drawn in every gap where the flow continues, receives a slot as wide as the
  configured spacing, and repeats with the same content on later pages.
- Right-to-left content fills the rightmost column first and offsets each column by its logical start.
- Multi-column composes with all layout elements. Text, tables, images and dynamic components flow
  through columns and continue across columns and pages.

### Layout state and speculative drawing

Balancing requires repeated trial layouts. The element measures by drawing the content into a
`FreeCanvas` through a replaceable `ProxyCanvas` target; every stateful child implementing
`IStateSnapshot` is wrapped in a drawing observer that captures its state before the first
speculative draw and restores it afterwards.

### Defects fixed along the way

- `ElementProxy` (the base of `CacheProxy` and `DebuggingProxy`) did not implement `IStateSnapshot`.
  Because caching proxies are applied before multi-column observers, cached stateful elements
  (`Column`, `Table`, `ShowOnce`, `Dynamic`, ...) were never rewound, so speculative draws consumed
  their content permanently. The proxy now forwards `CaptureState`/`RestoreState` to its child.
  Without the fix, `ShowOnce` was dropped and table rows were lost as soon as caching was enabled
  (the default outside a debugger).
- Balanced layout initially produced unbalanced columns when the multi-column element was the direct
  page content: the page's vertical extender draws with the full page height, so the balanced plan
  height was ignored while drawing. Adding the `Shrink` element and wrapping balanced content in it
  resolved the mismatch and matches the reference behavior.
- The spacer's 1-point separator rasterizes into one or two pixel columns depending on rounding. The
  test therefore locates the line by its band and asserted width instead of a fixed pixel column;
  the fixture asserts "exactly the declared gap width" inside the gap.

Reference behavior for balancing, right-to-left order and the shrink layer was compared against a
locally cached QuestPDF package (2025.7.4) by rendering probe documents and inspecting pixels. That
probe is discovery evidence only; the accepted contract is the independently authored `LAY-104`
fixture against the 2026.9.1 target. No conformance claim beyond the fixture is made.

## Fixture evidence

`LAY-104` renders on 240x180 pages with a 10-point margin, two 104-point columns and a 12-point gap at
72 DPI, emitting PDFs and PNGs beneath `parity-artifacts/LAY-104/`:

| Scenario | Check |
| --- | --- |
| `colored-flow` | 24 blocks of 104x15; page 1 contains exactly 20 block areas (10 per column), page 2 exactly 4; no ink in the gap; repeated generation yields identical per-page ink. |
| `balanced` | 8 blocks of 104x15 balance to 4 + 4 in two 60-point columns; every block is at its expected position; nothing is drawn below the balanced height; total ink equals 8 block areas. |
| `right-to-left` | 4 blocks of 104x60; the right column holds items 1-2 and the left column items 3-4. |
| `separator` | 12 blocks of 104x20 with a centered 1-point `#808080` line; at three heights the gap contains exactly one ink pixel column, and the gap edges stay white. |
| `images` | 4 raster images of 104x60 balance to 2 + 2 and appear at the expected column positions. |
| `paragraphs` | The fixture's Alpha/Beta/Gamma paragraphs in three colors span two pages; per-paragraph glyph ink equals the same paragraphs rendered in a single 104-point column, so no glyph is lost or duplicated; reading order never moves backwards down the columns; page 1 fills both columns; the last page's two columns differ in ink bottom by at most one line; repeated generation is identical. |
| `empty-content` | One blank page. |
| `oversized-child` | A 200-point child in a 160-point column throws `DocumentLayoutException` twice. |

The paragraph reading-order check classifies ink rows by paragraph color instead of using text
extraction, and the no-loss check compares raster ink counts against an independently composed
single-column reference. No text goldens are used.

## Cost measurements

Measured on the environment above with a Release `net9.0` harness, three warm-up runs, then 30
iterations; medians are reported. "fit" is three short paragraphs on one page; "flow" is three long
paragraphs spanning two pages. Both use two columns, a 12-point gap and a separator.

| Scenario | Mode | PDF median | PDF allocations | Raster median |
| --- | --- | --- | --- | --- |
| fit | unbalanced | 2.9 ms | 0.57 MB | 9.8 ms |
| fit | balanced | 9.9 ms | 2.59 MB | 13.4 ms |
| flow | unbalanced | 4.3 ms | 0.99 MB | 66.7 ms |
| flow | balanced | 10.0 ms | 3.29 MB | 64.9 ms |

Peak working set was 51.6-56.8 MB across all four processes; managed heap after the runs was
1.2-10.8 MB. Balancing costs up to eight additional measure-and-draw passes per page, which is
visible as roughly 3.5x PDF generation time and 3-4x allocation for the one-page document. Raster
export is dominated by text rendering and does not show a meaningful difference. These numbers are
informational, not a performance gate.

## Limits

- Content must be splittable between columns; an unbreakable child taller than a column raises
  `DocumentLayoutException` rather than being clipped.
- Balancing optimizes height only and resolves to `available height / 256` (about 0.6 points on the
  160-point content area used here).
- The implementation is width-generic, but the fixture and evidence cover two columns.
- Nested multi-column layouts follow the same state rules but are not part of this evidence.
- Conformance validators (qpdf, veraPDF) are unavailable here and are not needed by this work item.

## Reproduce

From `Source`:

~~~powershell
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter TestCategory=FeatureParity
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~MultiColumnTests
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~MultiColumnExamples
dotnet run --project FossPDF.Quality/FossPDF.Quality.csproj -c Release -- --mode all --output ../artifacts/quality
~~~
