using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FossPDF.UnitTests.Parity
{
    internal sealed class FixtureCatalog
    {
        internal static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        public int SchemaVersion { get; set; }
        public FixtureTarget Target { get; set; }
        public string ContractPolicy { get; set; }
        public VisualPolicy VisualPolicy { get; set; }
        public List<CapabilityContract> Capabilities { get; set; } = new();
        public List<ParityFixture> Fixtures { get; set; } = new();

        internal static string DataPath(string name) =>
            Path.Combine(AppContext.BaseDirectory, "Parity", name);

        internal static FixtureCatalog Load() =>
            JsonSerializer.Deserialize<FixtureCatalog>(
                File.ReadAllText(DataPath("acceptance-fixtures.json")), JsonOptions)
            ?? throw new InvalidDataException("The parity fixture catalog is empty.");

        internal static JsonElement[] ReadArray(JsonElement parent, string name)
        {
            using var enumerator = parent.GetProperty(name).EnumerateArray();
            return enumerator.ToArray();
        }
    }

    internal sealed class FixtureTarget
    {
        public string Questpdf { get; set; }
        public string FosspdfBaselineCommit { get; set; }
    }

    internal sealed class VisualPolicy
    {
        public double GeometryTolerancePoints { get; set; }
        public int SolidChannelTolerance { get; set; }
        public double MaxChangedPixelFraction { get; set; }
    }

    internal sealed class CapabilityContract
    {
        public string WorkItem { get; set; }
        public string BaselineStatus { get; set; }
        public string CurrentCapability { get; set; }
        public string PublicContract { get; set; }
        public List<string> FixtureIds { get; set; } = new();
    }

    internal sealed class ParityFixture
    {
        /// <summary>
        /// The framework the fixture runs on. An empty list means every framework the tests
        /// target. Fixtures backed by a net9.0-only companion package declare "net9.0" so the
        /// net8.0 run neither executes nor expects their runner.
        /// </summary>
        public List<string> Frameworks { get; set; } = new();

        public string Id { get; set; }
        public string Title { get; set; }
        public List<string> WorkItems { get; set; } = new();
        public string Execution { get; set; }
        public JsonElement Input { get; set; }
        public List<string> Expected { get; set; } = new();
        public RasterExpectation Raster { get; set; }

#if NET9_0
        internal const string CurrentFramework = "net9.0";
#else
        internal const string CurrentFramework = "net8.0";
#endif

        internal bool ExecutesOnCurrentFramework =>
            Frameworks.Count == 0 ||
            Frameworks.Contains(CurrentFramework, StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class RasterExpectation
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int Dpi { get; set; }
        public List<List<ReferenceRectangle>> Pages { get; set; } = new();
    }

    internal sealed class ReferenceRectangle
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Color { get; set; }
    }
}
