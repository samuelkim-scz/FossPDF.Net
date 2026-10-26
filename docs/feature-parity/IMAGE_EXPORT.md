# Page-image export: PNG, JPEG and WebP

[FP-203](https://github.com/samuelkim-scz/FossPDF.Net/issues/17) adds JPEG/WebP encoding to GenerateImages. These APIs are available in both core targets (netstandard2.1 and net9.0), using the existing Skia dependencies.

## Export byte arrays or files

~~~csharp
using FossPDF.Fluent;
using FossPDF.Infrastructure;

var options = new ImageGenerationSettings
{
    Format = ImageOutputFormat.Jpeg,
    RasterDpi = 144,
    CompressionQuality = 90,
    BackgroundColor = "#ffffff"
};

var pages = document.GenerateImages(options).ToArray();

document.GenerateImages(
    index => $"page-{index + 1}.jpg",
    options);
~~~

Byte arrays and file/stream selectors use the same page order. Selector indices start at zero. Format controls the encoded bytes; filenames/extensions are supplied by the caller. Existing GenerateImages() and GenerateImages(pathSelector) calls still produce PNG.

RasterDpi inherits the resolved document DPI when omitted (legacy default 72). Explicit output DPI overrides it for this export only. Layout remains in points; a 120x100-point page becomes 240x200 pixels at 144 DPI. Dimensions truncate fractional pixels as before. This controls raster dimensions rather than promising embedded DPI/EXIF metadata.

## Format and background rules

| Format | CompressionQuality | Default background | Alpha |
| --- | --- | --- | --- |
| Png | Accepted range 0-100; ignored by the lossless PNG encoder | Transparent | Preserved |
| Jpeg | 0-100 lossy quality; 100 remains JPEG encoding | White | Flattened before drawing/encoding |
| Webp | 0-99 lossy quality; 100 selects lossless encoding | Transparent | Preserved in both modes |

BackgroundColor fills unpainted page areas. Explicit page backgrounds/content are drawn over it. Use the same hex formats as other FossPDF colors: RGB/ARGB/RRGGBB/AARRGGBB, with optional hash prefix; eight-digit values are alpha-first.

PNG/WebP accept opaque or translucent backgrounds. JPEG requires an opaque explicit background, since its output has no alpha channel. Null selects the format default. For example, "#0000ff" flattens a JPEG over blue, while "#400000ff" is a translucent blue background suitable for PNG/WebP.

WebP lossless preserves the rendered page raster; RGB values in fully transparent pixels are not meaningful and may be normalized by the codec. Lossy output is judged by decoded color/geometry, rather than byte identity. Quality is not a promise of a particular file size. DocumentSettings.ImageCompressionQuality concerns PDF image encoding and does not override this export's CompressionQuality.

## Write to caller-owned streams

For a two-page document:

~~~csharp
using var first = new MemoryStream();
using var second = new MemoryStream();

document.GenerateImagesToStreams(
    index => index == 0 ? first : second,
    new ImageGenerationSettings
    {
        Format = ImageOutputFormat.Webp,
        CompressionQuality = 100
    });

// Both streams remain open; each contains one encoded page.
// Writes start at each stream's current position.
~~~

GenerateImagesToStreams(streamSelector) defaults to PNG. The distinct method name preserves existing path-selector overload resolution. Streams must be writable; seeking/reading is not required. The library borrows streams and leaves them open on success or write failure. The caller disposes resources and chooses whether distinct page indices use distinct streams.

All pages are rendered and encoded eagerly before any file/stream selector is called. Invalid options and encoding failures therefore do not write page destinations. A selector or I/O failure can occur after earlier pages have been written; multi-file/stream export is not an atomic transaction.

## Validation and limits

- Explicit DPI must be positive, quality must be 0-100, and the format enum must be known.
- Invalid background colors and translucent JPEG backgrounds fail before composition/output selection.
- Computed pixel dimensions must be positive finite integers after truncation, and the ARGB page buffer must fit a signed 32-bit byte count.
- WebP dimensions cannot exceed 16383 pixels per axis. This is the [format limit](https://developers.google.com/speed/webp/faq#what_is_the_maximum_size_a_webp_image_can_be).
- Other native allocation/encoding failures surface as rendering/encoding exceptions with the selected format; no page destinations are selected before encoding completes.
- Null selectors, null returned streams and read-only streams are rejected. Write exceptions propagate without transferring ownership.

The background/format/quality options are immutable per export. Concurrent calls can use different formats/backgrounds without changing document metadata or process defaults.

## Example and acceptance evidence

Run the .NET 9 examples from Source:

~~~powershell
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --filter FullyQualifiedName~DocumentSettingsExamples
~~~

[DocumentSettingsExamples.cs](../../Source/FossPDF.Examples/DocumentSettingsExamples.cs) writes both pages in all three formats and verifies caller-owned streams. CI runs these examples and uploads their output with the parity evidence.

OUT-203 is active in the [acceptance catalog](acceptance-fixtures.json): each format must decode into two 240x200 pages, with red then blue markers at 144 DPI. PNG probes are exact; JPEG/WebP at quality 90 allow up to eight RGB levels of difference at solid interiors away from edges. The independent grayscale fixture additionally verifies that higher lossy quality retains more signal, and WebP quality 100 matches the rendered raster exactly.

ImageExportTests also checks partial alpha, compositing, page-background precedence, unchanged default PNG bytes, format dimensions, page ordering across byte arrays/files/streams, concurrent backgrounds, invalid options and stream ownership on failure.

See [the settings contract](SETTINGS.md) for shared defaults/precedence and [the Windows evidence report](evidence/fp-203-windows.md) for the local build/test record.
