#!/usr/bin/env dotnet
#:sdk Meziantou.NET.Sdk
#:project ../src/Meziantou.Framework.FullPath/Meziantou.Framework.FullPath.csproj
#:project ../src/Meziantou.Framework.DependencyScanning/Meziantou.Framework.DependencyScanning.csproj
// File-based apps are AOT-compatible by default, which disables the reflection-based JSON serialization the scanners use
#:property PublishAot=false
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Meziantou.Framework;
using Meziantou.Framework.DependencyScanning;

// Finds what a pull request changes that needs a closer review:
// - dependencies: a dependency that no file of the base revision references (a version bump does not count),
// - tests: a file of tests/ that is modified, deleted or renamed (adding files does not count).
// The pr-review-labels workflow runs it on the merge commit of the pull request with a token that can write, so it
// must only read the files of the pull request, never run them.
string? baseRevision = null;
var headRevision = "HEAD";

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--help" or "-h":
            Console.WriteLine("Usage: dotnet run ./eng/detect-pr-review-labels.cs -- --base <revision> [--head <revision>]");
            Console.WriteLine("Detects new dependencies and changes to existing tests between two revisions.");
            Console.WriteLine("Writes dependencies=true|false and tests=true|false to $GITHUB_OUTPUT when it is set.");
            Console.WriteLine("Options:");
            Console.WriteLine("  --base <revision>   The revision of the target branch");
            Console.WriteLine("  --head <revision>   The revision of the pull request (default: HEAD)");
            return 0;
        case "--base" when i + 1 < args.Length:
            baseRevision = args[++i];
            break;
        case "--head" when i + 1 < args.Length:
            headRevision = args[++i];
            break;
        default:
            Console.Error.WriteLine($"Unknown argument '{args[i]}'. Use --help to list the options.");
            return 1;
    }
}

if (baseRevision is null)
{
    Console.Error.WriteLine("--base is required. Use --help to list the options.");
    return 1;
}

var rootPath = FullPath.CurrentDirectory().FindRequiredGitRepositoryRoot();
var summary = new StringBuilder();

var changedTests = GetChangedTests(rootPath, baseRevision, headRevision);
var newDependencies = await GetNewDependenciesAsync(rootPath, baseRevision, headRevision);

summary.AppendLine("## Review labels");
summary.AppendLine();
if (newDependencies.Count is 0)
{
    summary.AppendLine("- No new dependency");
}
else
{
    summary.AppendLine(CultureInfo.InvariantCulture, $"- `review:dependencies`: {newDependencies.Count} new dependencies");
    foreach (var dependency in newDependencies)
    {
        summary.AppendLine($"  - {dependency.Type} `{dependency.Name}` ({dependency.FilePath})");
    }
}

if (changedTests.Count is 0)
{
    summary.AppendLine("- No existing test changed");
}
else
{
    summary.AppendLine(CultureInfo.InvariantCulture, $"- `review:tests`: {changedTests.Count} files of tests/ modified, deleted or renamed");
    foreach (var (status, path) in changedTests)
    {
        summary.AppendLine($"  - {status} `{path}`");
    }
}

Console.WriteLine(summary);
AppendToGitHubFile("GITHUB_STEP_SUMMARY", summary.ToString());
AppendToGitHubFile("GITHUB_OUTPUT", $"dependencies={(newDependencies.Count > 0 ? "true" : "false")}\ntests={(changedTests.Count > 0 ? "true" : "false")}\n");
return 0;

static List<(string Status, string Path)> GetChangedTests(FullPath rootPath, string baseRevision, string headRevision)
{
    // Without rename detection, a renamed file is a deleted file and an added file, so it is reported
    var output = RunGit(rootPath, ["diff", "--name-status", "--no-renames", "-z", baseRevision, headRevision, "--", "tests/"]);
    var parts = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    var result = new List<(string Status, string Path)>();
    for (var i = 0; i + 1 < parts.Length; i += 2)
    {
        var status = parts[i];
        if (status is not "A")
        {
            result.Add((status, parts[i + 1]));
        }
    }

    return result;
}

