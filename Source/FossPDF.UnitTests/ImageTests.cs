using NUnit.Framework;
using FossPDF.Drawing;
using FossPDF.Elements;
using FossPDF.Fluent;
using FossPDF.Infrastructure;
using FossPDF.UnitTests.TestEngine;
using SkiaSharp;

namespace FossPDF.UnitTests
{
    [TestFixture]
    public class ImageTests
    {
        [Test]
        public void Measure_TakesAvailableSpaceRegardlessOfSize()
        {
            using var image = GenerateImage(400, 300);
            TestPlan
                .For(x => new Elements.Image
                {
                    InternalImage = image
                })
                .MeasureElement(new Size(300, 200))
                .CheckMeasureResult(SpacePlan.FullRender(300, 200));
        }
        
        [Test]
        public void Draw_TakesAvailableSpaceRegardlessOfSize()
        {
            using var image = GenerateImage(400, 300);
            TestPlan
                .For(x => new Elements.Image
                {
                    InternalImage = image
                })
                .DrawElement(new Size(300, 200))
                .ExpectCanvasDrawImage(new Position(0, 0), new Size(300, 200))
                .CheckDrawResult();
        }
        
        [Test]
        public void Fluent_RecognizesImageProportions()
        {
            using var skImage = GenerateImage(600, 200);
            using var imageData = skImage.Encode(SKEncodedImageFormat.Png, 100);
            var image = imageData.ToArray();
            
            TestPlan
                .For(x =>
                {
                    var container = new Container();
                    container.Image(image);
                    return container;
                })
                .MeasureElement(new Size(300, 200))
                .CheckMeasureResult(SpacePlan.FullRender(300, 100));;
        }
        
        SKImage GenerateImage(int width, int height)
        {
            var imageInfo = new SKImageInfo(width, height);
            using var surface = SKSurface.Create(imageInfo);
            return surface.Snapshot();
        }
    }
}
