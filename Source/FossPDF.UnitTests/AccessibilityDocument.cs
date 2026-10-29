#if NET9_0
using System;
using FossPDF.Drawing;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using SkiaSharp;

namespace FossPDF.UnitTests
{
    internal static class AccessibilityDocument
    {
        internal static Document Create(PdfAConformance profile) => Document.Create(container => container.Page(page =>
        {
            page.Size(420, 600); page.Margin(24); page.DefaultTextStyle(x=>x.FontSize(12));
            page.Header().Text("Quarterly service report - repeated header");
            page.Footer().Text("Accessibility review fixture - repeated footer");
            page.Content().Column(column =>
            {
                column.Spacing(8);
                column.Item().SemanticHeading1().Text("Quarterly service report");
                column.Item().Text("This report describes completed requests and requests awaiting a response. Reading order follows this text, key results, the table, the chart and the links.");
                column.Item().SemanticHeading2().Text("Key results");
                column.Item().SemanticList().Column(list =>
                {
                    foreach(var text in new[]{"Twelve requests were completed.","Four requests await a response."})
                        list.Item().SemanticListItem().Row(row =>
                        {
                            row.ConstantItem(20).SemanticListLabel().Text("- ");
                            row.RelativeItem().SemanticListItemBody().Text(text);
                        });
                });
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns=>{columns.RelativeColumn();columns.RelativeColumn();});
                    table.Header(header=>{header.Cell().Text("Request status");header.Cell().Text("Count");});
                    table.Cell().Text("Completed");table.Cell().Text("12");
                    table.Cell().Text("Awaiting response");table.Cell().Text("4");
                });
                column.Item().SemanticFigure("Bar chart: twelve requests completed and four requests awaiting a response.").Height(45).Canvas((canvas,size)=>
                {
                    using var blue=new SKPaint {Color=SKColors.Blue}; using var red=new SKPaint {Color=SKColors.Red};
                    canvas.DrawRect(0,0,240,18,blue); canvas.DrawRect(0,24,80,18,red);
                });
                column.Item().SemanticLink("Read the project documentation").Hyperlink("https://example.org/documentation").Text("Read the project documentation");
                column.Item().Text(text=>{text.Span("Inline link: ");text.Hyperlink("Reference site","https://example.org/reference");});
                column.Item().SemanticLink("Jump to the details section").SectionLink("details").Text("Jump to details on the next page");
                column.Item().PageBreak();
                column.Item().Section("details").SemanticHeading2().Text("Details and language changes");
                column.Item().Text("The first page contains a list, a table, a chart and links. This paragraph follows the details heading on page two. Repeated headers and footers are decorative.");
                column.Item().SemanticLanguage("fr-FR").Text("Texte en fran\u00e7ais : caf\u00e9 et r\u00e9sum\u00e9.");
                column.Item().Text("End of the review fixture.");
            });
        })).WithMetadata(new DocumentMetadata
        {
            Title="Quarterly service report - accessibility review",Language="en-US",Author="FossPDF",
            CreationDate=new DateTime(2026,10,1,0,0,0,DateTimeKind.Utc),ModifiedDate=new DateTime(2026,10,1,0,0,0,DateTimeKind.Utc)
        }).WithSettings(new DocumentSettings {PdfUaConformance=PdfUaConformance.PdfUa1,PdfAConformance=profile});

    }
}
#endif
