using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace FossPDF.Quality;

internal sealed class VisualScenarioMetrics
{
    public int PageCount { get; init; }
    public List<PageMetrics> Pages { get; init; } = new();
}

internal sealed class PageMetrics
{
    public int Page { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int InkPixels { get; init; }
    public PixelBounds InkBounds { get; init; } = new();
    public Dictionary<string, int> ColorPixels { get; init; } = new(StringComparer.Ordinal);
}

internal sealed class PixelBounds
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

internal sealed class VisualTolerances
{
    public int BoundsPixels { get; set; } = 4;
    public double InkPixelsRelative { get; set; } = 0.10;
    public double ColorPixelsRelative { get; set; } = 0.20;
    public int MinimumAbsolutePixels { get; set; } = 100;
}

internal sealed class VisualBaseline
{
    public int SchemaVersion { get; set; } = 1;
    public string MeasuredOn { get; set; } = string.Empty;
    public Dictionary<string, string> FontSha256 { get; set; } = new(StringComparer.Ordinal);
    public VisualTolerances Tolerances { get; set; } = new();
    public Dictionary<string, VisualScenarioMetrics> Scenarios { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, VisualPlatformBaseline> Platforms { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class VisualPlatformBaseline
{
    public string MeasuredOn { get; set; } = string.Empty;
    public VisualTolerances? Tolerances { get; set; }
    public Dictionary<string, VisualScenarioMetrics> Scenarios { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class VisualRunResult
{
    public int SchemaVersion { get; init; } = 1;
    public QualityEnvironment Environment { get; init; } = new();
    public string Baseline { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public List<string> Failures { get; init; } = new();
    public Dictionary<string, VisualScenarioMetrics> Scenarios { get; init; } = new(StringComparer.Ordinal);
}

internal static class VisualMetricsCalculator
{
    public static PageMetrics Compute(SKBitmap page, int pageNumber)
    {
        var inkPixels = 0;
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        var colors = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["red"] = 0,
            ["green"] = 0,
            ["blue"] = 0,
            ["yellow"] = 0,
            ["dark"] = 0,
            ["gray"] = 0,
            ["other"] = 0
        };

        for (var y = 0; y < page.Height; y++)
        for (var x = 0; x < page.Width; x++)
        {
            var color = page.GetPixel(x, y);

            if (color.Red >= 245 && color.Green >= 245 && color.Blue >= 245)
                continue;

            inkPixels++;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);

            colors[Classify(color)]++;
        }

        var bounds = inkPixels == 0
            ? new PixelBounds()
            : new PixelBounds
            {
                X = minX,
                Y = minY,
                Width = maxX - minX + 1,
                Height = maxY - minY + 1
            };

        return new PageMetrics
        {
            Page = pageNumber,
            Width = page.Width,
            Height = page.Height,
            InkPixels = inkPixels,
            InkBounds = bounds,
            ColorPixels = colors
        };
    }

    private static string Classify(SKColor color)
    {
        var red = color.Red;
        var green = color.Green;
        var blue = color.Blue;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));

        if (max <= 100)
            return "dark";

        if (max - min <= 25)
            return "gray";

        if (red >= 120 && green >= 120 && blue <= Math.Min(red, green) - 40)
            return "yellow";

        if (red >= 120 && red - green >= 40 && red - blue >= 40)
            return "red";

        if (green >= 120 && green - red >= 40 && green - blue >= 40)
            return "green";

        if (blue >= 120 && blue - red >= 40 && blue - green >= 40)
            return "blue";

        return "other";
    }
}

internal static class VisualBaselineComparer
{
    public static List<string> Compare(
        VisualBaseline baseline,
        string runtimeIdentifier,
        Dictionary<string, VisualScenarioMetrics> actual)
    {
        var failures = new List<string>();

        var expectedScenarios = baseline.Scenarios;
        var tolerances = baseline.Tolerances;

        if (baseline.Platforms.TryGetValue(runtimeIdentifier, out var platform))
        {
            expectedScenarios = platform.Scenarios;
            tolerances = platform.Tolerances ?? tolerances;
        }

        var expectedIds = expectedScenarios.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var actualIds = actual.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();

        if (!expectedIds.SequenceEqual(actualIds))
        {
            failures.Add(
                $"Scenario set changed. Baseline: [{string.Join(", ", expectedIds)}]; " +
                $"actual: [{string.Join(", ", actualIds)}].");
            return failures;
        }

        foreach (var scenarioId in expectedIds)
        {
            var expected = expectedScenarios[scenarioId];
            var current = actual[scenarioId];

            if (expected.PageCount != current.PageCount)
            {
                failures.Add($"{scenarioId}: page count {current.PageCount} != baseline {expected.PageCount}.");
                continue;
            }

            for (var index = 0; index < expected.Pages.Count; index++)
            {
                var expectedPage = expected.Pages[index];
                var actualPage = current.Pages[index];
                var page = $"{scenarioId} page {index + 1}";

                if (expectedPage.Width != actualPage.Width || expectedPage.Height != actualPage.Height)
                {
                    failures.Add(
                        $"{page}: dimensions {actualPage.Width}x{actualPage.Height} != " +
                        $"{expectedPage.Width}x{expectedPage.Height}.");
                    continue;
                }

                CompareBounds(page, expectedPage.InkBounds, actualPage.InkBounds, tolerances, failures);

                if (!WithinRelative(actualPage.InkPixels, expectedPage.InkPixels,
                        tolerances.InkPixelsRelative, tolerances.MinimumAbsolutePixels))
                {
                    failures.Add(
                        $"{page}: ink pixels {actualPage.InkPixels} outside baseline " +
                        $"{expectedPage.InkPixels} +/- {tolerances.InkPixelsRelative:P0}.");
                }

                foreach (var color in expectedPage.ColorPixels.Keys
                             .Union(actualPage.ColorPixels.Keys)
                             .OrderBy(x => x, StringComparer.Ordinal))
                {
                    var expectedCount = expectedPage.ColorPixels.GetValueOrDefault(color);
                    var actualCount = actualPage.ColorPixels.GetValueOrDefault(color);

                    if (!WithinRelative(actualCount, expectedCount,
                            tolerances.ColorPixelsRelative, tolerances.MinimumAbsolutePixels))
                    {
                        failures.Add(
                            $"{page}: {color} pixels {actualCount} outside baseline {expectedCount} " +
                            $"+/- {tolerances.ColorPixelsRelative:P0}.");
                    }
                }
            }
        }

        return failures;
    }

    private static void CompareBounds(
        string page,
        PixelBounds expected,
        PixelBounds actual,
        VisualTolerances tolerances,
        List<string> failures)
    {
        var x = Math.Abs(actual.X - expected.X);
        var y = Math.Abs(actual.Y - expected.Y);
        var width = Math.Abs(actual.Width - expected.Width);
        var height = Math.Abs(actual.Height - expected.Height);

        if (x > tolerances.BoundsPixels || y > tolerances.BoundsPixels ||
            width > tolerances.BoundsPixels || height > tolerances.BoundsPixels)
        {
            failures.Add(
                $"{page}: ink bounds ({actual.X},{actual.Y},{actual.Width},{actual.Height}) outside " +
                $"({expected.X},{expected.Y},{expected.Width},{expected.Height}) +/- {tolerances.BoundsPixels}px.");
        }
    }

    private static bool WithinRelative(int actual, int expected, double relative, int minimumAbsolute)
    {
        if (actual == expected)
            return true;

        var difference = Math.Abs(actual - expected);

        if (difference <= minimumAbsolute)
            return true;

        return difference / (double) Math.Max(1, Math.Abs(expected)) <= relative;
    }
}
