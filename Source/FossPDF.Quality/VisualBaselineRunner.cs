using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FossPDF.Fluent;
using SkiaSharp;
using SharedImage = FossPDF.Infrastructure.Image;

namespace FossPDF.Quality;

internal static class VisualBaselineRunner
{
    private const string BaselineFileName = "visual-baselines.json";

    public static bool Run(QualityOptions options)
    {
        var environment = QualityEnvironment.Capture();
        var outputDirectory = Path.Combine(options.OutputDirectory, "visual");
        Directory.CreateDirectory(outputDirectory);

        var scenarios = new Dictionary<string, VisualScenarioMetrics>(StringComparer.Ordinal);

        using var image = SharedImage.FromBinaryData(DocumentScenarios.CreateQuadrantImagePng(200, 150));

        var definitions = new (string Id, Func<Document> Factory)[]
        {
            ("report", DocumentScenarios.CreateReportDocument),
            ("text", DocumentScenarios.CreateTextDocument),
            ("graphics", DocumentScenarios.CreateGraphicsDocument),
            ("image", () => DocumentScenarios.CreateImageDocument(image))
        };

        foreach (var definition in definitions)
        {
            var document = definition.Factory();
            var pdf = document.GeneratePdf(out var pageCount);
            var images = document.GenerateImages().ToArray();

            if (images.Length != pageCount)
            {
                throw new InvalidOperationException(
                    $"{definition.Id}: PDF reported {pageCount} pages but raster export produced {images.Length}.");
            }

            var scenarioDirectory = Path.Combine(outputDirectory, definition.Id);
            Directory.CreateDirectory(scenarioDirectory);
            File.WriteAllBytes(Path.Combine(scenarioDirectory, "document.pdf"), pdf);

            var pages = new List<PageMetrics>(images.Length);

            for (var index = 0; index < images.Length; index++)
            {
                File.WriteAllBytes(Path.Combine(scenarioDirectory, $"page-{index + 1}.png"), images[index]);
                using var bitmap = SKBitmap.Decode(images[index])
                    ?? throw new InvalidOperationException($"{definition.Id}: could not decode page {index + 1}.");

                pages.Add(VisualMetricsCalculator.Compute(bitmap, index + 1));
            }

            scenarios[definition.Id] = new VisualScenarioMetrics
            {
                PageCount = pageCount,
                Pages = pages
            };

            Console.WriteLine($"VISUAL: {definition.Id} -> {pageCount} page(s), {string.Join(", ", pages.Select(x => x.InkPixels + " ink px"))}");
        }

        JsonFile.Write(Path.Combine(outputDirectory, "metrics.json"), new VisualRunResult
        {
            Environment = environment,
            Baseline = "actual",
            Passed = true,
            Scenarios = scenarios
        });

        var baselinePath = Path.Combine(options.BaselineDirectory, BaselineFileName);

        if (options.UpdateVisualBaseline)
        {
            var baseline = JsonFile.Read<VisualBaseline>(baselinePath) ?? new VisualBaseline();

            baseline.SchemaVersion = 1;

            if (string.IsNullOrWhiteSpace(baseline.MeasuredOn) ||
                baseline.MeasuredOn == environment.RuntimeIdentifier)
            {
                // The reference platform owns the default metrics and the font pins.
                baseline.MeasuredOn = environment.RuntimeIdentifier;
                baseline.FontSha256 = environment.FontSha256;
                baseline.Scenarios = scenarios;
            }
            else
            {
                // Other platforms get an override rather than replacing the reference baseline.
                baseline.Platforms[environment.RuntimeIdentifier] = new VisualPlatformBaseline
                {
                    MeasuredOn = environment.RuntimeIdentifier,
                    Scenarios = scenarios
                };
            }

            JsonFile.Write(baselinePath, baseline);
            Console.WriteLine($"VISUAL: baseline written to {baselinePath} for {environment.RuntimeIdentifier}");
            return true;
        }

        var existing = JsonFile.Read<VisualBaseline>(baselinePath);

        if (existing == null)
        {
            Console.Error.WriteLine(
                $"VISUAL: baseline not found at {baselinePath}. " +
                "Run with --update-visual-baseline on a reference platform, review the output, and commit it.");
            return false;
        }

        var failures = new List<string>();

        var expectedFonts = existing.FontSha256.OrderBy(x => x.Key, StringComparer.Ordinal);
        var actualFonts = environment.FontSha256.OrderBy(x => x.Key, StringComparer.Ordinal);

        if (existing.FontSha256.Count > 0 && !expectedFonts.SequenceEqual(actualFonts))
            failures.Add("Embedded font hashes changed; regenerate the visual baseline and review the output.");

        failures.AddRange(VisualBaselineComparer.Compare(existing, environment.RuntimeIdentifier, scenarios));

        JsonFile.Write(Path.Combine(outputDirectory, "result.json"), new VisualRunResult
        {
            Environment = environment,
            Baseline = baselinePath,
            Passed = failures.Count == 0,
            Failures = failures,
            Scenarios = scenarios
        });

        foreach (var failure in failures)
            Console.Error.WriteLine("VISUAL: " + failure);

        Console.WriteLine(failures.Count == 0
            ? $"VISUAL: passed for {environment.RuntimeIdentifier} ({definitions.Length} scenarios)."
            : $"VISUAL: failed for {environment.RuntimeIdentifier}: {failures.Count} issue(s).");

        return failures.Count == 0;
    }
}
