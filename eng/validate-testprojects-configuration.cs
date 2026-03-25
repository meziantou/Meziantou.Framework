#:sdk Meziantou.NET.Sdk
#:project ../src/Meziantou.Framework.FullPath/Meziantou.Framework.FullPath.csproj
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Meziantou.Framework;

if (args.Length > 0 && args[0] is "--help" or "-h")
{
    Console.WriteLine("Usage: dotnet run validate-testprojects-configuration.cs");
    Console.WriteLine("Validates that test projects target all TFMs of their referenced src projects.");
    return 0;
}

var rootPath = GetRepositoryRoot();
var testsRootPath = Path.Combine(rootPath, "tests");
var utilsPath = Path.GetFullPath(Path.Combine(testsRootPath, "TestUtilities", "TestUtilities.csproj"));

var testProjects = Directory.GetFiles(testsRootPath, "*.csproj", SearchOption.AllDirectories);
var errors = new ConcurrentBag<string>();

var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 5 };
await Parallel.ForEachAsync(testProjects, parallelOptions, async (proj, ct) =>
{
    await Task.Yield(); // ensure async context

    var testProjectTfms = DotNetBuildGetTfmsWithRetry(proj)
        .Select(SimplifyTfm)
        .ToList();

    var doc = XDocument.Load(proj);
    var projDir = Path.GetDirectoryName(proj)!;
    var references = doc.Descendants("ProjectReference")
        .Select(e => e.Attribute("Include")?.Value)
        .Where(v => v is not null)
        .Select(v => Path.GetFullPath(Path.Combine(projDir, v!)))
        .ToList();

    foreach (var refProj in references)
    {
        if (string.Equals(refProj, utilsPath, StringComparison.OrdinalIgnoreCase))
            continue;

        // Only consider the main referenced project (test project name starts with ref project name)
        if (!Path.GetFileNameWithoutExtension(proj).StartsWith(Path.GetFileNameWithoutExtension(refProj), StringComparison.Ordinal))
            continue;

        var refTfms = DotNetBuildGetTfmsWithRetry(refProj);
        foreach (var refTfmRaw in refTfms)
        {
            var refTfm = SimplifyTfm(refTfmRaw);

            if (refTfm is "netstandard2.0" or "netstandard2.1")
                continue;

            if (refTfm == "net462" && testProjectTfms.Contains("net472", StringComparer.Ordinal))
                continue;

            if (!testProjectTfms.Contains(refTfm, StringComparer.Ordinal))
            {
                var errorMsg = $"Project {proj} does not target {refTfm}, but it references {refProj} which does. ({string.Join(", ", testProjectTfms)}) != ({string.Join(", ", refTfms)})";
                Console.Error.WriteLine($"ERROR: {errorMsg}");
                errors.Add(errorMsg);
            }
        }
    }
});

if (!errors.IsEmpty)
{
    return 1;
}

return 0;

static string SimplifyTfm(string tfm)
{
    var dashIndex = tfm.IndexOf('-', StringComparison.Ordinal);
    return dashIndex >= 0 ? tfm[..dashIndex] : tfm;
}

static string[] DotNetBuildGetTfmsWithRetry(string projectPath, int maxAttempts = 3, int delaySeconds = 2)
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
        psi.ArgumentList.Add("--getProperty:TargetFrameworks");
        psi.ArgumentList.Add(projectPath);

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        if (process.ExitCode == 0)
        {
            return output.Split(';', StringSplitOptions.RemoveEmptyEntries);
        }

        if (attempt < maxAttempts)
        {
            Console.WriteLine($"WARNING: Attempt {attempt} failed for {projectPath}. Retrying in {delaySeconds} seconds...");
            Thread.Sleep(delaySeconds * 1000);
        }
        else
        {
            throw new InvalidOperationException($"Failed to get TargetFrameworks for {projectPath} after {maxAttempts} attempts.");
        }
    }

    return []; // unreachable
}

static string GetRepositoryRoot([CallerFilePath] string? path = null)
    => FullPath.FromPath(Path.GetDirectoryName(path)!).FindRequiredGitRepositoryRoot();
