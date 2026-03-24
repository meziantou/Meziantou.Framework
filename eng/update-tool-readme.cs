using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

if (args.Length > 0 && args[0] is "--help" or "-h")
{
    Console.WriteLine("Usage: dotnet run update-tool-readme.cs");
    Console.WriteLine("Updates tool README files with --help output.");
    Console.WriteLine("Exit code 1 if any README was not up-to-date.");
    return 0;
}

Environment.SetEnvironmentVariable("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1");
Environment.SetEnvironmentVariable("DOTNET_NOLOGO", "true");

var rootPath = Path.GetFullPath(Path.Combine(GetScriptDirectory(), ".."));
var srcRootPath = Path.Combine(rootPath, "src");
var editedFiles = 0;

foreach (var csproj in Directory.EnumerateFiles(srcRootPath, "*.csproj", SearchOption.AllDirectories))
{
    Console.WriteLine($"Processing {csproj}");

    var doc = XDocument.Load(csproj);
    var packAsTool = doc.Root?.Descendants("PackAsTool").FirstOrDefault()?.Value;
    if (!string.Equals(packAsTool, "true", StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    var toolName = doc.Root?.Descendants("ToolCommandName").FirstOrDefault()?.Value;

    var toolReadme = Path.Combine(Path.GetDirectoryName(csproj)!, "readme.md");
    if (!File.Exists(toolReadme))
    {
        Console.Error.WriteLine($"ERROR: Tool {csproj} does not have a readme.md file");
        return 1;
    }

    var helpText = RunProcessAndCaptureOutput("dotnet", ["run", "--project", csproj, "--framework", "net10.0", "--", "--help"]);
    helpText = helpText.TrimEnd(' ', '\t', '\r', '\n').Replace("]9;4;3;\\]9;4;0;\\", "");
    if (!string.IsNullOrEmpty(toolName))
    {
        helpText = helpText.Replace(Path.GetFileNameWithoutExtension(csproj), toolName);
    }

    var toolReadmeContent = File.ReadAllText(toolReadme);
    var pattern = new Regex("(?<=<!-- help -->)(.*?)(?=<!-- help -->)", RegexOptions.Singleline);
    var newToolReadmeContent = pattern.Replace(toolReadmeContent, $"\n```\n{helpText}\n```\n");
    newToolReadmeContent = newToolReadmeContent.TrimEnd(' ', '\t', '\r', '\n');

    if (toolReadmeContent != newToolReadmeContent)
    {
        File.WriteAllText(toolReadme, newToolReadmeContent);
        Console.WriteLine("WARNING: README was not up-to-date");
        editedFiles++;
    }
}

if (editedFiles > 0)
{
    Console.Error.WriteLine("ERROR: Some README files were not up-to-date");
    RunProcess("git", ["diff"]);
    return 1;
}

return 0;

static string RunProcessAndCaptureOutput(string fileName, string[] arguments)
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
}

static string GetScriptDirectory([CallerFilePath] string? path = null)
    => Path.GetDirectoryName(path)!;
