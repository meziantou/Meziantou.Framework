#:sdk Meziantou.NET.Sdk
#:project ../src/Meziantou.Framework.FullPath/Meziantou.Framework.FullPath.csproj
using System.Diagnostics;
using System.Text;
using Meziantou.Framework;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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

    // Enumerate all .csproj files under src/, sorted by file name (without extension)
    var csprojFiles = new List<string>(Directory.EnumerateFiles(srcRootPath, "*.csproj", SearchOption.AllDirectories));
    csprojFiles.Sort((a, b) => string.Compare(Path.GetFileNameWithoutExtension(a), Path.GetFileNameWithoutExtension(b), StringComparison.OrdinalIgnoreCase));

    // Build the NuGet packages Markdown table
    var sb = new StringBuilder();
    sb.Append("| Name | Version | Readme |\n");
    sb.Append("| :--- | :---: | :---: |\n");

    foreach (var csproj in csprojFiles)
    {
        var doc = XDocument.Load(csproj);
        var isPackable = doc.Root?.Descendants("IsPackable").FirstOrDefault()?.Value;
        if (string.Equals(isPackable, "False", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var fileName = Path.GetFileNameWithoutExtension(csproj);
        sb.Append($"| {fileName} | [![NuGet](https://img.shields.io/nuget/v/{fileName}.svg)](https://www.nuget.org/packages/{fileName}/) |");

        var packageReadmePath = FullPath.FromPath(csproj).Parent / "readme.md";
        if (File.Exists(packageReadmePath))
        {
            var relativePath = packageReadmePath.MakePathRelativeTo(rootPath).Replace('\\', '/');
            sb.Append($" [readme]({relativePath}) |\n");
        }
        else
        {
            sb.Append(" |\n");
        }
    }

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

    Console.WriteLine("[update-readme] Starting tool project discovery");
    var discoveryStopwatch = Stopwatch.StartNew();
    var scannedProjectCount = 0;
    var executableProjectCount = 0;
    var commandLineProjectCount = 0;
    var toolProjects = new List<(string Csproj, string? ToolName, string ToolReadme)>();

    foreach (var csproj in Directory.EnumerateFiles(srcRootPath, "*.csproj", SearchOption.AllDirectories))
    {
        scannedProjectCount++;
        var doc = XDocument.Load(csproj);
        if (!IsExecutableProject(csproj, latestTfm))
        {
            continue;
        }

        executableProjectCount++;
        var referencesSystemCommandLine = doc.Root?
            .Descendants("PackageReference")
            .Any(static node => string.Equals(
                node.Attribute("Include")?.Value ?? node.Attribute("Update")?.Value,
                "System.CommandLine",
                StringComparison.OrdinalIgnoreCase)) is true;
        if (!referencesSystemCommandLine)
        {
            continue;
        }

        commandLineProjectCount++;
        var toolName = doc.Root?.Descendants("ToolCommandName").FirstOrDefault()?.Value;

        var toolReadme = FullPath.FromPath(csproj).Parent / "readme.md";
        if (!File.Exists(toolReadme))
        {
            Console.Error.WriteLine($"ERROR: Tool {csproj} does not have a readme.md file");
            return 1;
        }

        toolProjects.Add((csproj, toolName, toolReadme));
    }

    discoveryStopwatch.Stop();
    Console.WriteLine($"[update-readme] Discovery metrics: scanned={scannedProjectCount}, executable={executableProjectCount}, system-commandline={commandLineProjectCount}, tool-projects={toolProjects.Count}, elapsed={discoveryStopwatch.Elapsed.TotalSeconds:F2}s");

    var editedFiles = 0;
    for (var i = 0; i < toolProjects.Count; i++)
    {
        var project = toolProjects[i];
        Console.WriteLine($"[update-readme] [{i + 1}/{toolProjects.Count}] Building tool project: {project.Csproj}");
        string[] buildArgs = ["build", project.Csproj, "--framework", latestTfm, "-p:RunAnalyzers=false", "-p:RunAnalyzersDuringBuild=false"];
        _ = RunProcessAndCaptureOutput("dotnet", buildArgs, timeout: TimeSpan.FromMinutes(2));

        Console.WriteLine($"[update-readme] [{i + 1}/{toolProjects.Count}] Generating help output for tool project: {project.Csproj}");

        var helpMarkdown = BuildToolHelpMarkdown(project.Csproj, latestTfm, project.ToolName);

        var toolReadmeContent = File.ReadAllText(project.ToolReadme);
        var pattern = new Regex("(?<=<!-- help -->)(.*?)(?=<!-- help -->)", RegexOptions.Singleline | RegexOptions.ExplicitCapture, Timeout.InfiniteTimeSpan);
        var newToolReadmeContent = pattern.Replace(toolReadmeContent, $"\n{helpMarkdown}\n");
        newToolReadmeContent = newToolReadmeContent.TrimEnd(' ', '\t', '\r', '\n');

        if (toolReadmeContent != newToolReadmeContent)
        {
            File.WriteAllText(project.ToolReadme, newToolReadmeContent);
            Console.WriteLine($"WARNING: {project.ToolReadme} was not up-to-date");
            Interlocked.Increment(ref editedFiles);
        }
    }

    if (editedFiles > 0)
    {
        Console.Error.WriteLine("ERROR: Some tool README files were not up-to-date");

        return 1;
    }

    return 0;
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
    Console.WriteLine($"[update-readme] Getting --help for '{csproj}' command '{FormatCommandPath(commandPath)}' using: dotnet {string.Join(' ', runArgs)}");

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
    Console.WriteLine($"[update-readme] Getting subcommands for '{csproj}' command '{FormatCommandPath(commandPath)}' using: dotnet {string.Join(' ', runArgs)}");
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
