#:sdk Meziantou.NET.Sdk
#:project ../src/Meziantou.Framework.FullPath/Meziantou.Framework.FullPath.csproj
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Meziantou.Framework;

if (args.Length > 0 && args[0] is "--help" or "-h")
{
    Console.WriteLine("Usage: dotnet run validate-test-target-frameworks.cs");
    Console.WriteLine("Validates that eng/test-target-frameworks.json matches TFMs found in tests/**/*.csproj.");
    return 0;
}

var rootPath = GetRepositoryRoot();
var manifestPath = rootPath / "eng" / "test-target-frameworks.json";
var testsRootPath = rootPath / "tests";

if (!File.Exists(manifestPath))
{
    Console.Error.WriteLine($"ERROR: Cannot find manifest file: {manifestPath}");
    return 1;
}

var manifest = LoadManifest(manifestPath);

var testProjects = Directory.GetFiles(testsRootPath, "*.csproj", SearchOption.AllDirectories)
    .Select(FullPath.FromPath)
    .ToList();

var tfms = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
Parallel.ForEach(testProjects, new ParallelOptions { MaxDegreeOfParallelism = 5 }, projectPath =>
{
    foreach (var tfm in GetProjectTargetFrameworksWithRetry(projectPath))
    {
        tfms.TryAdd(tfm, true);
    }
});

var allTfms = tfms.Keys.OrderBy(static tfm => tfm, StringComparer.Ordinal).ToArray();
var nonWindowsTfms = allTfms.Where(static tfm => !tfm.Contains("-windows", StringComparison.Ordinal)).ToArray();

var hasErrors = false;
hasErrors |= !ValidateManifestEntry("linux", manifest.Linux, nonWindowsTfms);
hasErrors |= !ValidateManifestEntry("mac", manifest.Mac, nonWindowsTfms);
hasErrors |= !ValidateManifestEntry("windows", manifest.Windows, allTfms);

if (hasErrors)
{
    return 1;
}

Console.WriteLine("Test target framework manifest is valid.");
return 0;

static bool ValidateManifestEntry(string os, IEnumerable<string> expectedValues, IEnumerable<string> actualValues)
{
    var expected = expectedValues
        .Where(static value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(static value => value, StringComparer.Ordinal)
        .ToArray();

    var actual = actualValues
        .Where(static value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(static value => value, StringComparer.Ordinal)
        .ToArray();

    var missingFromManifest = actual.Except(expected, StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
    var extraInManifest = expected.Except(actual, StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray();

    if (missingFromManifest.Length == 0 && extraInManifest.Length == 0)
    {
        Console.WriteLine($"[{os}] OK ({string.Join(", ", expected)})");
        return true;
    }

    Console.Error.WriteLine($"ERROR: Mismatch for '{os}' TFMs in eng/test-target-frameworks.json.");
    if (missingFromManifest.Length > 0)
    {
        Console.Error.WriteLine($"  Missing in manifest: {string.Join(", ", missingFromManifest)}");
    }

    if (extraInManifest.Length > 0)
    {
        Console.Error.WriteLine($"  Extra in manifest: {string.Join(", ", extraInManifest)}");
    }

    Console.Error.WriteLine($"  Manifest value: {string.Join(", ", expected)}");
    Console.Error.WriteLine($"  Actual value:   {string.Join(", ", actual)}");
    return false;
}

static string[] GetProjectTargetFrameworksWithRetry(FullPath projectPath)
{
    var value = DotNetBuildGetPropertyWithRetry(projectPath, "TargetFrameworks");
    if (string.IsNullOrWhiteSpace(value))
    {
        value = DotNetBuildGetPropertyWithRetry(projectPath, "TargetFramework");
    }

    return value
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(static tfm => tfm, StringComparer.Ordinal)
        .ToArray();
}

static string DotNetBuildGetPropertyWithRetry(FullPath projectPath, string propertyName, int maxAttempts = 3, int delaySeconds = 2)
{
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("build");
        psi.ArgumentList.Add("--no-restore");
        psi.ArgumentList.Add("--nologo");
        psi.ArgumentList.Add("-v:q");
        psi.ArgumentList.Add("--getProperty:" + propertyName);
        psi.ArgumentList.Add(projectPath);

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();

        if (process.ExitCode == 0)
        {
            return output;
        }

        if (attempt < maxAttempts)
        {
            Console.WriteLine($"WARNING: Attempt {attempt} failed for {projectPath} ({propertyName}). Retrying in {delaySeconds} seconds...");
            Thread.Sleep(delaySeconds * 1000);
        }
        else
        {
            throw new InvalidOperationException($"Failed to get {propertyName} for {projectPath} after {maxAttempts} attempts. {error}");
        }
    }

    return ""; // unreachable
}

static FullPath GetRepositoryRoot() => FullPath.CurrentDirectory().FindRequiredGitRepositoryRoot();

static TestTargetFrameworkManifest LoadManifest(FullPath manifestPath)
{
    using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
    var root = document.RootElement;
    if (root.ValueKind != JsonValueKind.Object)
    {
        throw new InvalidOperationException($"Invalid JSON in {manifestPath}. Expected an object.");
    }

    return new TestTargetFrameworkManifest(
        Linux: GetRequiredStringArray(root, "linux", manifestPath),
        Mac: GetRequiredStringArray(root, "mac", manifestPath),
        Windows: GetRequiredStringArray(root, "windows", manifestPath));
}

static string[] GetRequiredStringArray(JsonElement root, string propertyName, FullPath manifestPath)
{
    if (!root.TryGetProperty(propertyName, out var property))
    {
        throw new InvalidOperationException($"Missing '{propertyName}' property in {manifestPath}.");
    }

    if (property.ValueKind != JsonValueKind.Array)
    {
        throw new InvalidOperationException($"Property '{propertyName}' in {manifestPath} must be an array.");
    }

    return property
        .EnumerateArray()
        .Select(static value => value.GetString())
        .Where(static value => !string.IsNullOrWhiteSpace(value))
        .Select(static value => value!)
        .ToArray();
}

readonly record struct TestTargetFrameworkManifest(string[] Linux, string[] Mac, string[] Windows);
