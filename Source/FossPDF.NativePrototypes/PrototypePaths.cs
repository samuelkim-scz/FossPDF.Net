using System;

namespace FossPDF.NativePrototypes;

internal static class PrototypePaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string LatoRegular =>
        Path.Combine(RepositoryRoot, "Source", "FossPDF", "Resources", "DefaultFont", "Lato-Regular.ttf");

    public static string LatoBold =>
        Path.Combine(RepositoryRoot, "Source", "FossPDF", "Resources", "DefaultFont", "Lato-Bold.ttf");

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
