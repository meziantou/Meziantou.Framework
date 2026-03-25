#:sdk Meziantou.NET.Sdk
#:project ../src/Meziantou.Framework.FullPath/Meziantou.Framework.FullPath.csproj
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Meziantou.Framework;

var createPullRequest = false;
var numberOfCommits = 50;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--help" or "-h":
            Console.WriteLine("Usage: dotnet run update-versions.cs [-- --create-pull-request --number-of-commits <N>]");
            Console.WriteLine("Bumps package versions based on git commit history.");
            Console.WriteLine("Options:");
            Console.WriteLine("  --create-pull-request      Create or update a GitHub pull request");
            Console.WriteLine("  --number-of-commits <N>    Number of commits to analyze (default: 50)");
            return 0;
        case "--create-pull-request":
            createPullRequest = true;
            break;
        case "--number-of-commits" when i + 1 < args.Length:
            numberOfCommits = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
    }
}

var skippedCommits = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "ef6862b9195bd864e1f449317edc85a19041149f",
    "c4ac0bdd868a37b37e2eddd6ae99b7b888f3fd29",
};

var rootPath = GetRepositoryRoot();
var srcPath = Path.Combine(rootPath, "src");

// Get recent commits
var commits = RunAndCapture("git", ["log", $"--pretty=format:%H", "-n", numberOfCommits.ToString(CultureInfo.InvariantCulture)])
    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
    .Select(c => c.Trim().Trim('\''))
    .ToArray();
Console.WriteLine($"Commits loaded ({commits.Length} commits)");

// Initialize per-csproj tracking
var changesPerCsproj = new Dictionary<string, CsprojInfo>(StringComparer.OrdinalIgnoreCase);
var allCsprojFiles = new List<string>();

foreach (var file in Directory.EnumerateFiles(srcPath, "*.csproj", SearchOption.AllDirectories))
{
    Console.WriteLine($"Project file detected: {file}");
    allCsprojFiles.Add(file);
    changesPerCsproj[file] = new CsprojInfo();
}

// Build dependency graph
Console.WriteLine("Building dependency graph...");
var dependencyGraph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
var reverseDependencyGraph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

foreach (var csproj in allCsprojFiles)
{
    dependencyGraph[csproj] = GetProjectReferences(csproj);
    reverseDependencyGraph[csproj] = [];
}

foreach (var csproj in allCsprojFiles)
{
    foreach (var dependency in dependencyGraph[csproj])
    {
        if (reverseDependencyGraph.TryGetValue(dependency, out var list))
        {
            list.Add(csproj);
        }
    }
}

// Print dependency graph
Console.WriteLine("Dependency Graph:");
foreach (var csproj in dependencyGraph.Keys.OrderBy(k => k, StringComparer.Ordinal))
{
    var projectName = Path.GetFileNameWithoutExtension(csproj);
    var dependencies = dependencyGraph[csproj];
    if (dependencies.Count > 0)
    {
        foreach (var dependency in dependencies.OrderBy(d => d, StringComparer.Ordinal))
        {
            Console.WriteLine($"{projectName} -> {Path.GetFileNameWithoutExtension(dependency)}");
        }
    }
}

Console.WriteLine();

// Process commits
var i2 = 0;
foreach (var commit in commits)
{
    if (skippedCommits.Contains(commit))
    {
        Console.WriteLine($"Skipping commit {commit}");
        continue;
    }

    i2++;
    Console.WriteLine($"Processing commit {i2}/{commits.Length}: {commit}");

    var changes = RunAndCapture("git", ["diff", "--name-only", commit, $"{commit}~1"])
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Where(c => c.StartsWith("src/", StringComparison.Ordinal))
        .ToArray();

    // Process csproj files first to check for version changes
    foreach (var change in changes.Where(c => c.EndsWith(".csproj", StringComparison.Ordinal)))
    {
        string previousContent;
        try
        {
            previousContent = RunAndCapture("git", ["show", $"{commit}~1:{change}"]);
        }
        catch
        {
            previousContent = "";
        }

        Console.WriteLine($"Getting previous version of {change}");
        var previousVersion = GetVersion(previousContent);

        var currentContent = RunAndCapture("git", ["show", $"{commit}:{change}"]);
        Console.WriteLine($"Getting current version of {change}");
        var currentVersion = GetVersion(currentContent);

        if (previousVersion != currentVersion)
        {
            var csproj = GetCsproj(change);
            if (csproj is null)
                continue;

            if (!changesPerCsproj.TryGetValue(csproj, out var info))
            {
                info = new CsprojInfo();
                changesPerCsproj[csproj] = info;
            }

            info.StopProcessing = true;
        }
    }

    foreach (var change in changes)
    {
        var csproj = GetCsproj(change);
        if (csproj is null)
            continue;

        if (!changesPerCsproj.TryGetValue(csproj, out var info))
        {
            info = new CsprojInfo();
            changesPerCsproj[csproj] = info;
        }

        if (info.StopProcessing)
            continue;

        info.Commits.Add(commit);
    }
}

