using System.Text.Json;

namespace FossPDF.NativePrototypes;

internal sealed class PrototypeResult
{
    public string Name { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public List<string> Checks { get; init; } = new();
    public List<string> Artifacts { get; init; } = new();
    public string? Error { get; init; }
}

internal static class JsonFile
{
    private static readonly JsonSerializerOptions Options = new()
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
}
