#:sdk Meziantou.NET.Sdk
#:project ../src/Meziantou.Framework.FullPath/Meziantou.Framework.FullPath.csproj
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Meziantou.Framework;

if (args.Length > 0 && args[0] is "--help" or "-h")
{
    Console.WriteLine("Usage: dotnet run update-readme.cs");
    Console.WriteLine("Regenerates the NuGet packages table in README.md and updates tool README files with recursive --help output.");
    Console.WriteLine("Exit code 1 if any README was not up-to-date.");
    return 0;
}

var rootPath = GetRepositoryRoot();
var srcRootPath = rootPath / "src";

var nugetReadmeTask = Task.Run(UpdateNuGetReadme);
var toolReadmeTask = Task.Run(UpdateToolReadmes);

await Task.WhenAll(nugetReadmeTask, toolReadmeTask);

var nugetReadmeResult = nugetReadmeTask.Result;
var toolReadmeResult = toolReadmeTask.Result;

if (nugetReadmeResult != 0 || toolReadmeResult != 0)
{
    RunProcess("git", ["--no-pager", "diff"]);
    return 1;
}

return 0;

int UpdateNuGetReadme()
{
    var readmePath = rootPath / "README.md";
    Console.WriteLine("[update-nuget-readme] Starting NuGet README update");
    var nugetUpdateStopwatch = Stopwatch.StartNew();

    // Enumerate all .csproj files under src/, sorted by file name (without extension)
    var csprojFiles = new List<string>(Directory.EnumerateFiles(srcRootPath, "*.csproj", SearchOption.AllDirectories));
    csprojFiles.Sort((a, b) => string.Compare(Path.GetFileNameWithoutExtension(a), Path.GetFileNameWithoutExtension(b), StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"[update-nuget-readme] NuGet discovery: found {csprojFiles.Count} project files");

    // Build the NuGet packages Markdown table
    var sb = new StringBuilder();
    sb.Append("| Name | Version | Readme |\n");
    sb.Append("| :--- | :---: | :---: |\n");
    var packableProjectCount = 0;
    var projectWithReadmeCount = 0;

    foreach (var csproj in csprojFiles)
    {
        var doc = XDocument.Load(csproj);
        var isPackable = doc.Root?.Descendants("IsPackable").FirstOrDefault()?.Value;
        if (string.Equals(isPackable, "False", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        packableProjectCount++;
        var fileName = Path.GetFileNameWithoutExtension(csproj);
        sb.Append($"| {fileName} | [![NuGet](https://img.shields.io/nuget/v/{fileName}.svg)](https://www.nuget.org/packages/{fileName}/) |");

        var packageReadmePath = FullPath.FromPath(csproj).Parent / "readme.md";
        if (File.Exists(packageReadmePath))
        {
            projectWithReadmeCount++;
            var relativePath = packageReadmePath.MakePathRelativeTo(rootPath).Replace('\\', '/');
            sb.Append($" [readme]({relativePath}) |\n");
        }
        else
        {
            sb.Append(" |\n");
        }
    }

    nugetUpdateStopwatch.Stop();
    Console.WriteLine($"[update-nuget-readme] NuGet metrics: packable={packableProjectCount}, with-readme={projectWithReadmeCount}, elapsed={nugetUpdateStopwatch.Elapsed.TotalSeconds:F2}s");

    // Read existing README
    var originalLines = File.ReadAllLines(readmePath);
    var originalContent = string.Join("\n", originalLines);

    // Rebuild content, replacing the # NuGet packages section
    var newContentBuilder = new StringBuilder();
    var isInPackages = false;

    foreach (var line in originalLines)
    {
        if (line == "# NuGet packages")
        {
            newContentBuilder.Append(line).Append('\n').Append('\n').Append(sb).Append('\n');
            isInPackages = true;
        }
        else
        {
            if (line.StartsWith('#'))
            {
                isInPackages = false;
            }
        }

        if (!isInPackages)
        {
            newContentBuilder.Append(line).Append('\n');
        }
    }

    var newContent = newContentBuilder.ToString().TrimEnd('\n', '\r');

    if (originalContent != newContent)
    {
        File.WriteAllText(readmePath, newContent);
        Console.WriteLine("WARNING: README.md was not up-to-date");

        return 1;
    }

    return 0;
}

int UpdateToolReadmes()
{
    // Read the latest TFM from Directory.Build.props so we don't hardcode "net10.0"
    var directoryBuildProps = XDocument.Load(rootPath / "Directory.Build.props");
    var latestTfm = directoryBuildProps.Root?.Descendants("LatestTargetFramework").FirstOrDefault()?.Value ?? throw new InvalidOperationException("Cannot find LatestTargetFramework");
    var maxDegreeOfParallelism = GetUpdateReadmeMaxDegreeOfParallelism();

    Console.WriteLine($"[update-tool-readme] Starting tool project discovery (max-degree-of-parallelism={maxDegreeOfParallelism})");
    var discoveryStopwatch = Stopwatch.StartNew();
    var csprojFiles = Directory.EnumerateFiles(srcRootPath, "*.csproj", SearchOption.AllDirectories).ToArray();
    var scannedProjectCount = csprojFiles.Length;
    var executableProjectCount = 0;
    var commandLineProjectCount = 0;
    var toolProjects = new ConcurrentBag<ToolProject>();
    var missingReadmeErrors = new ConcurrentBag<string>();
    var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism };
    Parallel.ForEach(csprojFiles, parallelOptions, csproj =>
    {
        var doc = XDocument.Load(csproj);
        if (!IsExecutableProject(csproj, latestTfm))
        {
            return;
        }

        Interlocked.Increment(ref executableProjectCount);
        var referencesSystemCommandLine = doc.Root?
            .Descendants("PackageReference")
            .Any(static node => string.Equals(
                node.Attribute("Include")?.Value ?? node.Attribute("Update")?.Value,
                "System.CommandLine",
                StringComparison.OrdinalIgnoreCase)) is true;
        if (!referencesSystemCommandLine)
        {
            return;
        }

        Interlocked.Increment(ref commandLineProjectCount);
        var toolName = doc.Root?.Descendants("ToolCommandName").FirstOrDefault()?.Value;

        var toolReadme = FullPath.FromPath(csproj).Parent / "readme.md";
        if (!File.Exists(toolReadme))
        {
            missingReadmeErrors.Add($"ERROR: Tool {csproj} does not have a readme.md file");
            return;
        }

        toolProjects.Add(new ToolProject(csproj, toolName, toolReadme));
    });
    var orderedToolProjects = toolProjects.OrderBy(static project => project.Csproj, StringComparer.OrdinalIgnoreCase).ToArray();

    discoveryStopwatch.Stop();
    Console.WriteLine($"[update-tool-readme] Discovery metrics: scanned={scannedProjectCount}, executable={executableProjectCount}, system-commandline={commandLineProjectCount}, tool-projects={orderedToolProjects.Length}, elapsed={discoveryStopwatch.Elapsed.TotalSeconds:F2}s");

    if (!missingReadmeErrors.IsEmpty)
    {
        foreach (var error in missingReadmeErrors.OrderBy(static error => error, StringComparer.Ordinal))
        {
            Console.Error.WriteLine(error);
        }

        return 1;
    }

    Console.WriteLine("[update-tool-readme] Starting parallel --help generation");
    var generationStopwatch = Stopwatch.StartNew();
    var helpSectionPattern = new Regex("(?<=<!-- help -->)(.*?)(?=<!-- help -->)", RegexOptions.Singleline | RegexOptions.ExplicitCapture, Timeout.InfiniteTimeSpan);
    var readmeUpdates = new ToolReadmeUpdate?[orderedToolProjects.Length];
    Parallel.For(0, orderedToolProjects.Length, parallelOptions, index =>
    {
        var project = orderedToolProjects[index];
        Console.WriteLine($"[update-tool-readme] [{index + 1}/{orderedToolProjects.Length}] Building tool project: {project.Csproj}");
        string[] buildArgs = ["build", project.Csproj, "--framework", latestTfm, "-p:RunAnalyzers=false", "-p:RunAnalyzersDuringBuild=false"];
        _ = RunProcessAndCaptureOutput("dotnet", buildArgs, timeout: TimeSpan.FromMinutes(2));

        Console.WriteLine($"[update-tool-readme] [{index + 1}/{orderedToolProjects.Length}] Generating help output for tool project: {project.Csproj}");

        var helpMarkdown = BuildToolHelpMarkdown(project.Csproj, latestTfm, project.ToolName);

        var toolReadmeContent = File.ReadAllText(project.ToolReadme);
        var newToolReadmeContent = helpSectionPattern.Replace(toolReadmeContent, $"\n{helpMarkdown}\n");
        newToolReadmeContent = newToolReadmeContent.TrimEnd(' ', '\t', '\r', '\n');
        readmeUpdates[index] = new ToolReadmeUpdate(project.ToolReadme, toolReadmeContent, newToolReadmeContent);
    });
    generationStopwatch.Stop();
    Console.WriteLine($"[update-tool-readme] --help generation metrics: tool-projects={orderedToolProjects.Length}, elapsed={generationStopwatch.Elapsed.TotalSeconds:F2}s");

    var editedFiles = 0;
    for (var i = 0; i < orderedToolProjects.Length; i++)
    {
        var update = readmeUpdates[i] ?? throw new InvalidOperationException($"Internal error: missing README update result for index {i}.");
        if (update.HasChanges)
        {
            File.WriteAllText(update.ToolReadme, update.NewContent);
            Console.WriteLine($"WARNING: {update.ToolReadme} was not up-to-date");
            editedFiles++;
        }
    }

    if (editedFiles > 0)
    {
        Console.Error.WriteLine("ERROR: Some tool README files were not up-to-date");

        return 1;
    }

    return 0;
}

static int GetUpdateReadmeMaxDegreeOfParallelism()
{
    var value = Environment.GetEnvironmentVariable("UPDATE_README_MAX_PARALLELISM");
    if (string.IsNullOrWhiteSpace(value))
    {
        return Math.Clamp(Environment.ProcessorCount, 1, 4);
    }

    if (int.TryParse(value, out var parsedValue) && parsedValue > 0)
    {
        return parsedValue;
    }

    throw new InvalidOperationException($"Environment variable UPDATE_README_MAX_PARALLELISM must be a positive integer. Current value: '{value}'.");
}

static string BuildToolHelpMarkdown(string csproj, string latestTfm, string? toolName)
{
    var sections = new List<(string[] CommandPath, string HelpText)>();
    var visitedCommandPaths = new HashSet<string>(StringComparer.Ordinal);
    AppendToolHelpSection(sections, visitedCommandPaths, csproj, latestTfm, toolName, []);

    var hasRootSection = false;
    for (var i = 0; i < sections.Count; i++)
    {
        var section = sections[i];
        if (string.IsNullOrWhiteSpace(section.HelpText))
        {
            var commandPathDisplay = FormatCommandPath(section.CommandPath);
            throw new InvalidOperationException($"Tool '{csproj}' produced an empty help section for command '{commandPathDisplay}'.");
        }

        if (section.CommandPath.Length is 0)
        {
            hasRootSection = true;
        }
    }

    if (!hasRootSection)
    {
        throw new InvalidOperationException($"Tool '{csproj}' did not produce a root help section.");
    }

    var sb = new StringBuilder();
    sb.AppendLine("## Help");
    sb.AppendLine();

    for (var i = 0; i < sections.Count; i++)
    {
        var section = sections[i];
        if (section.CommandPath.Length is not 0)
        {
            sb.Append("### ");
            sb.AppendLine(string.Join(' ', section.CommandPath));
            sb.AppendLine();
        }

        sb.AppendLine("```");
        sb.AppendLine(section.HelpText);
        sb.AppendLine("```");

        if (i < sections.Count - 1)
        {
            sb.AppendLine();
        }
    }

    return sb.ToString().TrimEnd('\r', '\n');
}

static void AppendToolHelpSection(
    List<(string[] CommandPath, string HelpText)> sections,
    HashSet<string> visitedCommandPaths,
    string csproj,
    string latestTfm,
    string? toolName,
    string[] commandPath)
{
    var commandPathKey = string.Join('\u001F', commandPath);
    if (!visitedCommandPaths.Add(commandPathKey))
    {
        return;
    }

    var helpText = GetToolHelpText(csproj, latestTfm, toolName, commandPath);
    sections.Add((commandPath, helpText));

    foreach (var subcommandName in GetSubcommandNames(csproj, latestTfm, commandPath))
    {
        var subcommandPath = new string[commandPath.Length + 1];
        commandPath.CopyTo(subcommandPath, 0);
        subcommandPath[^1] = subcommandName;
        AppendToolHelpSection(sections, visitedCommandPaths, csproj, latestTfm, toolName, subcommandPath);
    }
}

static string GetToolHelpText(string csproj, string latestTfm, string? toolName, string[] commandPath)
{
    var runArgs = new List<string> { "run", "--no-build", "--project", csproj, "--framework", latestTfm, "--" };
    runArgs.AddRange(commandPath);
    runArgs.Add("--help");
    Console.WriteLine($"[update-tool-readme] Getting --help for '{csproj}' command '{FormatCommandPath(commandPath)}' using: dotnet {string.Join(' ', runArgs)}");

    var (standardOutput, standardError) = RunProcessAndCaptureOutputs("dotnet", [.. runArgs], timeout: TimeSpan.FromMinutes(2));
    var helpText = string.IsNullOrWhiteSpace(standardOutput) ? standardError : standardOutput;
    helpText = TrimEndOfLines(helpText).TrimEnd('\r', '\n');
    if (string.IsNullOrWhiteSpace(helpText))
    {
        throw new InvalidOperationException($"Process 'dotnet {string.Join(' ', runArgs)}' produced empty help output.");
    }

    if (!string.IsNullOrEmpty(toolName))
    {
        helpText = helpText.Replace(Path.GetFileNameWithoutExtension(csproj), toolName, StringComparison.Ordinal);
    }

    return helpText;
}

static IReadOnlyList<string> GetSubcommandNames(string csproj, string latestTfm, string[] commandPath)
{
    var commandLine = commandPath.Length is 0 ? string.Empty : string.Join(' ', commandPath) + " ";
    var suggestDirective = $"[suggest:{commandLine.Length}]";
    var runArgs = new List<string> { "run", "--no-build", "--project", csproj, "--framework", latestTfm, "--", suggestDirective, commandLine };
    Console.WriteLine($"[update-tool-readme] Getting subcommands for '{csproj}' command '{FormatCommandPath(commandPath)}' using: dotnet {string.Join(' ', runArgs)}");
    var (standardOutput, standardError) = RunProcessAndCaptureOutputs("dotnet", [.. runArgs], timeout: TimeSpan.FromMinutes(2));
    var suggestionsOutput = string.IsNullOrWhiteSpace(standardOutput) ? standardError : standardOutput;
    suggestionsOutput = TrimEndOfLines(suggestionsOutput).TrimEnd('\r', '\n');

    var result = new List<string>();
    var existingSuggestions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var rawLine in suggestionsOutput.Split('\n'))
    {
        var suggestion = rawLine.TrimEnd('\r').Trim();
        if (string.IsNullOrEmpty(suggestion))
        {
            continue;
        }

        if (suggestion.StartsWith("-", StringComparison.Ordinal) || suggestion.StartsWith("/", StringComparison.Ordinal))
        {
            continue;
        }

        if (suggestion.Contains(' ', StringComparison.Ordinal) || suggestion.Contains('\t', StringComparison.Ordinal))
        {
            continue;
        }

        if (existingSuggestions.Add(suggestion))
        {
            result.Add(suggestion);
        }
    }

    return result;
}

static string RunProcessAndCaptureOutput(string fileName, string[] arguments, TimeSpan? timeout = null)
    => RunProcessAndCaptureOutputs(fileName, arguments, timeout).StandardOutput;

static (string StandardOutput, string StandardError) RunProcessAndCaptureOutputs(string fileName, string[] arguments, TimeSpan? timeout = null)
{
    var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(2);
    var psi = new ProcessStartInfo(fileName)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        Environment = { ["TERM"] = "dumb" },
    };
    foreach (var arg in arguments)
    {
        psi.ArgumentList.Add(arg);
    }

    using var process = Process.Start(psi)!;
    var standardOutputTask = process.StandardOutput.ReadToEndAsync();
    var standardErrorTask = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit((int)effectiveTimeout.TotalMilliseconds))
    {
        process.Kill(entireProcessTree: true);
        throw new TimeoutException($"Process '{fileName} {string.Join(' ', arguments)}' did not complete within {effectiveTimeout}.");
    }

    var output = standardOutputTask.GetAwaiter().GetResult();
    var error = standardErrorTask.GetAwaiter().GetResult();
    if (process.ExitCode is not 0)
    {
        throw new InvalidOperationException($"Process '{fileName} {string.Join(' ', arguments)}' exited with code {process.ExitCode}.{Environment.NewLine}{error}");
    }

    return (output, error);
}

