using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FossPDF.Previewer;

internal static class SourceNavigation
{
    internal static ProcessStartInfo CreateStartInfo(string executable, InspectionNode source)
    {
        if (string.IsNullOrWhiteSpace(source.SourceFile) || !Path.IsPathFullyQualified(source.SourceFile) ||
            !File.Exists(source.SourceFile) || source.SourceLine < 1)
            throw new InvalidOperationException("The source file is unavailable here. Use Copy location to find it in your local checkout.");
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            throw new InvalidOperationException("VS Code was not found. Use Copy location or set FOSSPDF_VSCODE_PATH to its executable.");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("FOSSPDF_VSCODE_PATH must name the VS Code executable, not a command script.");
        var start = new ProcessStartInfo { FileName = executable, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--goto");
        start.ArgumentList.Add(Path.GetFullPath(source.SourceFile) + ":" + source.SourceLine);
        return start;
    }

    internal static string? FindExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("FOSSPDF_VSCODE_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var candidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe") }
            : new[] { "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code", "/usr/bin/code", "/usr/local/bin/code", "/snap/bin/code" };
        return candidates.FirstOrDefault(File.Exists);
    }
}
