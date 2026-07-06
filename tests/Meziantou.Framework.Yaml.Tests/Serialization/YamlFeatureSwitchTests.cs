using System.Diagnostics;
using TestUtilities;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlFeatureSwitchTests
{
    [Fact]
    public void Assembly_EmbedsIlLinkSubstitutions()
    {
        var resources = typeof(YamlSerializer).Assembly.GetManifestResourceNames();
        Assert.True(resources.Contains("ILLink.Substitutions.xml", StringComparer.Ordinal), "Expected embedded resource 'ILLink.Substitutions.xml'.");
    }

    [Fact]
    public void ReflectionSwitch_DisablesReflectionButKeepsUntypedContainersWorking()
    {
        var smokeDll = OutputPathHelper.GetOutputDirectory("Meziantou.Framework.Yaml.FeatureSwitchSmoke");
        //var smokeDll = Path.Combine(root, "artifacts", "bin", "Meziantou.Framework.Yaml.FeatureSwitchSmoke", "debug_net10.0", "Meziantou.Framework.Yaml.FeatureSwitchSmoke.dll");

        Assert.True(File.Exists(smokeDll), $"Missing smoke executable at {smokeDll}.");

        var startInfo = new ProcessStartInfo("dotnet", $"\"{smokeDll}\"")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        process.WaitForExit(30_000);

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        if (process.ExitCode != 0)
        {
            Assert.Fail($"Smoke executable failed with exit code {process.ExitCode}.{Environment.NewLine}stdout:{Environment.NewLine}{output}{Environment.NewLine}stderr:{Environment.NewLine}{error}");
        }
    }
}
