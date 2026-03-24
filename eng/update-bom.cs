#:sdk Meziantou.NET.Sdk
#:project ../src/Meziantou.Framework.FullPath/Meziantou.Framework.FullPath.csproj
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Meziantou.Framework;

if (args.Length > 0 && args[0] is "--help" or "-h")
{
    Console.WriteLine("Usage: dotnet run update-bom.cs");
    Console.WriteLine("Removes UTF-8 BOM from source files under src/.");
    Console.WriteLine("Exit code equals the number of files that were edited.");
    return 0;
}

var rootPath = GetRepositoryRoot();
var srcRootPath = Path.Combine(rootPath, "src");
var editedFiles = 0;

var extensions = new[] { "*.cs", "*.csproj", "*.fsproj", "*.proj", "*.props", "*.targets", "*.save", "*.slnx", "*.ps1", "*.yml", "*.yaml", "*.md", "*.json" };
var files = extensions.SelectMany(ext => Directory.EnumerateFiles(srcRootPath, ext, SearchOption.AllDirectories));

Parallel.ForEach(files, file =>
{
    Console.WriteLine($"Processing {file}");
    var content = File.ReadAllBytes(file);
    if (content.Length < 3)
    {
        return;
    }

    if (content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
    {
        Console.WriteLine($"WARNING: File {file} contains BOM. Removing it.");
        File.WriteAllBytes(file, content.AsSpan(3).ToArray());
        Interlocked.Increment(ref editedFiles);
    }
});

return editedFiles;

static string GetRepositoryRoot([CallerFilePath] string? path = null)
    => FullPath.FromPath(Path.GetDirectoryName(path)!).FindRequiredGitRepositoryRoot();
