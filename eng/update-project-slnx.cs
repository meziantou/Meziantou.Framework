#:project ../src/Meziantou.Framework.FullPath/Meziantou.Framework.FullPath.csproj
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
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
var toolsProjectSet = new HashSet<string>(toolsProjects, StringComparer.OrdinalIgnoreCase);

// Tool → src project mappings
var toolProjectsBySrcName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["Meziantou.Framework.Http.Hsts"] = "Meziantou.Framework.Http.Hsts.Generator",
    ["Meziantou.Framework.Unicode"] = "Meziantou.Framework.Unicode.Generator",
};

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

        var srcProject = srcProjects.FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p), srcName, StringComparison.OrdinalIgnoreCase));
        if (srcProject is not null)
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
var plans = new List<SlnxPlan>();

foreach (var project in srcProjects.OrderBy(p => p, StringComparer.Ordinal))
{
    var fileName = $"{Path.GetFileNameWithoutExtension(project)}.slnx";
    var outputFile = Path.Combine(outputRootPath, fileName);

    var srcProjectsToInclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { project };
    foreach (var dependency in GetTransitiveReferences(project, srcProjectSet))
    {
        srcProjectsToInclude.Add(dependency);
    }

    var testProjectsToInclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var testProject in testsBySrcProject[project])
    {
        testProjectsToInclude.Add(testProject);
        foreach (var dependency in GetTransitiveReferences(testProject, testsProjectSet))
        {
            testProjectsToInclude.Add(dependency);
        }
    }

    var toolProjectsToInclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var toolProject in toolsBySrcProject[project])
    {
        toolProjectsToInclude.Add(toolProject);
    }

    var projectsToAdd = new List<string>();
    projectsToAdd.AddRange(srcProjectsToInclude.OrderBy(p => p, StringComparer.Ordinal));
    projectsToAdd.AddRange(testProjectsToInclude.OrderBy(p => p, StringComparer.Ordinal));
    projectsToAdd.AddRange(toolProjectsToInclude.OrderBy(p => p, StringComparer.Ordinal));

    plans.Add(new SlnxPlan
    {
        ProjectName = Path.GetFileNameWithoutExtension(project),
        OutputFile = outputFile,
        ProjectsToAdd = projectsToAdd,
    });

    generatedFiles.Add(outputFile);
}

// Create solution files sequentially (dotnet new sln)
foreach (var plan in plans)
{
    if (File.Exists(plan.OutputFile))
    {
        File.Delete(plan.OutputFile);
    }

    RunProcess("dotnet", ["new", "sln", "-n", plan.ProjectName, "-o", Path.GetDirectoryName(plan.OutputFile)!, "--force"], captureOutput: true);
}

// Add projects in parallel with throttle limit
var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 8 };
Parallel.ForEach(plans, parallelOptions, plan =>
{
    if (!File.Exists(plan.OutputFile))
    {
        throw new InvalidOperationException($"Solution file not found: {plan.OutputFile}");
    }

    if (plan.ProjectsToAdd.Count > 0)
    {
        var addArgs = new List<string> { "sln", plan.OutputFile, "add" };
        addArgs.AddRange(plan.ProjectsToAdd);
        RunProcess("dotnet", addArgs.ToArray(), captureOutput: true);
    }
});

