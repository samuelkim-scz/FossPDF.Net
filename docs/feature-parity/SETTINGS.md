# Per-document settings and compatibility contract

[FP-004 / issue #5](https://github.com/samuelkim-scz/FossPDF.Net/issues/5) defines settings for the QuestPDF 2026.9.1 parity target. The public types live in FossPDF.Infrastructure and the fluent methods in FossPDF.Fluent. All APIs below are available in both core targets, netstandard2.1 and net9.0. The .NET 8 tests consume the netstandard2.1 build; .NET 9 tests and examples consume net9.0.

## Configure a document

~~~csharp
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;

var metadata = new DocumentMetadata
{
    Title = "Invoice",
    RasterDpi = 72
};

var document = Document.Create(container => container.Page(page =>
{
    page.Size(240, 120);
    page.Margin(12);
    page.Content().Text("Invoice 2026-001");
}))
.WithMetadata(metadata)
.WithSettings(new DocumentSettings
{
    ImageRasterDpi = 144,
    ImageCompressionQuality = 90,
    ContentDirection = ContentDirection.LeftToRight,
    DocumentLayoutExceptionThreshold = 10,
    EnableCaching = true,
    CheckIfAllTextGlyphsAreAvailable = true
});

var pdf = document.GeneratePdf();
var thumbnail = document.GenerateImages(new ImageGenerationSettings
{
    RasterDpi = 72,
    Format = ImageOutputFormat.Png
});
~~~

DocumentSettings and ImageGenerationSettings are immutable records. Use object initializers or with-expressions. Configure the document and caller-owned metadata before sharing them with rendering tasks.

The concrete Document.WithSettings method stores the options and returns the same builder, like WithMetadata. The extension on an arbitrary IDocument returns a wrapper borrowing the source; retain its return value. IDocument still requires only GetMetadata and Compose. Existing document implementations need no new members.

Repeated WithSettings calls replace the complete override record. Null members inherit compatibility defaults; they do not retain values from an earlier override record. WithMetadata and WithSettings may be called in either order on the concrete builder.

## Precedence and defaults

Each generation resolves one private snapshot before composition. It copies metadata, reads document overrides and captures the four process-wide runtime defaults. Rendering never changes FossPDF.Settings or the caller's metadata.

| Setting | When omitted | Explicit override |
| --- | --- | --- |
| DocumentSettings.ImageRasterDpi | DocumentMetadata.RasterDpi; legacy default 72 | Positive integer; renderer rasterization DPI, also the default page-image DPI |
| ImageCompressionQuality | DocumentMetadata.ImageQuality; legacy default 101 | Integer 0-101; 101 retains the legacy lossless behavior |
| ContentDirection | LeftToRight | LeftToRight or RightToLeft; explicit page/element direction takes precedence |
| PdfAConformance | Existing metadata PdfA false -> None, true -> Legacy | None disables the legacy flag; Legacy enables the same flag |
| PdfUaConformance | None | PdfUa1 is reserved and rejected for PDF until its backend is implemented |
| CompressDocument | true; normal renderer compression | false is reserved and rejected for PDF |
| EnableAdditionalCompression | false | true is reserved and rejected for PDF |
| Linearize | false | true is reserved and rejected for PDF |
| DocumentLayoutExceptionThreshold | FossPDF.Settings.DocumentLayoutExceptionThreshold; initial default 250 | Positive maximum page count, inclusive |
| EnableCaching | FossPDF.Settings.EnableCaching; initially enabled without a debugger | Per-document bool |
| EnableDebugging | FossPDF.Settings.EnableDebugging; initially enabled with a debugger | Per-document bool |
| CheckIfAllTextGlyphsAreAvailable | Matching FossPDF.Settings flag; initially enabled with a debugger | Per-document bool |

The page-limit boundary now allows a completed document containing exactly the configured maximum. A document requiring another page fails with DocumentLayoutException. This corrects the previous boundary check, which also rejected a completed document at the limit.

Legacy DocumentMetadata properties and its obsolete process-setting wrappers remain available. Setting an obsolete wrapper still changes the process default, as before. Prefer document overrides for per-request behavior. Configure process defaults, fonts and subset callbacks at application startup; concurrent mutation of global defaults or of document/content objects is not a supported configuration pattern.

Separate generation calls receive independent snapshots. Metadata changes made during Compose affect later generation calls, rather than changing the current call partway through. Custom documents must create fresh element trees and keep their own composition code/resources safe for concurrent use. Configure a builder before starting rendering; WithSettings/WithMetadata are configuration operations.

ImageRasterDpi maps the existing backend-wide RasterDpi setting. It does not implement per-image downsampling or quality controls; those remain in FP-202. PDF layout uses points, independently of raster output DPI.

## Page-image export

~~~csharp
var pages = document.GenerateImages(new ImageGenerationSettings
{
    RasterDpi = 144,
    Format = ImageOutputFormat.Png,
    CompressionQuality = 100
});

document.GenerateImages(
    index => $"page-{index + 1}.png",
    new ImageGenerationSettings { RasterDpi = 72 });
~~~

| ImageGenerationSettings member | Default / behavior |
| --- | --- |
| RasterDpi | Null inherits the resolved document DPI. An explicit positive value affects this export only. |
| Format | Png. Jpeg and Webp are reserved for FP-203 and currently throw NotSupportedException. |
| CompressionQuality | Integer 0-100, default 100. PNG is lossless and ignores quality. Lossy encoders will consume this setting when implemented. |

Existing GenerateImages() and GenerateImages(Func<int, string>) calls preserve their behavior. The new path-selector overload adds a settings argument; indices remain zero-based. Images are generated eagerly before path selection. The caller receives managed byte arrays and does not dispose the settings or results.

## Conformance and compression extension points

The contract reserves PdfA2A, PdfA2B, PdfA2U, PdfA3A, PdfA3B and PdfA3U in PdfAConformance, and PdfUa1 in PdfUaConformance. Requesting these PDF modes throws NotSupportedException before composition or output writes. Legacy is the existing unqualified Skia PdfA flag; it does not claim a particular validated PDF/A profile.

Likewise, disabling ordinary compression, requesting extra optimization or enabling linearization is rejected for PDF. These options cannot silently fall back to the normal renderer behavior.

| Capability owner | Integration obligation |
| --- | --- |
| [FP-402](https://github.com/samuelkim-scz/FossPDF.Net/issues/27) | Implement the six PDF/A profiles and validation; replace the corresponding generation guard only when the backend honors the selected profile. |
| [FP-401 / FP-403](https://github.com/samuelkim-scz/FossPDF.Net/issues/26) / [PDF/UA](https://github.com/samuelkim-scz/FossPDF.Net/issues/28) | Carry semantic structure and enforce PDF/UA requirements before accepting PdfUa1. |
| [FP-306](https://github.com/samuelkim-scz/FossPDF.Net/issues/25) | Honor compression overrides/linearization and document their conformance/security interactions. |
| [FP-203](https://github.com/samuelkim-scz/FossPDF.Net/issues/17) | Implement JPEG/WebP encoding, quality, alpha/background policy and decoded-output validation. |
| [FP-202](https://github.com/samuelkim-scz/FossPDF.Net/issues/16) | Add per-image optimization overrides without changing these document/output precedence rules. |

Valid PDF-only conformance/compression options do not prevent PNG, SVG, XPS or preview generation. Those outputs do not make PDF conformance claims. Invalid enum values, non-positive explicit DPI/page limits and out-of-range explicit quality values fail with ArgumentOutOfRangeException. Null settings arguments fail with ArgumentNullException.

These extension points establish a shared contract. The corresponding feature issues and planned acceptance fixtures remain open until implemented and validated.

## Ownership and compatibility

| API / resource | Contract |
| --- | --- |
| DocumentSettings / ImageGenerationSettings | Managed immutable values; no disposal required. |
| WithSettings wrapper | Borrows the source IDocument; never disposes caller resources or changes its metadata. |
| GeneratePdf(Stream) | Renders to an internal buffer, then writes at the caller's current position; requires a writable stream and leaves it open. Invalid/unsupported settings write no bytes. |
| GeneratePdf(path) | Renders before replacing the destination; file I/O failure is not an atomic-write guarantee. |
| GenerateXps(Stream) | Preserves the existing writable/seekable requirement and caller ownership. |
| GenerateXps(path) | Preserves existing behavior: creates/truncates the destination before generation. |
| Image/SVG path selectors | Preserve zero-based indexing and existing overwrite behavior. |
| Shared images and registered fonts | Caller/global ownership contracts remain unchanged; keep borrowed resources alive until generation finishes. |
| Rendering-native objects | Generation still releases owned canvases, fonts and temporary resources on success/failure. Preview picture ownership remains with its caller. |

No new IDocument members, package dependencies or target frameworks are required. Metadata, existing fluent calls and output overloads remain available. The new ContentDirection enum is public in its existing infrastructure namespace.

## Run and extend

From Source:

~~~powershell
dotnet restore FossPDF.sln
dotnet build FossPDF.sln -c Release --no-restore --warnaserror
dotnet test FossPDF.UnitTests/FossPDF.UnitTests.csproj -c Release --no-build --no-restore --logger trx --results-directory ../artifacts/fp004/full
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~DocumentSettingsExamples
~~~

[DocumentSettingsExamples.cs](../../Source/FossPDF.Examples/DocumentSettingsExamples.cs) is a runnable .NET 9 consumer. It writes a PDF, a 72-DPI thumbnail and a 144-DPI page under the example assembly's settings-example directory. Existing solution samples compile unchanged.

DocumentSettingsTests covers precedence, legacy IDocument implementations, metadata snapshots, concurrent DPI/direction/runtime policies, glyph checks, inclusive page limits, input validation and fail-before-write behavior. SET-004 joins the [acceptance catalog](acceptance-fixtures.json) with independent raster expectations for overriding legacy DPI without mutation.

The implementation is independently authored against this contract. The reference [QuestPDF settings overview](https://www.questpdf.com/concepts/document-settings.html) and [output overview](https://www.questpdf.com/concepts/generating-output.html) identify target capability categories; FossPDF retains its own compatibility defaults.

The [Windows evidence report](evidence/fp-004-windows.md) records the initial build, test and consumer results.
