using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace FossPDF.UnitTests
{
    /// <summary>
    /// Locates and runs the external qpdf tool. Tests that need a real PDF optimizer use
    /// <see cref="Require"/>, which skips (rather than fails) on machines without qpdf. CI installs
    /// qpdf, so the coverage runs there. The production lookup only uses PATH and explicit paths;
    /// the well-known install directory fallback here exists because the Windows installer does not
    /// always update the PATH of the current process.
    /// </summary>
    internal static class QpdfTestTool
    {
        private static readonly Lazy<string> ResolvedPath = new Lazy<string>(Find);

        public static string Require()
        {
            var path = ResolvedPath.Value;

            if (path == null)
                Assert.Ignore("qpdf 11.0 or newer is required for PDF optimization tests and was not found.");

            return path;
        }

        public static (int ExitCode, string Output) Run(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Require(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return (process.ExitCode, output + error);
        }

        private static string Find()
        {
            var fromPath = FindOnPath();

            if (fromPath != null)
                return fromPath;

            var candidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? FindWindowsInstallations()
                : new[]
                {
                    "/usr/bin/qpdf",
                    "/usr/local/bin/qpdf",
                    "/opt/homebrew/bin/qpdf",
                    "/opt/local/bin/qpdf"
                };

            return candidates.FirstOrDefault(File.Exists);
        }

        private static string FindOnPath()
        {
            var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

            foreach (var directory in pathVariable.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                    continue;

                foreach (var name in new[] { "qpdf", "qpdf.exe" })
                {
                    var candidate = Path.Combine(directory.Trim(), name);

                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            return null;
        }

        private static string[] FindWindowsInstallations()
        {
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };

            return roots
                .Where(Directory.Exists)
                .SelectMany(root => Directory.GetDirectories(root, "qpdf*"))
                .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(directory => Path.Combine(directory, "bin", "qpdf.exe"))
                .ToArray();
        }
    }
}
