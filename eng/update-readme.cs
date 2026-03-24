using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;

if (args.Length > 0 && args[0] is "--help" or "-h")
{
    Console.WriteLine("Usage: dotnet run update-readme.cs");
    Console.WriteLine("Regenerates the NuGet packages table in README.md.");
    Console.WriteLine("Exit code 1 if the README was not up-to-date.");
    return 0;
}

var rootPath = Path.GetFullPath(Path.Combine(GetScriptDirectory(), ".."));
var srcRootPath = Path.Combine(rootPath, "src");
var readmePath = Path.Combine(rootPath, "README.md");

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

    var packageReadmePath = Path.Combine(Path.GetDirectoryName(csproj)!, "readme.md");
    if (File.Exists(packageReadmePath))
    {
        var relativePath = Path.GetRelativePath(rootPath, packageReadmePath).Replace('\\', '/');
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
    Console.WriteLine("WARNING: README was not up-to-date");
    RunProcess("git", ["--no-pager", "diff", readmePath]);
    return 1;
}

return 0;

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
