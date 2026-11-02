using System.IO;
using System.Linq;
using FossPDF.Fluent;
using FossPDF.Helpers;
using FossPDF.Infrastructure;
using NUnit.Framework;

namespace FossPDF.Examples
{
    public class LabelExamples
    {
        [Test]
        public void GenerateLabelSamples()
        {
            // Override the output directory when running this example from the command line.
            var output = System.Environment.GetEnvironmentVariable("FOSSPDF_LABEL_OUTPUT")
                ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "labels");
            Directory.CreateDirectory(output);
            var addresses = Enumerable.Range(1, 27).Select(index =>
                $"Sample Recipient {index:00}\n{100 + index} Example Street\nSample City, CA 95000").ToArray();

            LabelDocument.Create(addresses, LabelStocks.Avery5160, Address,
                    new LabelOptions { SkipLabels = 5 })
                .GeneratePdf(Path.Combine(output, "avery-5160-skip-5.pdf"));

            LabelDocument.CreateCalibration(LabelStocks.Avery5160)
                .GeneratePdf(Path.Combine(output, "avery-5160-calibration.pdf"));

            LabelDocument.Create(addresses.Take(2), LabelStocks.Thermal4x6, Address)
                .GeneratePdf(Path.Combine(output, "thermal-4x6.pdf"));
        }

        private static void Address(IContainer label, string address) =>
            label.Padding(2, Unit.Millimetre).AlignMiddle().Text(address).FontSize(10);
    }
}
