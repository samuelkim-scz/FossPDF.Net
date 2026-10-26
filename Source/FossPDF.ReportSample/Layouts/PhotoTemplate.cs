using System;
using FossPDF.Fluent;
using FossPDF.Helpers;
using FossPDF.Infrastructure;

namespace FossPDF.ReportSample.Layouts
{
    public class PhotoTemplate : IComponent
    {
        public ReportPhoto Model { get; set; }

        public PhotoTemplate(ReportPhoto model)
        {
            Model = model;
        }
        
        public void Compose(IContainer container)
        {
            container
                .ShowEntire()
                .Column(column =>
                {
                    column.Spacing(5);
                    column.Item().Element(PhotoWithMaps);
                    column.Item().Element(PhotoDetails);
                });
        }
        
        void PhotoWithMaps(IContainer container)
        {
            container
                .Row(row =>
                {
                    row.RelativeItem(2).AspectRatio(4 / 3f).Component<ImagePlaceholder>();

                    row.RelativeItem().PaddingLeft(5).Column(column =>
                    {
                        column.Spacing(7f);
                        
                        column.Item().AspectRatio(4 / 3f).Component<ImagePlaceholder>();
                        column.Item().AspectRatio(4 / 3f).Component<ImagePlaceholder>();
                    });
                });
        }

        void PhotoDetails(IContainer container)
        {
            container.Border(0.75f).BorderColor(Colors.Grey.Medium).Table(table =>
            {
                table.ColumnsDefinition(definition =>
                {
                    for (var columnIndex = 0; columnIndex < 6; columnIndex++)
                        definition.RelativeColumn();
                });

                table.Cell().LabelCell().Text("Date");
                table.Cell().ColumnSpan(2).ValueCell().Text(Model.Date?.ToString("g") ?? string.Empty);
                table.Cell().LabelCell().Text("Location");
                table.Cell().ColumnSpan(2).ValueCell().Text(Model.Location.Format());
                
                table.Cell().LabelCell().Text("Comments");
                table.Cell().ColumnSpan(5).ValueCell().Text(Model.Comments);
            });
        }
    }
}
