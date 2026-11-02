# Migration and API equivalence for the 2026.9.1 target

The [audit](PARITY_AUDIT.md) records the remaining release gate. This guide describes the implemented
FossPDF workflows and their boundaries. Behavioral equivalence follows the accepted fixture
contracts; matching upstream namespaces or binary output is not required.

## Choose the package and target framework

| Package / workflow | Framework and deployment |
| --- | --- |
| `FossPDF` | `netstandard2.1` and `net9.0`; .NET 8 consumers use the former |
| `FossPDF.FontSubset` / `FossPDF.FontSubset.Glue` | Optional .NET 9 native HarfBuzz subsetting |
| `FossPDF.Svg` | Optional .NET 9 vector SVG input; parse once and dispose reusable images |
| `FossPDF.PdfOperations` | Optional .NET 9 existing-PDF, tagged/conformance and invoice workflows |
| `FossPDF.Previewer` | .NET 9 local tool; picture-only clients still work; inspection is opt-in |

Use FossPDF namespaces (`FossPDF.Fluent`, `FossPDF.Infrastructure`, `FossPDF.Drawing`) rather than
replacing package names alone. Add the optional companions only for the features you use. The
[native deployment guide](RUNTIME_SUPPORT.md) specifies startup assets, native compiler requirements
and the exact trimmed/AOT qualification scope. Repository package versions still read 2026.5.1;
the source/build packages contain these changes, and no new NuGet publication is implied.

## Preserve existing document generation

Existing `IDocument`, `Document.Create`, fluent layout, `GeneratePdf`, PNG image export, SVG/XPS
output and preview calls keep their workflows. Prefer immutable per-document overrides over
mutating process-wide defaults during requests:

```csharp
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;

var document = Document.Create(container => container.Page(page =>
{
    page.Size(300, 180);
    page.Margin(16);
    page.Content().Text("Quarterly revenue");
})).WithMetadata(new DocumentMetadata { Title = "Quarterly report" })
  .WithSettings(new DocumentSettings { ImageRasterDpi = 144, EnableCaching = true });

document.GeneratePdf("report.pdf");
```

Legacy metadata and obsolete process-setting wrappers remain available. Configure shared fonts,
callbacks and defaults at startup. Separate generation calls receive independent settings
snapshots; callers remain responsible for their own mutable document/component state.

## Capability equivalents