// Apply solution folders and normalize
foreach (var plan in plans)
{
    ApplySolutionFolders(plan.OutputFile, solutionFolderByProjectPath);
    NormalizeTextFile(plan.OutputFile);
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
    try
    {
        var doc = XDocument.Load(projectPath);
        var references = new List<string>();
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

        return references;
    }
    catch
    {
        return [];
    }
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

string ConvertToRelativePath(string path)
{
    return Path.GetRelativePath(rootPath, path).Replace('\\', '/');
}

void NormalizeTextFile(string path)
{
    var content = File.ReadAllText(path);
    content = content.Replace("\r\n", "\n").Replace("\r", "\n");

    if (content.Length > 0 && !content.EndsWith('\n'))
    {
        content += "\n";
    }

    File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}

void ApplySolutionFolders(string slnxPath, Dictionary<string, string> folderMapping)
{
    if (!File.Exists(slnxPath))
        return;

    var solutionDirectory = Path.GetDirectoryName(slnxPath)!;
    var doc = new XmlDocument();
    doc.Load(slnxPath);

    var solutionNode = doc.DocumentElement;
    if (solutionNode is null)
        return;

    // Build folder nodes map
    var folderNodesByName = new Dictionary<string, XmlElement>(StringComparer.Ordinal);
    foreach (XmlElement folderNode in solutionNode.SelectNodes("Folder")!)
    {
        var name = folderNode.GetAttribute("Name");
        if (!string.IsNullOrWhiteSpace(name))
        {
            folderNodesByName[name] = folderNode;
        }
    }

    // Build project nodes map by full path
    var projectNodesByFullPath = new Dictionary<string, XmlElement>(StringComparer.OrdinalIgnoreCase);
    foreach (XmlElement projectNode in doc.SelectNodes("//Project")!)
    {
        var projectPath = projectNode.GetAttribute("Path");
        if (string.IsNullOrEmpty(projectPath))
            continue;

        var fullPath = Path.GetFullPath(Path.Combine(solutionDirectory, projectPath));
        if (File.Exists(fullPath))
        {
            projectNodesByFullPath[fullPath] = projectNode;
        }
    }

    foreach (var (projectFullPath, projectNode) in projectNodesByFullPath)
    {
        if (string.IsNullOrWhiteSpace(projectFullPath))
            continue;

        string? folderName = null;
        if (!folderMapping.TryGetValue(projectFullPath, out folderName))
        {
            var relativePathFromRoot = ConvertToRelativePath(projectFullPath);
            folderName = relativePathFromRoot switch
            {
                _ when relativePathFromRoot.StartsWith("src/") => "/src/",
                _ when relativePathFromRoot.StartsWith("tests/SourceGenerators/") => "/tests/SourceGenerators/",
                _ when relativePathFromRoot.StartsWith("tests/") => "/tests/",
                _ when relativePathFromRoot.StartsWith("tools/") => "/tools/",
                _ when relativePathFromRoot.StartsWith("Samples/") => "/samples/",
                _ when relativePathFromRoot.StartsWith("benchmarks/") => "/benchmarks/",
                _ => null,
            };
        }

        if (string.IsNullOrWhiteSpace(folderName))
            continue;

        if (!folderNodesByName.TryGetValue(folderName, out var folderNode))
        {
            folderNode = doc.CreateElement("Folder");
            folderNode.SetAttribute("Name", folderName);
            solutionNode.AppendChild(folderNode);
            folderNodesByName[folderName] = folderNode;
        }

        if (projectNode.ParentNode != folderNode)
        {
            projectNode.ParentNode!.RemoveChild(projectNode);
            folderNode.AppendChild(projectNode);
        }
    }

    // Sort folders by name (ordinal)
    var folders = new List<XmlElement>();
    foreach (XmlElement folderNode in solutionNode.SelectNodes("Folder")!)
    {
        folders.Add(folderNode);
        solutionNode.RemoveChild(folderNode);
    }

    folders.Sort((a, b) => string.Compare(a.GetAttribute("Name"), b.GetAttribute("Name"), StringComparison.Ordinal));
    foreach (var folder in folders)
    {
        solutionNode.AppendChild(folder);
    }

    // Sort projects within each folder by path (ordinal)
    foreach (XmlElement folderNode in solutionNode.SelectNodes("Folder")!)
    {
        var projects = new List<XmlElement>();
        foreach (XmlElement projectNode in folderNode.SelectNodes("Project")!)
        {
            projects.Add(projectNode);
            folderNode.RemoveChild(projectNode);
        }

        projects.Sort((a, b) => string.Compare(a.GetAttribute("Path"), b.GetAttribute("Path"), StringComparison.Ordinal));
        foreach (var project in projects)
        {
            folderNode.AppendChild(project);
        }
    }

    SaveXmlDocument(doc, slnxPath);
}

void SaveXmlDocument(XmlDocument doc, string path)
{
    var settings = new XmlWriterSettings
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        OmitXmlDeclaration = true,
        NewLineChars = "\n",
        NewLineHandling = NewLineHandling.Replace,
        Indent = true,
    };

    using var writer = XmlWriter.Create(path, settings);
    doc.Save(writer);
}

static void RunProcess(string fileName, string[] arguments, bool captureOutput = false)
{
    var psi = new ProcessStartInfo(fileName)
    {
        UseShellExecute = false,
        RedirectStandardOutput = captureOutput,
        RedirectStandardError = captureOutput,
    };
    foreach (var arg in arguments)
    {
        psi.ArgumentList.Add(arg);
    }

    using var process = Process.Start(psi)!;
    if (captureOutput)
    {
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
    }

    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"Process '{fileName} {string.Join(' ', arguments)}' exited with code {process.ExitCode}");
    }
}

static string GetRepositoryRoot([CallerFilePath] string? path = null)
    => FullPath.FromPath(Path.GetDirectoryName(path)!).FindRequiredGitRepositoryRoot();

class SlnxPlan
{
    public required string ProjectName { get; set; }
    public required string OutputFile { get; set; }
    public required List<string> ProjectsToAdd { get; set; }
}
