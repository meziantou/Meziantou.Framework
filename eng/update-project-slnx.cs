#:project ../src/Meziantou.Framework.FullPath/Meziantou.Framework.FullPath.csproj
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using Meziantou.Framework;

var outputPath = "slnx";
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--help" or "-h":
            Console.WriteLine("Usage: dotnet run update-project-slnx.cs [-- --output-path <path>]");
            Console.WriteLine("Generates per-project .slnx solution files.");
            Console.WriteLine("Options:");
            Console.WriteLine("  --output-path <path>  Output folder for .slnx files (default: slnx)");
            return 0;
        case "--output-path" when i + 1 < args.Length:
            outputPath = args[++i];
            break;
    }
}

var rootPath = GetRepositoryRoot();
var srcRootPath = Path.Combine(rootPath, "src");
var testsRootPath = Path.Combine(rootPath, "tests");
var toolsRootPath = Path.Combine(rootPath, "tools");
var outputRootPath = Path.Combine(rootPath, outputPath);
var mainSolutionPath = Path.Combine(rootPath, "Meziantou.Framework.slnx");

// Get all project files
var srcProjects = GetProjectFiles(srcRootPath);
var testsProjects = GetProjectFiles(testsRootPath);
var toolsProjects = GetProjectFiles(toolsRootPath);

// Build solution folder mapping from the main solution
var solutionFolderByProjectPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
if (File.Exists(mainSolutionPath))
{
    var mainDoc = XDocument.Load(mainSolutionPath);
    var solutionElement = mainDoc.Root;
    if (solutionElement is not null)
    {
        foreach (var folderElement in solutionElement.Elements("Folder"))
        {
            var folderName = folderElement.Attribute("Name")?.Value;
            if (string.IsNullOrWhiteSpace(folderName))
                continue;

            foreach (var projectElement in folderElement.Elements("Project"))
            {
                var projectPath = projectElement.Attribute("Path")?.Value;
                if (projectPath is null)
                    continue;

                var fullPath = Path.GetFullPath(Path.Combine(rootPath, projectPath));
                if (File.Exists(fullPath))
                {
                    solutionFolderByProjectPath[fullPath] = folderName;
                }
            }
        }
    }
}

var srcProjectSet = new HashSet<string>(srcProjects, StringComparer.OrdinalIgnoreCase);
var testsProjectSet = new HashSet<string>(testsProjects, StringComparer.OrdinalIgnoreCase);

// Cache for parsed project references
var projectReferencesCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

// Tool → src project mappings
var toolProjectsBySrcName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["Meziantou.Framework.Http.Hsts"] = "Meziantou.Framework.Http.Hsts.Generator",
    ["Meziantou.Framework.Unicode"] = "Meziantou.Framework.Unicode.Generator",
};

var srcProjectByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
foreach (var project in srcProjects)
{
    srcProjectByName[Path.GetFileNameWithoutExtension(project)] = project;
}

