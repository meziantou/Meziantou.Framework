#:project ../src/Meziantou.Framework.FullPath/Meziantou.Framework.FullPath.csproj
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using Meziantou.Framework;

if (args.Length > 0 && args[0] is "--help" or "-h")
{
    Console.WriteLine("Usage: dotnet run update-trimmable.cs");
    Console.WriteLine("Regenerates Samples/Trimmable/Trimmable.csproj with all trimmable projects.");
    Console.WriteLine("Exit code 1 if the file was not up-to-date.");
    return 0;
}

var rootPath = GetRepositoryRoot();
var srcPath = Path.Combine(rootPath, "src");
var trimmableCsprojPath = Path.Combine(rootPath, "Samples", "Trimmable", "Trimmable.csproj");
var trimmableWpfCsprojPath = Path.Combine(rootPath, "Samples", "Trimmable.Wpf", "Trimmable.Wpf.csproj");
var trimmableDir = Path.GetDirectoryName(trimmableCsprojPath)!;

// Find all IsTrimmable=true projects (excluding SkipTrimmableValidation=true)
var trimmableProjects = new List<string>();
foreach (var csproj in Directory.EnumerateFiles(srcPath, "*.csproj", SearchOption.AllDirectories))
{
    var doc = XDocument.Load(csproj);
    var isTrimmable = doc.Root?.Descendants("IsTrimmable").Any(e => string.Equals(e.Value, "true", StringComparison.OrdinalIgnoreCase)) ?? false;
    if (isTrimmable)
    {
        var skipValidation = doc.Root?.Descendants("SkipTrimmableValidation").Any(e => string.Equals(e.Value, "true", StringComparison.OrdinalIgnoreCase)) ?? false;
        if (!skipValidation)
        {
            trimmableProjects.Add(csproj);
        }
    }
}

// Find projects already referenced in Trimmable.Wpf
var wpfReferencedProjectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
if (File.Exists(trimmableWpfCsprojPath))
{
    var wpfDoc = XDocument.Load(trimmableWpfCsprojPath);
    var wpfDir = Path.GetDirectoryName(trimmableWpfCsprojPath)!;
    foreach (var projRef in wpfDoc.Descendants("ProjectReference"))
    {
        var include = projRef.Attribute("Include")?.Value;
        if (include is not null)
        {
            var refFullPath = Path.GetFullPath(Path.Combine(wpfDir, include));
            wpfReferencedProjectNames.Add(Path.GetFileNameWithoutExtension(refFullPath));
        }
    }
}

// Exclude projects already in Trimmable.Wpf, sort by name
var projectsForTrimmable = trimmableProjects
    .Where(p => !wpfReferencedProjectNames.Contains(Path.GetFileNameWithoutExtension(p)))
    .OrderBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.Ordinal)
    .ToList();

// Generate csproj content with LF line endings
var lf = "\n";
var sb = new StringBuilder();
sb.Append($"<Project Sdk=\"Meziantou.NET.Sdk\">{lf}");
sb.Append(lf);
sb.Append($"  <PropertyGroup>{lf}");
sb.Append($"    <OutputType>Exe</OutputType>{lf}");
sb.Append($"    <TargetFramework>$(LatestTargetFramework)</TargetFramework>{lf}");
sb.Append($"    <ImplicitUsings>enable</ImplicitUsings>{lf}");
sb.Append(lf);
sb.Append($"    <TrimmerSingleWarn>false</TrimmerSingleWarn>{lf}");
sb.Append($"    <PublishTrimmed>true</PublishTrimmed>{lf}");
sb.Append($"    <TrimMode>full</TrimMode>{lf}");
sb.Append($"  </PropertyGroup>{lf}");
sb.Append(lf);
sb.Append($"  <ItemGroup>{lf}");

foreach (var proj in projectsForTrimmable)
{
    var relativePath = Path.GetRelativePath(trimmableDir, proj).Replace('/', '\\');
    sb.Append($"    <ProjectReference Include=\"{relativePath}\" />{lf}");
}

sb.Append($"  </ItemGroup>{lf}");
sb.Append(lf);
sb.Append($"  <ItemGroup>{lf}");

foreach (var proj in projectsForTrimmable)
{
    var assemblyName = Path.GetFileNameWithoutExtension(proj);
    sb.Append($"    <TrimmerRootAssembly Include=\"{assemblyName}\" />{lf}");
}

sb.Append($"  </ItemGroup>{lf}");
sb.Append($"</Project>{lf}");

var newContent = sb.ToString();

// Read existing content, stripping BOM if present, normalizing line endings
var existingContent = "";
if (File.Exists(trimmableCsprojPath))
{
    var existingBytes = File.ReadAllBytes(trimmableCsprojPath);
    var offset = 0;
    if (existingBytes.Length >= 3 && existingBytes[0] == 0xEF && existingBytes[1] == 0xBB && existingBytes[2] == 0xBF)
    {
        offset = 3;
    }

    existingContent = Encoding.UTF8.GetString(existingBytes, offset, existingBytes.Length - offset);
}

var normalizedExisting = existingContent.Replace("\r\n", "\n");

if (normalizedExisting != newContent)
{
    File.WriteAllText(trimmableCsprojPath, newContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    Console.WriteLine("WARNING: Samples/Trimmable/Trimmable.csproj was not up-to-date");

    var psi = new System.Diagnostics.ProcessStartInfo("git", ["--no-pager", "diff", trimmableCsprojPath])
    {
        UseShellExecute = false,
    };
    using var process = System.Diagnostics.Process.Start(psi)!;
    process.WaitForExit();

    return 1;
}

Console.WriteLine("Samples/Trimmable/Trimmable.csproj is up-to-date");
return 0;

static string GetRepositoryRoot([CallerFilePath] string? path = null)
    => FullPath.FromPath(Path.GetDirectoryName(path)!).FindRequiredGitRepositoryRoot();
