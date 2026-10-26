using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using FossPDF.Fluent;

namespace FossPDF.Quality;

internal sealed class ConformanceToolResult
{
    public string Name { get; init; } = string.Empty;
    public bool Available { get; init; }
    public string Version { get; init; } = string.Empty;
    public int? ExitCode { get; init; }
    public bool? Passed { get; init; }
    public string Output { get; init; } = string.Empty;
}

internal sealed class ConformanceToolPin
{
    public string MinimumVersion { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public string Availability { get; init; } = string.Empty;
}

internal sealed class ConformanceToolPolicy
{
    public int SchemaVersion { get; init; }
    public string VerifiedOn { get; init; } = string.Empty;
    public Dictionary<string, ConformanceToolPin> Tools { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> CurrentClaims { get; init; } = new(StringComparer.Ordinal);
    public List<string> Notes { get; init; } = new();
}

internal sealed class ConformanceResult
{
    public int SchemaVersion { get; init; } = 1;
    public QualityEnvironment Environment { get; init; } = new();
    public string Document { get; init; } = string.Empty;
    public string PdfAConformance { get; init; } = "None";
    public string PdfUaConformance { get; init; } = "None";
    public string Claims { get; init; } =
        "No PDF/A or PDF/UA conformance claim. Selectable profiles require FP-402 and FP-403.";
    public bool Passed { get; init; }
    public ConformanceToolPolicy? Policy { get; init; }
    public List<ConformanceToolResult> Tools { get; init; } = new();
    public List<string> Notes { get; init; } = new();
}

internal static class ConformanceRunner
{
    public static bool Run(QualityOptions options)
    {
        var environment = QualityEnvironment.Capture();
        var outputDirectory = Path.Combine(options.OutputDirectory, "conformance");
        Directory.CreateDirectory(outputDirectory);

        var documentPath = Path.Combine(outputDirectory, "document.pdf");
        var pdf = DocumentScenarios.CreateConformanceDocument().GeneratePdf(out _);
        File.WriteAllBytes(documentPath, pdf);

        var tools = new List<ConformanceToolResult>
        {
            RunStructuralCheck(documentPath),
            RunAccessibilityCheck(documentPath)
        };

        var notes = new List<string>
        {
            "qpdf performs a structural PDF check; it does not validate PDF/A or PDF/UA profiles.",
            "veraPDF output is recorded for the pinned baseline but is not a conformance claim.",
            "The legacy DocumentMetadata.PdfA flag is not a validated PDF/A profile.",
            "PDF/A-2/PDF/A-3 and PDF/UA-1 remain blocked by FP-402 and FP-403."
        };

        var qpdf = tools[0];
        var passed = !qpdf.Available || qpdf.Passed != false;
        var policy = JsonFile.Read<ConformanceToolPolicy>(
            Path.Combine(options.BaselineDirectory, "conformance-tools.json"));

        JsonFile.Write(Path.Combine(outputDirectory, "conformance.json"), new ConformanceResult
        {
            Environment = environment,
            Document = documentPath,
            Passed = passed,
            Policy = policy,
            Tools = tools,
            Notes = notes
        });

        foreach (var tool in tools)
        {
            Console.WriteLine(tool.Available
                ? $"CONFORMANCE: {tool.Name} {tool.Version}; check {(tool.Passed == true ? "passed" : tool.Passed == false ? "failed" : "recorded")}."
                : $"CONFORMANCE: {tool.Name} not available; skipped.");
        }

        return passed;
    }

    private static ConformanceToolResult RunStructuralCheck(string documentPath)
    {
        var version = RunProcess("qpdf", "--version");

        if (!version.Available)
            return new ConformanceToolResult { Name = "qpdf" };

        var check = RunProcess("qpdf", "--check", documentPath);

        return new ConformanceToolResult
        {
            Name = "qpdf",
            Available = true,
            Version = FirstLine(version.Output),
            ExitCode = check.ExitCode,
            // qpdf uses 3 for warnings and 2 for errors; warnings do not indicate a broken document.
            Passed = check.ExitCode is 0 or 3,
            Output = check.Output
        };
    }

    private static ConformanceToolResult RunAccessibilityCheck(string documentPath)
    {
        var version = RunProcess("verapdf", "--version");

        if (!version.Available)
            return new ConformanceToolResult { Name = "verapdf" };

        var check = RunProcess("verapdf", "--format", "text", documentPath);

        return new ConformanceToolResult
        {
            Name = "verapdf",
            Available = true,
            Version = FirstLine(version.Output),
            ExitCode = check.ExitCode,
            Passed = null,
            Output = check.Output
        };
    }

    private static (bool Available, int? ExitCode, string Output) RunProcess(
        string fileName,
        params string[] arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);

            process.Start();

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                return (true, null, Trim($"Timed out after 30 seconds.{Environment.NewLine}{output}{Environment.NewLine}{error}"));
            }

            return (true, process.ExitCode, Trim($"{output}{Environment.NewLine}{error}"));
        }
        catch (Exception exception) when (exception is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            return (false, null, string.Empty);
        }
    }

    private static string FirstLine(string value)
    {
        var normalized = value.Trim();
        var newLine = normalized.IndexOfAny(new[] { '\r', '\n' });

        return newLine < 0 ? normalized : normalized[..newLine].Trim();
    }

    private static string Trim(string value)
    {
        const int limit = 4000;
        var normalized = value.Trim();

        return normalized.Length <= limit
            ? normalized
            : normalized[..limit] + Environment.NewLine + "[output truncated]";
    }
}