static bool IsExecutableProject(string csproj, string targetFramework)
{
    string[] arguments = ["msbuild", csproj, "-nologo", "-v:q", "-p:TargetFramework=" + targetFramework, "-getProperty:OutputKind", "-getProperty:OutputType"];

    var output = RunProcessAndCaptureOutput("dotnet", arguments);
    var outputKind = ExtractMsBuildPropertyValue(output, "OutputKind");
    if (!string.IsNullOrEmpty(outputKind))
    {
        return string.Equals(outputKind, "Exe", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(outputKind, "ConsoleApplication", StringComparison.OrdinalIgnoreCase);
    }

    var outputType = ExtractMsBuildPropertyValue(output, "OutputType");
    return string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase);
}

static string? ExtractMsBuildPropertyValue(string output, string propertyName)
{
    var match = Regex.Match(output, $"\"{Regex.Escape(propertyName)}\"\\s*:\\s*\"(?<value>[^\"]*)\"", RegexOptions.CultureInvariant, Timeout.InfiniteTimeSpan);
    return match.Success ? match.Groups["value"].Value : null;
}

static string TrimEndOfLines(string text) =>
    Regex.Replace(text, "[ \\t]+(?=\\r?\\n|$)", string.Empty, RegexOptions.CultureInvariant, Timeout.InfiniteTimeSpan);

static string FormatCommandPath(string[] commandPath) =>
    commandPath.Length is 0 ? "(root)" : string.Join(' ', commandPath);

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
}

static FullPath GetRepositoryRoot() => FullPath.CurrentDirectory().FindRequiredGitRepositoryRoot();

readonly record struct ToolProject(string Csproj, string? ToolName, string ToolReadme);

readonly record struct ToolReadmeUpdate(string ToolReadme, string OriginalContent, string NewContent)
{
    public bool HasChanges => !string.Equals(OriginalContent, NewContent, StringComparison.Ordinal);
}
