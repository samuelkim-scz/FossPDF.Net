using System;
using System.IO;

namespace FossPDF.Quality;

internal enum QualityMode
{
    Environment,
    Visual,
    Performance,
    Conformance,
    All
}

internal sealed class QualityOptions
{
    public QualityMode Mode { get; private init; } = QualityMode.All;
    public string RepositoryRoot { get; private init; } = string.Empty;
    public string OutputDirectory { get; private init; } = string.Empty;
    public string BaselineDirectory { get; private init; } = string.Empty;
    public bool UpdateVisualBaseline { get; private init; }
    public bool UpdatePerformanceBudgets { get; private init; }
    public bool EnforceBudgets { get; private init; } = true;
    public bool ShowHelp { get; private init; }

    public static QualityOptions Parse(string[] args)
    {
        var repositoryRoot = FindRepositoryRoot();
        var mode = QualityMode.All;
        string? output = null;
        string? baseline = null;
        var updateVisual = false;
        var updateBudgets = false;
        var enforceBudgets = true;
        var showHelp = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            switch (argument)
            {
                case "--mode":
                    mode = ParseMode(NextValue(args, ref index, argument));
                    break;
                case "--output":
                    output = NextValue(args, ref index, argument);
                    break;
                case "--baseline-dir":
                    baseline = NextValue(args, ref index, argument);
                    break;
                case "--update-visual-baseline":
                    updateVisual = true;
                    break;
                case "--update-performance-budgets":
                    updateBudgets = true;
                    break;
                case "--enforce-budgets":
                    enforceBudgets = true;
                    break;
                case "--no-enforce-budgets":
                    enforceBudgets = false;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{argument}'. Use --help for usage.");
            }
        }

        if (showHelp)
        {
            return new QualityOptions
            {
                RepositoryRoot = repositoryRoot,
                ShowHelp = true
            };
        }

        var outputDirectory = output == null
            ? Path.Combine(repositoryRoot, "artifacts", "quality")
            : Path.GetFullPath(output);

        var baselineDirectory = baseline == null
            ? Path.Combine(repositoryRoot, "docs", "feature-parity", "baselines")
            : Path.GetFullPath(baseline);

        return new QualityOptions
        {
            Mode = mode,
            RepositoryRoot = repositoryRoot,
            OutputDirectory = outputDirectory,
            BaselineDirectory = baselineDirectory,
            UpdateVisualBaseline = updateVisual,
            UpdatePerformanceBudgets = updateBudgets,
            EnforceBudgets = enforceBudgets
        };
    }

    public static string Usage =>
        """
        FossPDF quality baseline tool

          --mode <environment|visual|performance|conformance|all>   default: all
          --output <directory>                                      default: artifacts/quality
          --baseline-dir <directory>                                default: docs/feature-parity/baselines
          --update-visual-baseline                                  write visual-baselines.json from this run
          --update-performance-budgets                              write performance-budgets.json from this run
          --enforce-budgets                                         fail when a performance budget is exceeded (default)
          --no-enforce-budgets                                      record budget comparisons without failing
          --help                                                    show this help

        Exit codes: 0 = pass, 1 = baseline/budget failure, 2 = invalid arguments.
        """;

    private static string NextValue(string[] args, ref int index, string argument)
    {
        index++;

        if (index >= args.Length)
            throw new ArgumentException($"Missing value for '{argument}'.");

        return args[index];
    }

    private static QualityMode ParseMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "environment" => QualityMode.Environment,
            "visual" => QualityMode.Visual,
            "performance" => QualityMode.Performance,
            "conformance" => QualityMode.Conformance,
            "all" => QualityMode.All,
            _ => throw new ArgumentException(
                $"Unknown mode '{value}'. Use environment, visual, performance, conformance or all.")
        };
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);

            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "FEATURE_PARITY.md")))
                    return directory.FullName;

                directory = directory.Parent;
            }
        }

        return Directory.GetCurrentDirectory();
    }
}