static async Task<List<NewDependency>> GetNewDependenciesAsync(FullPath rootPath, string baseRevision, string headRevision)
{
    var tempDirectory = FullPath.FromPath(Directory.CreateTempSubdirectory("pr-review-labels-").FullName);
    var baseDirectory = tempDirectory / "base";
    var headDirectory = tempDirectory / "head";
    try
    {
        RunGit(rootPath, ["worktree", "add", "--detach", "--quiet", baseDirectory, baseRevision]);
        RunGit(rootPath, ["worktree", "add", "--detach", "--quiet", headDirectory, headRevision]);

        var baseDependencies = await ScanAsync(baseDirectory);
        var headDependencies = await ScanAsync(headDirectory);

        var baseKeys = baseDependencies.Select(GetKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. headDependencies
            .Where(dependency => !baseKeys.Contains(GetKey(dependency)))
            .GroupBy(GetKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(GetFilePath, StringComparer.Ordinal).First())
            .Select(dependency => new NewDependency(dependency.Type, dependency.Name!, GetRelativeFilePath(headDirectory, dependency)))
            .OrderBy(dependency => dependency.Type)
            .ThenBy(dependency => dependency.Name, StringComparer.OrdinalIgnoreCase)];
    }
    finally
    {
        foreach (var directory in new[] { baseDirectory, headDirectory })
        {
            if (Directory.Exists(directory))
            {
                RunGit(rootPath, ["worktree", "remove", "--force", directory]);
            }
        }

        RunGit(rootPath, ["worktree", "prune"]);
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    static string GetKey(Dependency dependency) => $"{dependency.Type}|{dependency.Name}";

    static string GetFilePath(Dependency dependency) => (dependency.NameLocation ?? dependency.VersionLocation)?.FilePath ?? "";

    static string GetRelativeFilePath(FullPath rootDirectory, Dependency dependency)
    {
        var filePath = GetFilePath(dependency);
        return filePath is "" ? "" : FullPath.FromPath(filePath).MakePathRelativeTo(rootDirectory).Replace('\\', '/');
    }
}

static async Task<Dependency[]> ScanAsync(FullPath directory)
{
    var options = new ScannerOptions
    {
        // These types describe the repository itself, not the packages, images or actions it depends on
        ExcludedDependencyTypes =
        [
            DependencyType.Unknown,
            DependencyType.DotNetSdk,
            DependencyType.DotNetTargetFramework,
            DependencyType.MSBuildProjectReference,
            DependencyType.DotNetAssemblyReference,
            DependencyType.RenovateConfiguration,
        ],
        ShouldRecursePredicate = static (ref entry) => entry.FileName is not ".git",
        OnFileScanFailed = (filePath, exception) => Console.Error.WriteLine($"WARNING: Cannot scan '{Path.GetRelativePath(directory, filePath)}': {exception.Message}"),
    };

    var dependencies = await DependencyScanner.ScanDirectoryAsync(directory, options);
    return [.. dependencies.Where(dependency => !string.IsNullOrEmpty(dependency.Name))];
}

static void AppendToGitHubFile(string variableName, string content)
{
    var path = Environment.GetEnvironmentVariable(variableName);
    if (!string.IsNullOrEmpty(path))
    {
        File.AppendAllText(path, content);
    }
}

static string RunGit(FullPath workingDirectory, string[] arguments)
{
    var psi = new ProcessStartInfo("git")
    {
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    psi.ArgumentList.Add("--no-pager");
    foreach (var argument in arguments)
    {
        psi.ArgumentList.Add(argument);
    }

    using var process = Process.Start(psi)!;
    var standardError = process.StandardError.ReadToEndAsync();
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode is not 0)
    {
        throw new InvalidOperationException($"'git {string.Join(' ', arguments)}' failed with exit code {process.ExitCode}: {standardError.Result}");
    }

    return output;
}

internal sealed record NewDependency(DependencyType Type, string Name, string FilePath);
