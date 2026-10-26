using System;
using System.Globalization;
using FossPDF.Drawing;
using FossPDF.Elements.Table;
using FossPDF.Fluent;
using FossPDF.Helpers;
using FossPDF.Infrastructure;
using SkiaSharp;
using SharedImage = FossPDF.Infrastructure.Image;

namespace FossPDF.Quality;

internal static class DocumentScenarios
{
    private static readonly DateTime FixedDate = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private const string LongParagraph =
        "FossPDF composes documents from simple layout elements and computes pagination while drawing. " +
        "This deterministic paragraph exercises line breaking, glyph shaping, kerning, and text measurement. " +
        "It deliberately contains enough words to wrap across multiple lines without depending on random input. " +
        "The same text is rendered on every platform so visual and semantic metrics remain comparable.";

    public static Document CreateReportDocument()
    {
        return CreateDocument(page =>
        {
            page.Header()
                .Height(36)
                .Background("#1f4e79")
                .PaddingHorizontal(12)
                .AlignMiddle()
                .Text("FossPDF quality baseline")
                .FontColor("#ffffff")
                .FontSize(16)
                .SemiBold();

            page.Content().PaddingVertical(12).Column(column =>
            {
                column.Spacing(8);

                column.Item().Text("Quarterly operations summary").FontSize(20).Bold();
                column.Item()
                    .Text("A fixed report that exercises headers, footers, tables, typography and page numbering.")
                    .FontSize(10);

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                    });

                    table.Header(header =>
                    {
                        HeaderCell(header.Cell(), "Item");
                        HeaderCell(header.Cell(), "Region");
                        HeaderCell(header.Cell(), "Units");
                        HeaderCell(header.Cell(), "Amount");
                    });

                    for (var index = 0; index < 12; index++)
                    {
                        var background = index % 2 == 0 ? "#ffffff" : "#f3f6fa";

                        table.Cell().Background(background).Padding(4).Text($"Item {index + 1:D2}");
                        table.Cell().Background(background).Padding(4).Text(
                            index % 3 == 0 ? "North" : index % 3 == 1 ? "Central" : "South");
                        table.Cell().Background(background).Padding(4).AlignRight()
                            .Text((index * 7 + 3).ToString(CultureInfo.InvariantCulture));
                        table.Cell().Background(background).Padding(4).AlignRight()
                            .Text(((index + 1) * 125.50m).ToString("0.00", CultureInfo.InvariantCulture));
                    }
                });

                column.Item().Text(LongParagraph).FontSize(10);
            });

            page.Footer().Height(24).PaddingTop(6).AlignRight().Text(text =>
            {
                text.Span("Page ").FontSize(9).FontColor("#666666");
                text.CurrentPageNumber();
                text.Span(" of ").FontSize(9).FontColor("#666666");
                text.TotalPages();
            });
        });
    }

    public static Document CreateTextDocument()
    {
        return CreateDocument(page =>
        {
            page.Content().Column(column =>
            {
                column.Spacing(10);

                column.Item().Text("Typography baseline").FontSize(24).Bold();

                column.Item().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(11));
                    text.Span("Regular text with ");
                    text.Span("bold").Bold();
                    text.Span(", ");
                    text.Span("italic").Italic();
                    text.Span(", ");
                    text.Span("underlined").Underline();
                    text.Span(", and ");
                    text.Span("colored").FontColor("#c00000");
                    text.Span(" runs. ");
                    text.Span("Superscript").Superscript();
                    text.Span(" and ");
                    text.Span("subscript").Subscript();
                    text.Span(" exercise baseline positioning.");
                });

                column.Item().Text(LongParagraph).FontSize(10);
                column.Item().Text(LongParagraph).FontSize(8);
                column.Item().Text(LongParagraph).FontSize(18);

                column.Item().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(11).LineHeight(1.4f));
                    text.Span(LongParagraph);
                });
            });
        });
    }

    public static Document CreateGraphicsDocument()
    {
        return CreateDocument(page =>
        {
            page.Content().Column(column =>
            {
                column.Spacing(10);

                column.Item().Text("Graphics baseline").FontSize(20).Bold();

                column.Item().Row(row =>
                {
                    row.Spacing(6);
                    row.RelativeItem().Height(40).Background("#d32f2f");
                    row.RelativeItem().Height(40).Background("#388e3c");
                    row.RelativeItem().Height(40).Background("#1976d2");
                    row.RelativeItem().Height(40).Background("#f9a825");
                });

                column.Item()
                    .Height(70)
                    .Background("#e8f0fe")
                    .Border(1)
                    .BorderColor("#1f4e79")
                    .Padding(8)
                    .Text("A bordered, padded panel with a fixed palette.")
                    .FontColor("#1f4e79")
                    .FontSize(12);

                column.Item().LineHorizontal(1).LineColor("#1f4e79");

                column.Item().Column(inner =>
                {
                    for (var rowIndex = 0; rowIndex < 4; rowIndex++)
                    {
                        inner.Item().Row(row =>
                        {
                            row.Spacing(4);

                            for (var columnIndex = 0; columnIndex < 8; columnIndex++)
                            {
                                var shade = 32 * ((rowIndex + columnIndex) % 7);
                                var color = $"#{shade:X2}{shade:X2}{shade:X2}";

                                row.RelativeItem()
                                    .Height(22)
                                    .Background(color)
                                    .Border(0.5f)
                                    .BorderColor("#888888");
                            }
                        });
                    }
                });
            });
        });
    }

    public static Document CreateImageDocument(SharedImage image)
    {
        return CreateDocument(page =>
        {
            page.Content().Column(column =>
            {
                column.Spacing(10);
                column.Item().Text("Image baseline").FontSize(20).Bold();
                column.Item().Text("Deterministic quadrant image at a fixed display size.").FontSize(10);

                column.Item().Row(row =>
                {
                    row.Spacing(10);
                    row.RelativeItem().Width(200).Image(image);
                    row.RelativeItem().Width(200).Image(image);
                });

                column.Item().Text(LongParagraph).FontSize(10);
            });
        });
    }

    public static Document CreateLargeTableDocument()
    {
        return CreateDocument(page =>
        {
            page.Margin(20);
            page.DefaultTextStyle(style => style.FontFamily(Fonts.Lato).FontSize(6).FontColor("#202020"));

            page.Content().Table(table =>
            {
                const int columnCount = 8;
                const int rowCount = 500;

                table.ColumnsDefinition(columns =>
                {
                    for (var index = 0; index < columnCount; index++)
                        columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    for (var index = 0; index < columnCount; index++)
                    {
                        header.Cell()
                            .Background("#d9e2f3")
                            .Padding(2)
                            .Text($"H{index + 1}")
                            .SemiBold();
                    }
                });

                for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
                {
                    var background = rowIndex % 2 == 0 ? "#ffffff" : "#f3f6fa";

                    for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
                    {
                        table.Cell()
                            .Background(background)
                            .Padding(2)
                            .Text($"{rowIndex}-{columnIndex}");
                    }
                }
            });
        });
    }

    public static Document CreateConformanceDocument() => CreateReportDocument();

    public static byte[] CreateQuadrantImagePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { IsAntialias = false };

        canvas.Clear(SKColors.White);
        Fill(0, 0, width / 2, height / 2, "#d32f2f");
        Fill(width / 2, 0, width - width / 2, height / 2, "#388e3c");
        Fill(0, height / 2, width / 2, height - height / 2, "#1976d2");
        Fill(width / 2, height / 2, width - width / 2, height - height / 2, "#f9a825");

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();

        void Fill(int x, int y, int w, int h, string color)
        {
            paint.Color = SKColor.Parse(color);
            canvas.DrawRect(x, y, w, h, paint);
        }
    }

    private static void HeaderCell(ITableCellContainer cell, string text)
    {
        cell.Background("#d9e2f3").Padding(4).Text(text).SemiBold();
    }

    private static Document CreateDocument(Action<PageDescriptor> content)
    {
        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.PageColor("#ffffff");
            page.DefaultTextStyle(style => style.FontFamily(Fonts.Lato).FontSize(10).FontColor("#202020"));
            content(page);
        })).WithMetadata(new DocumentMetadata
        {
            Title = "FossPDF quality baseline",
            Author = "FossPDF contributors",
            Subject = "Deterministic visual and performance baseline document",
            Creator = "FossPDF.Quality",
            Producer = "FossPDF",
            CreationDate = FixedDate,
            ModifiedDate = FixedDate,
            RasterDpi = 72
        });
    }
}
