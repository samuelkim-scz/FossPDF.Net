#if NET9_0
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace FossPDF.UnitTests.Parity
{
    public partial class ParityAcceptanceTests
    {
        [ParityFixtureRunner("RUN-503")]
        private static void RunPublishedConsumers(ParityFixture fixture)
        {
            var path = Environment.GetEnvironmentVariable("FOSSPDF_RUNTIME_QUALIFICATION");
            if (!File.Exists(path))
                Assert.Ignore("Publish-and-run evidence is required. The Runtime deployment qualification CI workflow executes RUN-503 against real packaged consumers.");
            using var report = JsonDocument.Parse(File.ReadAllText(path));
            var root = report.RootElement;
            Assert.That(root.GetProperty("schema_version").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("sdk").GetString(), Does.StartWith("9."));
            Assert.That(root.GetProperty("package_feed").GetString(), Is.Not.Empty);
            var profiles = root.GetProperty("profiles").EnumerateArray().ToArray();
            Assert.That(profiles.Length, Is.EqualTo(12));
            foreach (var feature in new[] { "Core", "Fonts", "Svg", "Pdf" })
            foreach (var mode in new[] { "jit", "trimmed", "aot" })
            {
                var profile = profiles.Single(item => item.GetProperty("feature").GetString() == feature && item.GetProperty("mode").GetString() == mode);
                Assert.That(profile.GetProperty("status").GetString(), Is.EqualTo("passed"), feature + "/" + mode);
                Assert.That(profile.GetProperty("publish_exit_code").GetInt32(), Is.Zero);
                Assert.That(profile.GetProperty("run_exit_code").GetInt32(), Is.Zero);
                Assert.That(profile.GetProperty("independent_checks").GetInt32(), Is.GreaterThan(0));
                Assert.That(profile.GetProperty("foss_pdf_packages").GetArrayLength(), Is.GreaterThan(0));
                var directory = Path.Combine(Path.GetDirectoryName(path), profile.GetProperty("relative_directory").GetString());
                using var runtime = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "result", "runtime-report.json")));
                Assert.That(runtime.RootElement.GetProperty("DynamicCodeSupported").GetBoolean(), Is.EqualTo(mode != "aot"));
                Assert.That(runtime.RootElement.GetProperty("Rid").GetString(), Is.EqualTo(root.GetProperty("rid").GetString()));
                Assert.That(runtime.RootElement.GetProperty("NativeFiles").GetArrayLength(), Is.GreaterThan(0));
                Assert.That(runtime.RootElement.GetProperty("Results").EnumerateArray().All(result => result.GetProperty("Passed").GetBoolean()), Is.True);
                using var independent = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "result", "independent-inspection.json")));
                Assert.That(independent.RootElement.GetProperty("passed").GetBoolean(), Is.True);
                if (mode != "jit")
                    Assert.That(independent.RootElement.GetProperty("raster_comparisons").GetArrayLength(), Is.GreaterThanOrEqualTo(2));
            }
            SaveArtifact(fixture.Id, "qualification.json", File.ReadAllBytes(path));
        }
    }
}
#endif