// First pass: identify projects with direct changes
var projectsToUpdate = new Dictionary<string, ProjectUpdateInfo>(StringComparer.OrdinalIgnoreCase);
foreach (var (csproj, info) in changesPerCsproj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
{
    if (info.Commits.Count > 0)
    {
        projectsToUpdate[csproj] = new ProjectUpdateInfo { Commits = info.Commits };
    }
}

// Second pass: identify transitive dependents
foreach (var csproj in projectsToUpdate.Keys.ToList())
{
    var dependents = GetTransitiveDependents(csproj);
    foreach (var dependent in dependents)
    {
        if (!projectsToUpdate.ContainsKey(dependent))
        {
            Console.WriteLine($"Project {dependent} will be updated due to dependency on {csproj}");
            var packageName = Path.GetFileNameWithoutExtension(csproj);
            projectsToUpdate[dependent] = new ProjectUpdateInfo
            {
                UpdatedDueToDependency = true,
                DependencySource = packageName,
            };
        }
    }
}

var updated = false;
var prMessage = new StringBuilder();

foreach (var (csproj, info) in projectsToUpdate.OrderBy(kv => kv.Key, StringComparer.Ordinal))
{
    if (IncrementVersion(csproj))
    {
        updated = true;

        if (prMessage.Length > 0)
        {
            prMessage.Append('\n');
        }

        var packageName = Path.GetFileNameWithoutExtension(csproj);
        prMessage.Append($"## {packageName}\n");

        if (info.UpdatedDueToDependency)
        {
            prMessage.Append($"- Updated due to dependency on {info.DependencySource}\n");
        }
        else
        {
            foreach (var commit in info.Commits.Distinct(StringComparer.Ordinal))
            {
                var message = RunAndCapture("git", ["log", "--format=%B", "-n", "1", commit]);
                message = Regex.Replace(message, @"\r?\n", "\n", RegexOptions.NonBacktracking);
                message = Regex.Replace(message, @"\s+", " ", RegexOptions.NonBacktracking);
                message = message.Replace("Co-authored-by: renovate[bot] <29139614+renovate[bot]@users.noreply.github.com>", "", StringComparison.OrdinalIgnoreCase);
                message = message.Trim();
                prMessage.Append($"- {commit}: {message}\n");
            }
        }
    }
}

if (updated)
{
    var prBody = prMessage.ToString();
    Console.WriteLine(prBody);

    if (createPullRequest)
    {
        Console.WriteLine("Commiting changes");
        RunProcess("git", ["config", "--global", "user.email", "git@meziantou.net"]);
        RunProcess("git", ["config", "--global", "user.name", "meziantou"]);
        RunProcess("git", ["add", "."]);
        RunProcess("git", ["commit", "-m", $"Bump package versions\n\n{prBody}"]);

        Console.WriteLine("Pushing changes");
        RunProcess("git", ["push", "origin", "main:generated/bump-package-versions", "--force"]);

        Thread.Sleep(10000);

        Console.WriteLine("Listing existing pull requests");
        var openPrJson = RunAndCapture("gh", ["pr", "list", "--repo", "meziantou/meziantou.framework", "--head", "generated/bump-package-versions", "--json", "number"]);

        // Simple JSON parsing — look for "number": <digits>
        var numberMatch = Regex.Match(openPrJson, @"""number""\s*:\s*(?<number>\d+)", RegexOptions.NonBacktracking | RegexOptions.ExplicitCapture);
        if (numberMatch.Success)
        {
            var prNumber = numberMatch.Groups["number"].Value;
            Console.WriteLine($"Editing existing pull request {prNumber}");
            RunProcess("gh", ["pr", "edit", prNumber, "--title", "Bump package versions", "--body", prBody]);
        }
        else
        {
            Console.WriteLine("Creating new pull request");
            RunProcess("gh", ["pr", "create", "--title", "Bump package versions", "--body", prBody, "--base", "main", "--head", "generated/bump-package-versions"]);
        }
    }
}
else
{
    Console.WriteLine("No version updated");
}

return 0;

// ── Helper methods ──

string? GetVersion(string fileContent)
{
    // Strip BOM-like artifacts
    fileContent = fileContent.Replace("\uFEFF", "", StringComparison.Ordinal);
    var xmlStart = fileContent.IndexOf('<', StringComparison.Ordinal);
    if (xmlStart > 0)
    {
        fileContent = fileContent[xmlStart..];
    }

    if (string.IsNullOrWhiteSpace(fileContent))
        return null;

    try
    {
        var doc = XDocument.Parse(fileContent);
        return doc.Root?.Descendants("Version").FirstOrDefault()?.Value;
    }
    catch
    {
        return null;
    }
}

bool IncrementVersion(string csprojPath)
{
    var doc = new XmlDocument { PreserveWhitespace = true };
    doc.Load(csprojPath);

    // Check for SkipAutoVersionUpdates
    var skipNodes = doc.GetElementsByTagName("SkipAutoVersionUpdates");
    if (skipNodes.Count > 0)
        return false;

    var versionNodes = doc.GetElementsByTagName("Version");
    if (versionNodes.Count == 0)
        return false;

    var versionNode = versionNodes[0]!;
    var version = versionNode.InnerText;
    var parts = version.Split('.');
    parts[^1] = (int.Parse(parts[^1], CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
    versionNode.InnerText = string.Join('.', parts);

    doc.Save(csprojPath);
    return true;
}

string? GetCsproj(string relativePath)
{
    var fullPath = Path.Combine(rootPath, relativePath);
    var current = fullPath;
    while (current is not null)
    {
        var parent = Path.GetDirectoryName(current);
        if (parent is not null && Directory.Exists(parent))
        {
            var csprojFiles = Directory.GetFiles(parent, "*.csproj");
            if (csprojFiles.Length > 0)
            {
                return csprojFiles[0];
            }
        }

        current = parent;
    }

    return null;
}

List<string> GetProjectReferences(string csprojPath)
{
    try
    {
        var doc = XDocument.Load(csprojPath);
        var result = new List<string>();
        foreach (var projRef in doc.Descendants("ProjectReference"))
        {
            var include = projRef.Attribute("Include")?.Value;
            if (string.IsNullOrEmpty(include))
                continue;

            var referencedProject = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(csprojPath)!, include));
            if (File.Exists(referencedProject) && referencedProject.StartsWith(srcPath, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(referencedProject);
            }
        }

        return result;
    }
    catch
    {
        return [];
    }
}

List<string> GetTransitiveDependents(string csproj, HashSet<string>? visited = null)
{
    visited ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (!visited.Add(csproj))
        return [];

    var dependents = new List<string>();
    if (reverseDependencyGraph.TryGetValue(csproj, out var directDependents))
    {
        foreach (var dependent in directDependents)
        {
            dependents.Add(dependent);
            dependents.AddRange(GetTransitiveDependents(dependent, visited));
        }
    }

    return dependents.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

static string RunAndCapture(string fileName, string[] arguments)
{
    var psi = new ProcessStartInfo(fileName)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    foreach (var arg in arguments)
    {
        psi.ArgumentList.Add(arg);
    }

    using var process = Process.Start(psi)!;
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();

    return output;
}

static void RunProcess(string fileName, string[] arguments)
{
    var psi = new ProcessStartInfo(fileName)
    {
        UseShellExecute = false,
    };
    foreach (var arg in arguments)
    {
        psi.ArgumentList.Add(arg);
    }

    using var process = Process.Start(psi)!;
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"Process '{fileName} {string.Join(' ', arguments)}' exited with code {process.ExitCode}");
    }
}

static FullPath GetRepositoryRoot() => FullPath.CurrentDirectory().FindRequiredGitRepositoryRoot();

internal sealed class CsprojInfo
{
    public List<string> Commits { get; } = [];
    public bool StopProcessing { get; set; }
}

internal sealed class ProjectUpdateInfo
{
    public List<string> Commits { get; init; } = [];
    public bool UpdatedDueToDependency { get; init; }
    public string? DependencySource { get; init; }
}
