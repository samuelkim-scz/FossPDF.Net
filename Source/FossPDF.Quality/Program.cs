using System;
using System.IO;

namespace FossPDF.Quality;

internal static class Program
{
    public static int Main(string[] args)
    {
        QualityOptions options;

        try
        {
            options = QualityOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(QualityOptions.Usage);
            return 2;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(QualityOptions.Usage);
            return 0;
        }

        Directory.CreateDirectory(options.OutputDirectory);

        var passed = true;

        if (options.Mode is QualityMode.Environment or QualityMode.All)
        {
            var environment = QualityEnvironment.Capture();
            environment.Write(options.OutputDirectory);

            Console.WriteLine(
                $"ENVIRONMENT: {environment.RuntimeIdentifier} | {environment.Framework} | " +
                $"FossPDF {environment.FossPdfVersion} | SkiaSharp {environment.SkiaSharpVersion} | " +
                $"HarfBuzzSharp {environment.HarfBuzzSharpVersion}");
            Console.WriteLine($"ENVIRONMENT: {environment.FontSha256.Count} embedded Lato font(s) hashed.");
        }

        if (options.Mode is QualityMode.Visual or QualityMode.All)
            passed &= VisualBaselineRunner.Run(options);

        if (options.Mode is QualityMode.Performance or QualityMode.All)
            passed &= PerformanceRunner.Run(options);

        if (options.Mode is QualityMode.Conformance or QualityMode.All)
            passed &= ConformanceRunner.Run(options);

        Console.WriteLine(passed
            ? "QUALITY: all requested checks passed."
            : "QUALITY: one or more checks failed; inspect the artifacts.");

        return passed ? 0 : 1;
    }
}
