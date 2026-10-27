# Per-image raster resolution and compression

[FP-202](https://github.com/samuelkim-scz/FossPDF.Net/issues/16) adds per-image DPI and quality controls to PDF generation. The APIs are available in both core targets (netstandard2.1 and net9.0) and use the existing SkiaSharp dependency.

## Configure an image

~~~csharp
using FossPDF.Fluent;
using FossPDF.Infrastructure;

// Resample to the displayed size at 144 DPI and encode opaquely at quality 75.
page.Content()
    .Width(80)
    .Image("photo.jpg")
    .WithRasterDpi(144)
    .WithCompressionQuality(ImageCompressionQuality.High);

// The same controls are available for dynamically generated images.
page.Content()
    .Image(size => RenderChart(size))
    .WithRasterDpi(192)
    .WithCompressionQuality(80);
~~~

The fluent Image overloads now return ImageDescriptor or DynamicImageDescriptor. Statement-style calls such as container.Image(path) continue to compile. Assign or chain the descriptor to configure the image. A descriptor configures one image occurrence; other occurrences keep their own settings.

| Method | Behavior |
| --- | --- |
| WithRasterDpi(int dpi) | Target resolution for this occurrence. Positive integer; display size in points is converted at dpi/72. |
| WithCompressionQuality(int quality) | Lossy re-encoding quality from 0 to 100. |
| WithCompressionQuality(ImageCompressionQuality quality) | Preset mapping: Best 100, VeryHigh 90, High 75, Medium 50, Low 25, VeryLow 10. |
| UseOriginalImage(bool value = true) | Draws the decoded source unchanged, ignoring DPI, quality and document image defaults. |

WithRasterDpi and WithCompressionQuality can be combined or used independently. Setting only DPI resamples losslessly; setting only quality keeps the source dimensions. Invalid DPI (zero or negative), quality outside 0-100, and unknown enum values throw ArgumentOutOfRangeException while composing the document.

## Resampling rules

The target raster size is calculated from the space the layout assigns to the image:

~~~text
pixel width  = ceil(display width  in points * dpi / 72)
pixel height = ceil(display height in points * dpi / 72)
~~~

Each axis is capped independently at the corresponding source dimension, so an image is never upscaled. Resampling uses Skia's linear filter without mipmaps. The layout and the PDF page remain measured in points; only the embedded raster changes. Reusing one source image at different display sizes produces independent rasters. The source image is borrowed and never resized, re-encoded or otherwise mutated, so caller-owned SharedImage instances remain valid for later documents.

## Quality and encoding

The encoder chooses the output format from the source image's alpha type:

| Source | Encoding | PDF representation |
| --- | --- | --- |
| Opaque | JPEG at the requested quality; quality 100 remains JPEG | Skia embeds the JPEG as a DCTDecode image, so the requested bytes are preserved. |
| Has alpha | WebP lossy at the requested quality; quality 100 selects lossless WebP | PDF has no WebP image filter. Skia decodes the WebP and stores the decoded bitmap losslessly (FlateDecode) with its alpha channel. |

Consequently, quality is a decoded-image control rather than a guarantee of file size. It works as a size/quality trade-off for opaque JPEG sources. For transparent sources, it lowers the color fidelity stored in the PDF, but the PDF backend re-encodes the result losslessly, so the file may not be smaller than the source. Transparency itself is preserved. Document pages and page-image export (FP-203) use independent quality settings and do not inherit this per-image value.

Resampling alone preserves pixels at the new resolution. An opaque image that is resampled without a quality value is stored losslessly by the PDF backend, while resampling plus quality uses JPEG.

## Document defaults and precedence

DocumentSettings.ImageRasterDpi and DocumentSettings.ImageCompressionQuality act as defaults for every image that has no per-image value, including images produced dynamically during layout. ImageCompressionQuality accepts 0-101; 101 means no recompression and matches the legacy default. Settings apply to PDF, XPS, image, SVG and preview rendering through the shared page context. Layout-only passes skip resampling and encoding because they do not paint images.

Precedence, highest first:

1. UseOriginalImage(true) on the image.
2. The image's WithRasterDpi and WithCompressionQuality values.
3. DocumentSettings.ImageRasterDpi and ImageCompressionQuality, when explicitly set.
4. The existing behavior: no resampling and no recompression.

DocumentMetadata.RasterDpi still controls the backend raster resolution (including page-image export) but is not a per-image default. DocumentMetadata.ImageQuality retains its legacy role as the Skia PDF encoding quality when no DocumentSettings override is present.

~~~csharp
var document = Document.Create(container => container.Page(page =>
{
    page.Size(190, 60);
    page.Margin(10);
    page.Content().Row(row =>
    {
        row.ConstantItem(80).Image("photo.jpg").WithRasterDpi(72);
        row.ConstantItem(10);
        row.ConstantItem(80).Image("photo.jpg").WithRasterDpi(144);
    });
}))
.WithSettings(new DocumentSettings
{
    ImageRasterDpi = 96,
    ImageCompressionQuality = 75
});
~~~

The two occurrences above use 72 DPI and 144 DPI respectively; both inherit quality 75. Removing an explicit DPI would make that occurrence use 96 DPI. The document settings are snapshotted per generation and never mutate caller metadata.

## Ownership and limits

- The library borrows the source SKImage passed through Infrastructure.Image, the image decoded from byte arrays, files and streams, and images returned by dynamic sources. The caller retains ownership of files, streams and Infrastructure.Image instances.
- Temporary resampled/encoded images are created per draw and disposed after the canvas consumes them. They are not cached between pages or occurrences.
- Reusing an image many times reprocesses it each time; pre-resize or pre-encode when a document places the same large image repeatedly.
- JPEG quality applies to RGB data; the source alpha channel is not flattened into a background. Use an opaque source when a smaller DCTDecode image and predictable output size are required.
- The controls affect raster images. SVG input remains FP-201 work.

## Example and acceptance evidence

Run the example from Source:

~~~powershell
dotnet test FossPDF.Examples/FossPDF.Examples.csproj -c Release --filter FullyQualifiedName~ImageOptimizationExamples
~~~

[ImageOptimizationExamples.cs](../../Source/FossPDF.Examples/ImageOptimizationExamples.cs) writes a PDF with independent 72/144 DPI occurrences and document-default settings.

IMG-202 is active in the [acceptance catalog](acceptance-fixtures.json). It renders one shared 800x400 quadrant image at 80x40 points with DPI 72 and 144 and qualities 40 and 90. Independent PDF inspection must find embedded 80x40 and 160x80 images with DCTDecode encoding; the decoded page must retain quadrant order and expected colors within lossy tolerances; the shared source must remain 800x400. ImageOptimizationTests covers resampling, the no-upscale cap, UseOriginalImage, quality differences, transparency preservation, descriptor validation, document defaults and source immutability.

See [the settings contract](SETTINGS.md) for document/output precedence and [the Windows evidence report](evidence/fp-202-windows.md) for the local test record.
