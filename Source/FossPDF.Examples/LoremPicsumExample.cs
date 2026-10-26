using System.Net.Http;
using NUnit.Framework;
using FossPDF.Examples.Engine;
using FossPDF.Fluent;
using FossPDF.Infrastructure;

namespace FossPDF.Examples
{
    public class LoremPicsum : IComponent
    {
        public bool Greyscale { get; }

        public LoremPicsum(bool greyscale)
        {
            Greyscale = greyscale;
        }
        
        public void Compose(IContainer container)
        {
            var url = "https://picsum.photos/300/200";

            if (Greyscale)
                url += "?grayscale";

            using var client = new HttpClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("user-agent", "FossPDF/1.0 Unit Testing");
            
            var response = client.GetByteArrayAsync(url).GetAwaiter().GetResult();
            container.Image(response);
        }
    }
    
    public class LoremPicsumExample
    {
        [Test]
        public void LoremPicsum()
        {
            RenderingTest
                .Create()
                .PageSize(350, 280)
                .ProducePdf()
                .ShowResults()
                .Render(container =>
                {
                    container
                        .Background("#FFF")
                        .Padding(25)
                        .Column(column =>
                        {
                            column.Spacing(10);

                            column
                                .Item()
                                .Component(new LoremPicsum(true));
                    
                            column
                                .Item()
                                .AlignRight()
                                .Text("From Lorem Picsum");
                        });
                });
        }
    }
}
