using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using FossPDF.Fluent;
using SkiaSharp;

namespace FossPDF.Quality;

internal sealed class QualityEnvironment
{
    public int SchemaVersion { get; init; } = 1;
    public string CapturedAtUtc { get; init; } = string.Empty;
    public string BaselineCommit { get; init; } = BaselineCommitValue;
    public string OperatingSystem { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public string RuntimeIdentifier { get; init; } = string.Empty;
    public string Framework { get; init; } = string.Empty;
    public string FossPdfVersion { get; init; } = string.Empty;
    public string FossPdfInformationalVersion { get; init; } = string.Empty;
    public string SkiaSharpVersion { get; init; } = string.Empty;
    public string HarfBuzzSharpVersion { get; init; } = string.Empty;
    public Dictionary<string, string> FontSha256 { get; init; } = new(StringComparer.Ordinal);

    private const string BaselineCommitValue = "0339255ff8f987a4abb62ef81f06bbcf00e0b81e";

    public static QualityEnvironment Capture()
    {
        var runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;

        if (string.IsNullOrWhiteSpace(runtimeIdentifier))
            runtimeIdentifier = $"{GetPlatformName()}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";

        return new QualityEnvironment
        {
            CapturedAtUtc = DateTime.UtcNow.ToString("O"),
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeIdentifier = runtimeIdentifier,
            Framework = RuntimeInformation.FrameworkDescription,
            FossPdfVersion = DescribeAssembly(typeof(Document).Assembly),
            FossPdfInformationalVersion = DescribeInformationalVersion(typeof(Document).Assembly),
            SkiaSharpVersion = DescribeAssembly(typeof(SKCanvas).Assembly),
            HarfBuzzSharpVersion = DescribeAssembly(typeof(HarfBuzzSharp.Buffer).Assembly),
            FontSha256 = CaptureFontHashes(typeof(Document).Assembly)
        };
    }

    public void Write(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "environment.json");
        JsonFile.Write(path, this);
    }

    private static string GetPlatformName()
    {
        if (System.OperatingSystem.IsWindows())
            return "win";

        if (System.OperatingSystem.IsMacOS())
            return "osx";

        if (System.OperatingSystem.IsLinux())
            return "linux";

        return "unknown";
    }

    private static string DescribeAssembly(Assembly assembly)
    {
        var informational = DescribeInformationalVersion(assembly);

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Informational versions carry the package version plus source-link metadata.
            var metadataIndex = informational.IndexOf('+');
            return metadataIndex < 0 ? informational : informational[..metadataIndex];
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private static string DescribeInformationalVersion(Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? string.Empty;
    }

    private static Dictionary<string, string> CaptureFontHashes(Assembly assembly)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        var resources = assembly
            .GetManifestResourceNames()
            .Where(x => x.Contains(".Resources.DefaultFont.", StringComparison.Ordinal) &&
                        x.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.Ordinal);

        foreach (var resource in resources)
        {
            using var stream = assembly.GetManifestResourceStream(resource);

            if (stream == null)
                continue;

            using var hash = SHA256.Create();
            var digest = hash.ComputeHash(stream);
            var segments = resource.Split('.');
            var fileName = string.Join('.', segments[^2], segments[^1]);
            result[fileName] = Convert.ToHexString(digest).ToLowerInvariant();
        }

        return result;
    }
}

internal static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Write<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, JsonSerializer.Serialize(value, Options));
    }

    public static T? Read<T>(string path)
    {
        return File.Exists(path)
            ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
            : default;
    }
}
