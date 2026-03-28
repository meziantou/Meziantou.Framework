#:sdk Meziantou.NET.Sdk
#:project ../src/Meziantou.Framework.FullPath/Meziantou.Framework.FullPath.csproj
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using Meziantou.Framework;

if (args.Length > 0 && args[0] is "--help" or "-h")
{
    Console.WriteLine("Usage: dotnet run validate-nuget.cs");
    Console.WriteLine("Validates built NuGet packages for correctness.");
    Console.WriteLine("Requires NuGetDirectory and GITHUB_TOKEN environment variables.");
    return 0;
}

var nugetDirectory = FullPath.FromPath(
    Environment.GetEnvironmentVariable("NuGetDirectory")
        ?? throw new InvalidOperationException("NuGetDirectory environment variable is not set"));
var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? "";
var isDeltaBuild = Environment.GetEnvironmentVariable("NUGET_VALIDATION_IS_DELTA") is "true" or "1";

var rootPath = GetRepositoryRoot();
var nupkgFiles = Directory.GetFiles(nugetDirectory, "*.nupkg");

if (isDeltaBuild && nupkgFiles.Length == 0)
{
    Console.WriteLine("Delta build produced no NuGet package. Skipping validation.");
    return 0;
}

// Validate source generator packages
var generators = new[] { "Meziantou.Framework.StronglyTypedId", "Meziantou.Framework.FastEnumToStringGenerator" };
foreach (var generator in generators)
{
    Console.WriteLine($"Checking {generator}");

    var packagePattern = new Regex($@"{Regex.Escape(generator)}\.[0-9][0-9a-zA-Z.\-]*\.nupkg$", RegexOptions.NonBacktracking);
    var packagePath = Directory.EnumerateFiles(nugetDirectory)
        .FirstOrDefault(f => packagePattern.IsMatch(f));
    if (packagePath is null)
    {
        if (isDeltaBuild)
        {
            Console.WriteLine($"Skipping {generator} validation because package is absent in delta build output.");
            continue;
        }

        throw new InvalidOperationException($"Package not found for {generator}");
    }

    var annotationPath = rootPath / "src" / $"{generator}.Annotations";
    var tfms = RunAndCapture("dotnet", ["build", "--getProperty:TargetFrameworks", annotationPath]).Trim().Split(';');

    using (var zipFile = ZipFile.OpenRead(packagePath))
    {
        var entries = zipFile.Entries.Select(e => e.FullName).ToList();
        foreach (var tfm in tfms)
        {
            var hasEntry = entries.Any(e => e.StartsWith($"lib/{tfm}/", StringComparison.Ordinal));
            if (!hasEntry)
            {
                Console.Error.WriteLine($"ERROR: Package does not contain a lib/{tfm}/ entry");
                return 1;
            }
        }
    }
}

// Ensure InlineSnapshot package contains the prompt folder
{
    var packagePattern = new Regex(@"Meziantou\.Framework\.InlineSnapshotTesting\.[0-9][0-9a-zA-Z.\-]*\.nupkg$", RegexOptions.NonBacktracking);
    var packagePath = Directory.EnumerateFiles(nugetDirectory)
        .FirstOrDefault(f => packagePattern.IsMatch(f));
    if (packagePath is null)
    {
        if (isDeltaBuild)
        {
            Console.WriteLine("Skipping InlineSnapshotTesting prompt folder validation because package is absent in delta build output.");
        }
        else
        {
            throw new InvalidOperationException("InlineSnapshotTesting package not found");
        }
    }
    else
    {
        using var zipFile = ZipFile.OpenRead(packagePath);
        var hasPrompt = zipFile.Entries.Any(e => e.FullName.StartsWith("prompt/", StringComparison.Ordinal));
        if (!hasPrompt)
        {
            Console.Error.WriteLine("ERROR: Package does not contain a prompt/ entry");
            return 1;
        }
    }
}

// General validation
Console.WriteLine("Validating NuGet packages");
if (nupkgFiles.Length == 0)
{
    if (isDeltaBuild)
    {
        Console.WriteLine("No NuGet packages to validate in delta build output.");
        return 0;
    }

    throw new InvalidOperationException("No NuGet packages found to validate");
}

RunProcess("dotnet", ["tool", "update", "Meziantou.Framework.NuGetPackageValidation.Tool", "--global", "--no-cache", "--add-source", nugetDirectory]);

var validateArgs = new[] { "meziantou.validate-nuget-package" }
    .Concat(nupkgFiles)
    .Concat(["--excluded-rules", "ReadmeMustBeSet,TagsMustBeSet", "--excluded-rule-ids", "52", $"--github-token={githubToken}", "--only-report-errors"])
    .ToArray();

var exitCode = RunProcessWithExitCode(validateArgs[0], validateArgs[1..]);
if (exitCode != 0)
{
    return 1;
}

return 0;

static string RunAndCapture(string fileName, string[] arguments)
{
    var psi = new ProcessStartInfo(fileName)
    {
        RedirectStandardOutput = true,
        UseShellExecute = false,
    };
    foreach (var arg in arguments)
    {
        psi.ArgumentList.Add(arg);
    }

    using var process = Process.Start(psi)!;
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"Process '{fileName} {string.Join(' ', arguments)}' exited with code {process.ExitCode}");
    }

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

static int RunProcessWithExitCode(string fileName, string[] arguments)
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

    return process.ExitCode;
}

static FullPath GetRepositoryRoot() => FullPath.CurrentDirectory().FindRequiredGitRepositoryRoot();
