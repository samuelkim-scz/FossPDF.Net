using System;
using System.IO;
using NUnit.Framework;
using FossPDF.Fluent;
using FossPDF.ReportSample.Layouts;

namespace FossPDF.ReportSample
{
    public class ReportGeneration
    {
        private StandardReport Report { get; set; }
        
        [SetUp]
        public void SetUp()
        {
            var model = DataSource.GetReport();
            Report = new StandardReport(model);
        }
        
        [Test] 
        public void GenerateAndShowPdf()
        {
            //ImagePlaceholder.Solid = true;
        
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"test_result.pdf");
            Report.GeneratePdf(path);
            PlatformUtils.PlatformUtils.OpenFile(path);
        }
        
        [Test] 
        public void GenerateAndShowXps()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"test_result.xps");
            Report.GenerateXps(path);
            PlatformUtils.PlatformUtils.OpenFile(path);
        }
    }
}
