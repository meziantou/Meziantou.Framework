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
    Console.WriteLine("Regenerates the NuGet packages table in README.md and updates tool README files with --help output.");
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
    var latestTfm = directoryBuildProps.Root?.Descendants("LatestTargetFramework").FirstOrDefault()?.Value;

    var toolProjects = new List<(string Csproj, string? ToolName, string ToolReadme)>();

    foreach (var csproj in Directory.EnumerateFiles(srcRootPath, "*.csproj", SearchOption.AllDirectories))
    {
        var doc = XDocument.Load(csproj);
        var packAsTool = doc.Root?.Descendants("PackAsTool").FirstOrDefault()?.Value;
        if (!string.Equals(packAsTool, "true", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

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

        var toolName = doc.Root?.Descendants("ToolCommandName").FirstOrDefault()?.Value;

        var toolReadme = FullPath.FromPath(csproj).Parent / "readme.md";
        if (!File.Exists(toolReadme))
        {
            Console.Error.WriteLine($"ERROR: Tool {csproj} does not have a readme.md file");
            return 1;
        }

        toolProjects.Add((csproj, toolName, toolReadme));
    }

    var editedFiles = 0;
    Parallel.ForEach(
        source: toolProjects,
        parallelOptions: new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
        },
        body: project =>
    {
        Console.WriteLine($"Processing {project.Csproj}");

        string[] runArgs = latestTfm is not null
            ? ["run", "--project", project.Csproj, "--framework", latestTfm, "--", "--help"]
            : ["run", "--project", project.Csproj, "--", "--help"];
        var helpText = RunProcessAndCaptureOutput("dotnet", runArgs, timeout: TimeSpan.FromMinutes(2));
        helpText = helpText.TrimEnd(' ', '\t', '\r', '\n');
        if (!string.IsNullOrEmpty(project.ToolName))
        {
            helpText = helpText.Replace(Path.GetFileNameWithoutExtension(project.Csproj), project.ToolName, StringComparison.Ordinal);
        }

        var toolReadmeContent = File.ReadAllText(project.ToolReadme);
        var pattern = new Regex("(?<=<!-- help -->)(.*?)(?=<!-- help -->)", RegexOptions.Singleline | RegexOptions.ExplicitCapture, Timeout.InfiniteTimeSpan);
        var newToolReadmeContent = pattern.Replace(toolReadmeContent, $"\n```\n{helpText}\n```\n");
        newToolReadmeContent = newToolReadmeContent.TrimEnd(' ', '\t', '\r', '\n');

        if (toolReadmeContent != newToolReadmeContent)
        {
            File.WriteAllText(project.ToolReadme, newToolReadmeContent);
            Console.WriteLine($"WARNING: {project.ToolReadme} was not up-to-date");
            Interlocked.Increment(ref editedFiles);
        }
    });

    if (editedFiles > 0)
    {
        Console.Error.WriteLine("ERROR: Some tool README files were not up-to-date");

        return 1;
    }

    return 0;
}

static string RunProcessAndCaptureOutput(string fileName, string[] arguments, TimeSpan timeout)
{
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
    if (!process.WaitForExit((int)timeout.TotalMilliseconds))
    {
        process.Kill(entireProcessTree: true);
        throw new TimeoutException($"Process '{fileName} {string.Join(' ', arguments)}' did not complete within {timeout}.");
    }

    var output = standardOutputTask.GetAwaiter().GetResult();
    var error = standardErrorTask.GetAwaiter().GetResult();
    if (process.ExitCode is not 0)
    {
        throw new InvalidOperationException($"Process '{fileName} {string.Join(' ', arguments)}' exited with code {process.ExitCode}.{Environment.NewLine}{error}");
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
}

static FullPath GetRepositoryRoot() => FullPath.CurrentDirectory().FindRequiredGitRepositoryRoot();
