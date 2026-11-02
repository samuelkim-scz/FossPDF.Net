# PDF labels

`LabelDocument` creates fixed-slot label sheets and one-label-per-page PDFs. It is part of the
core package on both `netstandard2.1` and `net9.0`. Label content uses ordinary FossPDF containers,
text, images, and components. No additional package is needed.

## Avery 5160 address labels

```csharp
using FossPDF.Fluent;
using FossPDF.Helpers;
using FossPDF.Infrastructure;

var document = LabelDocument.Create(
    addresses,
    LabelStocks.Avery5160,
    (label, address) => label
        .Padding(2, Unit.Millimetre)
        .AlignMiddle()
        .Text(address.Formatted)
        .FontSize(10),
    new LabelOptions { SkipLabels = 5 });

document.GeneratePdf("address-labels.pdf");
```

The preset uses US Letter paper (8.5 x 11 inches), three columns and ten rows of 2.625 x 1 inch
labels. The left margin is 0.1875 inches, the top margin is 0.5 inches, the horizontal gap is
0.125 inches, and there is no vertical gap. The resulting right and bottom margins are
0.1875 and 0.5 inches. Avery 8160 uses the same template.

Sources: [Avery's 5160 template and compatibility list](https://www.avery.com/templates/5160),
and [Smith Corona's compatible stock dimensions](https://www.smithcorona.com/2-625x1-laser-sheet-labels.html).
These are nominal stock dimensions; test registration with the actual printer and stock.

## Reuse a partially used sheet

`SkipLabels` is the number of leading positions already used. It defaults to zero and applies
only to the first sheet. Labels fill left to right, then top to bottom; later sheets begin at
the first position. Skipped positions are empty and do not consume items or copies.

On Avery 5160, skipping five labels starts at slot 6: row 2, column 3. A job of 27 labels puts
25 on the first sheet and the remaining two in positions 1 and 2 of the next sheet.

Alternatively, specify one-based coordinates:

```csharp
new LabelOptions { StartRow = 2, StartColumn = 3 }
```

Specify both row and column. If `SkipLabels` is also supplied, it must describe the same slot.
Skip counts must be between zero and the sheet capacity minus one; a fully used sheet should
be replaced. Scattered unavailable positions and an interactive slot picker are future work.

`CopiesPerItem` repeats each item consecutively, including when its copies cross a sheet boundary:

```csharp
new LabelOptions { SkipLabels = 5, CopiesPerItem = 2 }
```

The input sequence is enumerated and copied once when creating the document. Re-generating the
document keeps the same starting position and item order. The item objects themselves are not
deep-copied, and content callbacks should be repeatable. Empty input produces zero pages and
no PDF bytes; applications should avoid offering a download for an empty job.

## Printer alignment and calibration

Physical calibration is independent of starting positions. Positive X shifts right and positive
Y shifts down; negative values shift left/up. Offsets apply to every sheet without resizing
labels or changing spacing. The entire shifted grid must remain inside the PDF page.

```csharp
var options = new LabelOptions
{
    SkipLabels = 5,
    OffsetX = -0.5f,
    OffsetY = 1,
    Unit = Unit.Millimetre
};

LabelDocument.CreateCalibration(LabelStocks.Avery5160, options)
    .GeneratePdf("label-calibration.pdf");
```

Calibration produces a single sheet with numbered outlines in the available positions and uses
the same geometry and offsets as the label job. It ignores `CopiesPerItem` after validating it.
Print on plain paper first and compare against the label sheet. Print using **100% / Actual Size**
and the matching paper size. Fit-to-page changes physical positions. See
[Avery's printing instructions](https://www.avery.com/help/article/printing-in-microsoft-edge).

## Thermal labels and custom stock

`LabelStocks.Thermal4x6` generates one 4 x 6 inch page per label. Use the same content callback;
the printer driver or PDF-capable printer handles delivery to the device.

```csharp
LabelDocument.Create(addresses, LabelStocks.Thermal4x6, RenderAddress)
    .GeneratePdf("thermal-labels.pdf");

var customStock = new LabelStock
{
    PageSize = new PageSize(100, 80, Unit.Millimetre),
    LabelWidth = 40,
    LabelHeight = 20,
    Columns = 2,
    Rows = 3,
    MarginLeft = 5,
    MarginTop = 5,
    HorizontalGap = 3,
    VerticalGap = 2,
    Unit = Unit.Millimetre
};
```

`LabelStock.Unit` governs its label dimensions, margins, and gaps. `PageSize` has its own unit
constructor and stores points. `LabelOptions.Unit` governs only printer offsets. Stock geometry
must be finite, positive where appropriate, and fit the page. Presets are immutable records;
use `with` expressions to derive custom stock.

Each label must fit its slot in full. Content that wraps or only partially fits throws
`DocumentLayoutException` identifying the one-based label number in the job. Add padding for
the label's safety area; keep borders and artwork inside the stock's actual die-cut shape.
Painting is clipped to each rectangular slot, including translated content and shadows, so
artwork cannot mark skipped or neighboring labels. Content extending beyond the slot is cropped.
Physical slots always fill left to right, regardless of the document's text-direction default.
Individual label content can set its own text direction.

## Verification

[`LabelDocumentTests`](../Source/FossPDF.UnitTests/LabelDocumentTests.cs) asserts physical positions,
unit conversions, pagination, copies, empty jobs, validation, RTL text and artwork isolation.
The Avery preset's 13.5/36-point left/top anchors and 198/72-point column/row pitches were also
cross-checked against Avery's official 5160 PDF template. The preset uses the marketed nominal
189-point label width; the template's drawn outline is approximately 189.36 points wide.

CI generates the examples on Windows, Linux and macOS and uses
[`verify_pdf_labels.py`](../Source/FossPDF.UnitTests/Parity/verify_pdf_labels.py) with the pinned
independent PDF readers to check page sizes, recipient order, text positions and the white
interiors of every skipped or unused label. The PDF samples and inspection report are included
in each platform's parity evidence artifact. Printer registration still requires calibration
with the actual printer and stock.

The result is a standard `Document`, so metadata, document settings, PDF generation, raster/SVG
export, and preview APIs remain available. The runnable
[`LabelExamples`](../Source/FossPDF.Examples/LabelExamples.cs) generates a two-sheet skip-five
sample, a calibration sheet, and a thermal sample. Set `FOSSPDF_LABEL_OUTPUT` to choose its directory.