var toolsBySrcProject = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
foreach (var project in srcProjects)
{
    toolsBySrcProject[project] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

foreach (var toolProject in toolsProjects)
{
    var toolProjectName = Path.GetFileNameWithoutExtension(toolProject);
    foreach (var (srcName, toolName) in toolProjectsBySrcName)
    {
        if (!string.Equals(toolName, toolProjectName, StringComparison.OrdinalIgnoreCase))
            continue;

        if (srcProjectByName.TryGetValue(srcName, out var srcProject))
        {
            toolsBySrcProject[srcProject].Add(toolProject);
        }
    }
}

// Build test → src mapping
var testsBySrcProject = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
foreach (var project in srcProjects)
{
    testsBySrcProject[project] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

foreach (var testProject in testsProjects)
{
    foreach (var reference in GetProjectReferencesInSet(testProject, srcProjectSet))
    {
        testsBySrcProject[reference].Add(testProject);
    }
}

if (!Directory.Exists(outputRootPath))
{
    Directory.CreateDirectory(outputRootPath);
}

var generatedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

// Set of all known projects for unrestricted transitive closure
var allProjectSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
allProjectSet.UnionWith(srcProjectSet);
allProjectSet.UnionWith(testsProjectSet);
allProjectSet.UnionWith(toolsProjects);

foreach (var project in srcProjects.OrderBy(p => p, StringComparer.Ordinal))
{
    var fileName = $"{Path.GetFileNameWithoutExtension(project)}.slnx";
    var outputFile = Path.Combine(outputRootPath, fileName);
    generatedFiles.Add(outputFile);

    // Seed with the src project and its direct src transitive deps
    var allProjectsToInclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { project };
    foreach (var dependency in GetTransitiveReferences(project, srcProjectSet))
    {
        allProjectsToInclude.Add(dependency);
    }

    // Add test projects and their transitive test deps
    foreach (var testProject in testsBySrcProject[project])
    {
        allProjectsToInclude.Add(testProject);
        foreach (var dependency in GetTransitiveReferences(testProject, testsProjectSet))
        {
            allProjectsToInclude.Add(dependency);
        }
    }

    // Add tool projects
    foreach (var toolProject in toolsBySrcProject[project])
    {
        allProjectsToInclude.Add(toolProject);
    }

    // Expand: include all transitive src deps of every included project
    // (mimics `dotnet sln add` which resolves full ProjectReference chains)
    var expanded = new HashSet<string>(allProjectsToInclude, StringComparer.OrdinalIgnoreCase);
    foreach (var included in allProjectsToInclude)
    {
        foreach (var dep in GetTransitiveReferences(included, allProjectSet))
        {
            expanded.Add(dep);
        }
    }

    // Group projects by folder
    var projectsByFolder = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    AddProjectsToFolders(projectsByFolder, expanded);

    // Generate .slnx XML directly
    var content = GenerateSlnx(outputFile, projectsByFolder);
    WriteIfChanged(outputFile, content);
}

// Clean up stale .slnx files
if (Directory.Exists(outputRootPath))
{
    foreach (var file in Directory.EnumerateFiles(outputRootPath, "*.slnx"))
    {
        if (!generatedFiles.Contains(file))
        {
            File.Delete(file);
        }
    }
}

return 0;

// ── Helper methods ──

List<string> GetProjectFiles(string rootDir)
{
    if (!Directory.Exists(rootDir))
        return [];

    var result = new List<string>();
    result.AddRange(Directory.EnumerateFiles(rootDir, "*.csproj", SearchOption.AllDirectories));
    result.AddRange(Directory.EnumerateFiles(rootDir, "*.fsproj", SearchOption.AllDirectories));
    return result;
}

List<string> GetProjectReferences(string projectPath)
{
    if (projectReferencesCache.TryGetValue(projectPath, out var cached))
        return cached;

    List<string> references;
    try
    {
        var doc = XDocument.Load(projectPath);
        references = [];
        foreach (var projRef in doc.Descendants("ProjectReference"))
        {
            var include = projRef.Attribute("Include")?.Value;
            if (string.IsNullOrEmpty(include))
                continue;

            var candidatePath = Path.Combine(Path.GetDirectoryName(projectPath)!, include);
            if (!File.Exists(candidatePath))
                continue;

            references.Add(Path.GetFullPath(candidatePath));
        }
    }
    catch
    {
        references = [];
    }

    projectReferencesCache[projectPath] = references;
    return references;
}

List<string> GetProjectReferencesInSet(string projectPath, HashSet<string> projectSet)
{
    return GetProjectReferences(projectPath).Where(r => projectSet.Contains(r)).ToList();
}

HashSet<string> GetTransitiveReferences(string projectPath, HashSet<string> projectSet)
{
    var queue = new Queue<string>();
    foreach (var reference in GetProjectReferencesInSet(projectPath, projectSet))
    {
        queue.Enqueue(reference);
    }

    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    while (queue.Count > 0)
    {
        var current = queue.Dequeue();
        if (!result.Add(current))
            continue;

        foreach (var reference in GetProjectReferencesInSet(current, projectSet))
        {
            queue.Enqueue(reference);
        }
    }

    return result;
}

string GetFolderForProject(string projectFullPath)
{
    if (solutionFolderByProjectPath.TryGetValue(projectFullPath, out var folderName))
        return folderName;

    var relativePathFromRoot = Path.GetRelativePath(rootPath, projectFullPath).Replace('\\', '/');
    return relativePathFromRoot switch
    {
        _ when relativePathFromRoot.StartsWith("src/") => "/src/",
        _ when relativePathFromRoot.StartsWith("tests/SourceGenerators/") => "/tests/SourceGenerators/",
        _ when relativePathFromRoot.StartsWith("tests/") => "/tests/",
        _ when relativePathFromRoot.StartsWith("tools/") => "/tools/",
        _ when relativePathFromRoot.StartsWith("Samples/") => "/samples/",
        _ when relativePathFromRoot.StartsWith("benchmarks/") => "/benchmarks/",
        _ => "/other/",
    };
}

void AddProjectsToFolders(Dictionary<string, List<string>> projectsByFolder, HashSet<string> projects)
{
    foreach (var project in projects)
    {
        var folder = GetFolderForProject(project);
        if (!projectsByFolder.TryGetValue(folder, out var list))
        {
            list = [];
            projectsByFolder[folder] = list;
        }

        list.Add(project);
    }
}

string GenerateSlnx(string outputFile, Dictionary<string, List<string>> projectsByFolder)
{
    var outputDir = Path.GetDirectoryName(outputFile)!;
    var sb = new StringBuilder();
    sb.Append("<Solution>\n");

    foreach (var folder in projectsByFolder.Keys.OrderBy(f => f, StringComparer.Ordinal))
    {
        var projects = projectsByFolder[folder];
        sb.Append("  <Folder Name=\"").Append(folder).Append("\">\n");

        var relativePaths = new List<string>(projects.Count);
        foreach (var project in projects)
        {
            relativePaths.Add(Path.GetRelativePath(outputDir, project).Replace('\\', '/'));
        }

        relativePaths.Sort(StringComparer.Ordinal);
        foreach (var relativePath in relativePaths)
        {
            sb.Append("    <Project Path=\"").Append(relativePath).Append("\" />\n");
        }

        sb.Append("  </Folder>\n");
    }

    sb.Append("</Solution>\n");
    return sb.ToString();
}

void WriteIfChanged(string path, string content)
{
    if (File.Exists(path))
    {
        var existing = File.ReadAllText(path);
        if (string.Equals(existing, content, StringComparison.Ordinal))
            return;
    }

    File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}

static string GetRepositoryRoot([CallerFilePath] string? path = null)
    => FullPath.FromPath(Path.GetDirectoryName(path)!).FindRequiredGitRepositoryRoot();
