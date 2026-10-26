using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FossPDF.Fluent;
using SharedImage = FossPDF.Infrastructure.Image;

namespace FossPDF.Quality;

internal sealed class PerformanceScenarioResult
{
    public string Id { get; init; } = string.Empty;
    public int Documents { get; init; }
    public int TotalPages { get; init; }
    public double TotalElapsedMs { get; init; }
    public double MedianElapsedMs { get; init; }
    public double PagesPerSecond { get; init; }
    public double AllocatedBytesPerDocument { get; init; }
    public long PeakManagedHeapBytes { get; init; }
    public long? PeakWorkingSetBytes { get; init; }
    public long? WorkingSetDeltaBytes { get; init; }
    public double AverageDocumentBytes { get; init; }
    public int MaxDegreeOfParallelism { get; init; }
    public List<string> BudgetFailures { get; init; } = new();
}

internal sealed class PerformanceResult
{
    public int SchemaVersion { get; init; } = 1;
    public QualityEnvironment Environment { get; init; } = new();
    public string BudgetFile { get; init; } = string.Empty;
    public bool Enforced { get; init; }
    public bool Passed { get; init; }
    public List<string> Failures { get; init; } = new();
    public List<PerformanceScenarioResult> Scenarios { get; init; } = new();
}

internal sealed class PerformanceBudgets
{
    public int SchemaVersion { get; set; } = 1;
    public string MeasuredOn { get; set; } = string.Empty;
    public string Policy { get; set; } =
        "Elapsed budgets are 8x and allocation budgets are 2x the measured baseline. " +
        "The working-set budget is the larger of 1 GiB or 2x the measured peak. " +
        "These are CI sanity ceilings, not service-level objectives; tighten them only with reviewed evidence.";
    public Dictionary<string, PerformanceBudget> Scenarios { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class PerformanceBudget
{
    public double MaxMedianElapsedMs { get; set; }
    public double MaxAllocatedBytesPerDocument { get; set; }
    public long MaxPeakWorkingSetBytes { get; set; }
}

internal static class PerformanceRunner
{
    private const string BudgetFileName = "performance-budgets.json";

    public static bool Run(QualityOptions options)
    {
        var environment = QualityEnvironment.Capture();
        var outputDirectory = Path.Combine(options.OutputDirectory, "performance");
        Directory.CreateDirectory(outputDirectory);

        using var image = SharedImage.FromBinaryData(DocumentScenarios.CreateQuadrantImagePng(200, 150));

        var definitions = new (string Id, Func<Document> Factory, int Documents, int Parallelism)[]
        {
            ("report", DocumentScenarios.CreateReportDocument, 8, 1),
            ("image-heavy", () => DocumentScenarios.CreateImageDocument(image), 5, 1),
            ("large-table", DocumentScenarios.CreateLargeTableDocument, 2, 1),
            ("concurrent", DocumentScenarios.CreateReportDocument, 8, Math.Min(4, Environment.ProcessorCount))
        };

        var results = definitions
            .Select(x => Measure(x.Id, x.Factory, x.Documents, x.Parallelism))
            .ToList();

        var budgetPath = Path.Combine(options.BaselineDirectory, BudgetFileName);
        var budgets = JsonFile.Read<PerformanceBudgets>(budgetPath);
        var failures = new List<string>();

        if (options.UpdatePerformanceBudgets)
        {
            budgets = CreateBudgets(environment, results);
            JsonFile.Write(budgetPath, budgets);
            Console.WriteLine($"PERFORMANCE: budgets written to {budgetPath}");
        }

        if (budgets == null)
        {
            failures.Add(
                $"Performance budget file not found at {budgetPath}. " +
                "Run with --update-performance-budgets on a reference platform and commit the result.");
        }
        else
        {
            foreach (var result in results)
            {
                if (!budgets.Scenarios.TryGetValue(result.Id, out var budget))
                {
                    failures.Add($"{result.Id}: budget entry is missing.");
                    continue;
                }

                var scenarioFailures = Evaluate(result, budget);
                result.BudgetFailures.AddRange(scenarioFailures);
                failures.AddRange(scenarioFailures.Select(x => $"{result.Id}: {x}"));
            }
        }

        foreach (var result in results)
        {
            Console.WriteLine(
                $"PERFORMANCE: {result.Id} -> {result.Documents} doc(s), {result.MedianElapsedMs:F1} ms/doc, " +
                $"{result.AllocatedBytesPerDocument / 1024 / 1024:F1} MiB alloc/doc, " +
                $"peak managed {result.PeakManagedHeapBytes / 1024 / 1024:F1} MiB, " +
                $"peak working set {FormatBytes(result.PeakWorkingSetBytes)}.");
        }

        foreach (var failure in failures)
            Console.Error.WriteLine("PERFORMANCE: " + failure);

        var passed = !options.EnforceBudgets || failures.Count == 0;

        JsonFile.Write(Path.Combine(outputDirectory, "performance.json"), new PerformanceResult
        {
            Environment = environment,
            BudgetFile = budgetPath,
            Enforced = options.EnforceBudgets,
            Passed = passed,
            Failures = failures,
            Scenarios = results
        });

        Console.WriteLine(passed
            ? $"PERFORMANCE: passed for {environment.RuntimeIdentifier}."
            : $"PERFORMANCE: failed for {environment.RuntimeIdentifier}: {failures.Count} issue(s).");

        return passed;
    }

    private static PerformanceScenarioResult Measure(
        string id,
        Func<Document> factory,
        int documents,
        int maxDegreeOfParallelism)
    {
        // Warm up caches and native font state without including it in the measurement.
        factory().GeneratePdf();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var process = Process.GetCurrentProcess();
        process.Refresh();

        var workingSetBefore = TryGetWorkingSet(process);
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var peakManaged = 0L;
        var documentSizes = new ConcurrentBag<long>();
        var pageCounts = new ConcurrentBag<int>();
        var perDocumentMs = new ConcurrentBag<double>();

        var stopwatch = Stopwatch.StartNew();

        if (maxDegreeOfParallelism <= 1)
        {
            for (var index = 0; index < documents; index++)
            {
                var documentStopwatch = Stopwatch.StartNew();
                var pdf = factory().GeneratePdf(out var pageCount);
                documentStopwatch.Stop();

                perDocumentMs.Add(documentStopwatch.Elapsed.TotalMilliseconds);
                documentSizes.Add(pdf.Length);
                pageCounts.Add(pageCount);

                peakManaged = Math.Max(peakManaged, GC.GetGCMemoryInfo().HeapSizeBytes);
            }
        }
        else
        {
            Parallel.For(
                0,
                documents,
                new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                _ =>
                {
                    var documentStopwatch = Stopwatch.StartNew();
                    var pdf = factory().GeneratePdf(out var pageCount);
                    documentStopwatch.Stop();

                    perDocumentMs.Add(documentStopwatch.Elapsed.TotalMilliseconds);
                    documentSizes.Add(pdf.Length);
                    pageCounts.Add(pageCount);

                    InterlockedMax(ref peakManaged, GC.GetGCMemoryInfo().HeapSizeBytes);
                });
        }

        stopwatch.Stop();

        var allocatedAfter = GC.GetTotalAllocatedBytes(true);
        process.Refresh();

        var totalPages = pageCounts.Sum();
        var totalElapsedMs = stopwatch.Elapsed.TotalMilliseconds;
        var ordered = perDocumentMs.OrderBy(x => x).ToArray();

        var median = ordered.Length == 0
            ? 0
            : ordered.Length % 2 == 1
                ? ordered[ordered.Length / 2]
                : (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2;

        var peakWorkingSetValue = TryGetPeakWorkingSet(process) ?? 0;
        var workingSetAfter = TryGetWorkingSet(process);

        return new PerformanceScenarioResult
        {
            Id = id,
            Documents = documents,
            TotalPages = totalPages,
            TotalElapsedMs = totalElapsedMs,
            MedianElapsedMs = maxDegreeOfParallelism <= 1 ? median : totalElapsedMs / documents,
            PagesPerSecond = totalElapsedMs <= 0 ? 0 : totalPages / (totalElapsedMs / 1000.0),
            AllocatedBytesPerDocument = documents == 0 ? 0 : (allocatedAfter - allocatedBefore) / (double) documents,
            PeakManagedHeapBytes = peakManaged,
            PeakWorkingSetBytes = peakWorkingSetValue > 0 ? peakWorkingSetValue : null,
            WorkingSetDeltaBytes = workingSetBefore.HasValue && workingSetAfter.HasValue
                ? workingSetAfter.Value - workingSetBefore.Value
                : null,
            AverageDocumentBytes = documentSizes.Count == 0 ? 0 : documentSizes.Average(),
            MaxDegreeOfParallelism = maxDegreeOfParallelism
        };
    }

    private static List<string> Evaluate(PerformanceScenarioResult result, PerformanceBudget budget)
    {
        var failures = new List<string>();

        if (result.MedianElapsedMs > budget.MaxMedianElapsedMs)
        {
            failures.Add(
                $"median elapsed {result.MedianElapsedMs:F1} ms exceeds budget {budget.MaxMedianElapsedMs:F1} ms.");
        }

        if (result.AllocatedBytesPerDocument > budget.MaxAllocatedBytesPerDocument)
        {
            failures.Add(
                $"allocated {result.AllocatedBytesPerDocument / 1024 / 1024:F1} MiB/document exceeds " +
                $"budget {budget.MaxAllocatedBytesPerDocument / 1024 / 1024:F1} MiB.");
        }

        if (result.PeakWorkingSetBytes.HasValue && result.PeakWorkingSetBytes.Value > budget.MaxPeakWorkingSetBytes)
        {
            failures.Add(
                $"peak working set {result.PeakWorkingSetBytes.Value / 1024 / 1024:F1} MiB exceeds " +
                $"budget {budget.MaxPeakWorkingSetBytes / 1024 / 1024:F1} MiB.");
        }

        return failures;
    }

    private static PerformanceBudgets CreateBudgets(
        QualityEnvironment environment,
        IReadOnlyCollection<PerformanceScenarioResult> results)
    {
        return new PerformanceBudgets
        {
            SchemaVersion = 1,
            MeasuredOn = environment.RuntimeIdentifier,
            Scenarios = results.ToDictionary(
                x => x.Id,
                x => new PerformanceBudget
                {
                    MaxMedianElapsedMs = Math.Max(50, Math.Ceiling(x.MedianElapsedMs * 8)),
                    MaxAllocatedBytesPerDocument = Math.Max(1, Math.Ceiling(x.AllocatedBytesPerDocument * 2)),
                    MaxPeakWorkingSetBytes = Math.Max(
                        1024L * 1024 * 1024,
                        (long) Math.Ceiling((double) (x.PeakWorkingSetBytes ?? 0) * 2))
                },
                StringComparer.Ordinal)
        };
    }

    private static long? TryGetWorkingSet(Process process)
    {
        try
        {
            process.Refresh();
            return process.WorkingSet64;
        }
        catch
        {
            return null;
        }
    }

    private static long? TryGetPeakWorkingSet(Process process)
    {
        try
        {
            process.Refresh();
            return process.PeakWorkingSet64;
        }
        catch
        {
            return null;
        }
    }

    private static void InterlockedMax(ref long target, long value)
    {
        long current;

        do
        {
            current = Volatile.Read(ref target);

            if (value <= current)
                return;
        } while (Interlocked.CompareExchange(ref target, value, current) != current);
    }

    private static string FormatBytes(long? value)
    {
        return value.HasValue
            ? $"{value.Value / 1024 / 1024:F1} MiB"
            : "unavailable";
    }
}
