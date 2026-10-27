using System;
using PdfSharp.Fonts;

namespace FossPDF.NativePrototypes;

internal static class Program
{
    public static int Main(string[] args)
    {
        var outputDirectory = ParseOutputDirectory(args) ??
                              Path.Combine(PrototypePaths.RepositoryRoot, "artifacts", "fp-002");

        Directory.CreateDirectory(outputDirectory);

        // Set this before any PDFsharp font resolution. The resolver avoids system-font dependencies
        // on Linux and macOS and embeds the bundled Lato files.
        GlobalFontSettings.FontResolver = new LatoFontResolver(
            PrototypePaths.LatoRegular,
            PrototypePaths.LatoBold);

        var results = new List<PrototypeResult>
        {
            Run("svg-input", () => SvgPrototype.Run(outputDirectory)),
            Run("existing-pdf-operations", () => PdfOperationsPrototype.Run(outputDirectory)),
            Run("semantic-tagging", () => TaggingPrototype.Run(outputDirectory))
        };

        JsonFile.Write(Path.Combine(outputDirectory, "prototype-results.json"), results);

        foreach (var result in results)
        {
            Console.WriteLine($"{result.Passed.ToString().ToUpperInvariant()}: {result.Name}");

            foreach (var check in result.Checks)
                Console.WriteLine("  " + check);

            if (!string.IsNullOrWhiteSpace(result.Error))
                Console.WriteLine("  ERROR: " + result.Error);
        }

        Console.WriteLine(results.All(x => x.Passed)
            ? "PROTOTYPES: all checks passed."
            : "PROTOTYPES: one or more checks failed; inspect prototype-results.json.");

        return results.All(x => x.Passed) ? 0 : 1;
    }

    private static PrototypeResult Run(string name, Func<PrototypeResult> handler)
    {
        try
        {
            return handler();
        }
        catch (Exception exception)
        {
            return new PrototypeResult
            {
                Name = name,
                Passed = false,
                Error = exception.ToString()
            };
        }
    }

    private static string? ParseOutputDirectory(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--output" && index + 1 < args.Length)
                return Path.GetFullPath(args[index + 1]);
        }

        return null;
    }
}