| Upstream capability | FossPDF workflow and contract |
| --- | --- |
| Paragraph alignment, indentation, word spacing | `Justify`, `AlignStart`/`AlignEnd`, `ParagraphFirstLineIndentation`, word spacing; [paragraph guide](TEXT_PARAGRAPH_LAYOUT.md) |
| Line clamping and custom ellipsis | `ClampLines`; [text clamping](TEXT_CLAMPING.md) |
| OpenType controls and text decorations | Explicit font features and decoration styles; [text guide](TEXT_FEATURES.md) |
| Newspaper-style flow and balancing | `MultiColumn`; [multi-column guide](MULTI_COLUMN.md) |
| Pagination helpers | `PreventPageBreak`, `Repeat`, page-aware `ShowIf`; existing `ShowOnce`/`SkipOnce` remain |
| Deferred composition | `Lazy` / `LazyWithCache`; [lifecycle and resource rules](LAZY_COMPOSITION.md) |
| Shrinking and stacking | `ShrinkHorizontal`, `ShrinkVertical`, `Shrink`, `ZIndex`; `MinimalBox` retains its legacy equivalence; [layout helpers](LAYOUT_HELPERS.md) |
| Inline alignment | `TextDescriptor.Element` with `TextInjectedElementAlignment`; [alignment values](LAYOUT_HELPERS.md) |
| Content geometry | `CaptureContentPosition`, `ContentPositions`, dynamic `GetContentCapturedPositions`; [units and occurrences](CONTENT_POSITIONS.md) |
| Complex scripts / CJK | Existing HarfBuzz shaping plus the [qualified font/script corpus](COMPLEX_SCRIPTS.md); supply fonts covering your own text |
| SVG input/preloading | `SvgImage.FromText`/`FromFile`/`FromBytes`/`FromStream`, then `.Svg(image)`; [SVG guide](SVG_INPUT.md) |
| Per-image optimization | Image descriptor DPI/resampling/quality overrides; [image optimization](IMAGE_OPTIMIZATION.md) |
| JPEG/WebP page export | `GenerateImages(ImageGenerationSettings)` / `GenerateImagesToStreams`; [format/background rules](IMAGE_EXPORT.md) |
| Rounded containers, borders, gradients | [Container styling](CONTAINER_STYLING.md); preserve documented clipping/alignment semantics |
| Dashes and shadows | [Line/shadow styling](LINE_STYLES_SHADOWS.md); effect tolerances are feature-specific |
| Merge generated documents | `Document.Merge(...).UseOriginalPageNumbers()` or `.UseContinuousPageNumbers()`; [merge guide](MERGING.md) |
| Load/merge/select existing PDF pages | `DocumentOperation.Load`/`LoadFile`, `TakePages`, `Merge`/`MergeFile`, `Save`; [operations guide](PDF_OPERATIONS.md) |
| Overlays and underlays | [PDF layers](PDF_LAYERS.md); existing pages retain their boxes and vector content |
| Security, attachments and XMP | [Security](PDF_SECURITY.md) and [attachments/XMP](ATTACHMENTS_XMP.md); preserve stream ownership and credential handling |
| Additional compression / linearization | `DocumentSettings` controls or operations companion with deployed qpdf; [optimization guide](PDF_OPTIMIZATION.md) |
| Semantic/tagged PDF | Semantic wrappers and `GenerateTaggedPdf`; [authoring and supported import boundaries](PDF_ACCESSIBILITY.md) |
| PDF/A-2/3 | `GeneratePdfA` in the companion, with pinned veraPDF; [archival guide](PDF_ARCHIVAL.md) |
| PDF/UA-1 | `GeneratePdfUa`, semantic authoring, machine checks and author review; [accessibility guide](PDF_UNIVERSAL_ACCESSIBILITY.md) |
| Structured e-invoice | `GenerateEInvoice` accepts external XML and validates final bytes; [supported release/profile pairs](EINVOICE.md) |
| Companion-style inspection | `.Inspect(...)`, opt-in inspected preview, local hierarchy/search/measurement/source tools; [protocol](PREVIEW_INSPECTION.md) and [UI](PREVIEWER_TOOLS.md) |

For generated merging, configure settings/metadata on the merged result: source settings are not
inherited. Original numbering restarts within each source; continuous numbering spans them all.
Existing-PDF import is a separate workflow and does not promise to preserve signatures,
conformance or every arbitrary PDF feature.

## Conformance and native deployment changes

Ordinary `GeneratePdf` retains `None`/`Legacy` PDF/A behavior and rejects explicit validated
profiles. Use the .NET 9 companion's `GeneratePdfA`/`GeneratePdfUa` methods for the six archival
profiles or PDF/UA-1. These validate the finished bytes and require application-deployed Java and
the pinned validator. `Legacy` is an unqualified historical flag, not a selected archival level.
Invoice generation additionally needs the pinned Mustang distribution and externally authored
XML. qpdf, Java, veraPDF and Mustang are external deployments, not binaries shipped by FossPDF.

For AOT embedded-font registration, supply the resource assembly explicitly:

```csharp
FontManager.RegisterFontFromEmbeddedResource("MyApp.Resources.Font.ttf", typeof(Program).Assembly);
```

The same overload exists on `DocumentSpecificFontManager`. Caller-discovery overloads remain
available for managed applications; NativeAOT callers can also open the resource stream and use
`RegisterFont(stream)`. Subsetting uses `BuiltInFontSubsetting.Callback` from the optional glue
package. Keep the Skia/HarfBuzz versions as the reviewed cohort and retain their notices.

Stream APIs generally borrow caller streams; follow each feature's documented position,
seekability and failure rules. Dispose reusable `SvgImage`/shared native resources only after
generation, and do not mutate global font/callback registrations during concurrent rendering.

## Run the examples and validation

From `Source`, with the repository's .NET SDK selection:

```powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~DocumentSettingsExamples|FullyQualifiedName~ParagraphLayoutExamples|FullyQualifiedName~DocumentMergeExamples"
```

The [audit manifest](parity-audit-2026.9.1.json) links a runnable example and tests for every work
item. Conformance examples require the validators in their feature guides. The
[runtime consumer driver](../../Source/FossPDF.RuntimeSmoke/README.md) reproduces isolated
package consumption, trimming and AOT checks on native hosts.
