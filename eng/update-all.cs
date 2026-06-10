#!/usr/bin/env dotnet
#:sdk Meziantou.NET.Sdk
#:project ../src/Meziantou.Framework.FullPath/Meziantou.Framework.FullPath.csproj
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Meziantou.Framework;

const string StepReadme = "readme";
const string StepTrimmable = "trimmable";
const string StepSlnx = "slnx";
const string StepTemplates = "templates";
const string StepBom = "bom";
const string StepValidateTestProjects = "validate-testprojects";
const string AnalyzerRulesSectionMarker = "<!-- analyzer-rules -->";
string[] defaultSteps = [StepReadme, StepTrimmable, StepSlnx, StepTemplates, StepBom];
string[] knownSteps = [StepReadme, StepTrimmable, StepSlnx, StepTemplates, StepBom, StepValidateTestProjects];
var updatedFiles = new ConcurrentBag<string>();
var outputPath = "slnx";
var selectedSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--help" or "-h":
            Console.WriteLine("Usage: dotnet run ./eng/update-all.cs [-- --step <name>] [--output-path <path>]");
            Console.WriteLine("Runs source generation/update steps.");
            Console.WriteLine("Available step names:");
            Console.WriteLine("  readme, trimmable, slnx, templates, bom, validate-testprojects, all");
            Console.WriteLine("Default order: readme -> trimmable -> slnx -> templates -> bom");
            return 0;

        case "--output-path" when i + 1 < args.Length:
            outputPath = args[++i];
            break;

        case "--step" when i + 1 < args.Length:
            selectedSteps.Add(args[++i]);
            break;

        default:
            throw new InvalidOperationException($"Unknown argument '{args[i]}'. Use --help for usage.");
    }
}

if (selectedSteps.Contains("all"))
{
    selectedSteps.Clear();
}

var rootPath = GetRepositoryRoot();

bool ShouldRunStep(string stepName) => selectedSteps.Count is 0 ? defaultSteps.Contains(stepName, StringComparer.OrdinalIgnoreCase) : selectedSteps.Contains(stepName);

var tasks = new List<Task>();
if (ShouldRunStep(StepReadme))
{
    Console.WriteLine("[update-all] Running readme");
    tasks.Add(Task.Run(() => RunUpdateReadmeStep(rootPath)));
}

if (ShouldRunStep(StepTrimmable))
{
    Console.WriteLine("[update-all] Running trimmable");
    tasks.Add(Task.Run(() => RunUpdateTrimmableStep(rootPath)));
}

if (ShouldRunStep(StepSlnx))
{
    Console.WriteLine("[update-all] Running slnx");
    tasks.Add(Task.Run(() => RunUpdateProjectSlnxStep(rootPath, outputPath)));
}

if (ShouldRunStep(StepValidateTestProjects))
{
    Console.WriteLine("[update-all] Running validate-testprojects");
    tasks.Add(Task.Run(() => RunValidateTestProjectsConfigurationStep(rootPath)));
}

if (ShouldRunStep(StepTemplates))
{
    Console.WriteLine("[update-all] Running templates");
    tasks.Add(Task.Run(() => RunTemplateStep(rootPath)));
}

await Task.WhenAll(tasks);
if (ShouldRunStep(StepBom))
{
    Console.WriteLine("[update-all] Running bom");
    RunUpdateBomStep(rootPath);
}

if (selectedSteps.Count > 0)
{
    foreach (var step in selectedSteps)
    {
        if (!knownSteps.Contains(step, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unknown step '{step}'. Use --help to list valid steps.");
        }
    }
}

if (!updatedFiles.IsEmpty)
{
    Console.WriteLine("One or more steps reported errors, you can fix them and re-run the tool to check that all steps are successful.");
    Console.WriteLine("dotnet run ./eng/update-all.cs");

    Console.WriteLine("Updated files:");
    foreach (var file in updatedFiles)
    {
        Console.WriteLine($"- {file}");
    }

    return 1;
}

return 0;

void RunTemplateStep(FullPath rootPath)
{
    var latestTargetFramework = GetLatestTargetFramework(rootPath);
    var templatesRootPath = rootPath / "src";
    var templateFiles = Directory.EnumerateFiles(templatesRootPath, "*.tt", SearchOption.AllDirectories)
        .Select(FullPath.FromPath)
        .OrderBy(path => path.Value, StringComparer.Ordinal)
        .ToArray();

    Parallel.ForEach(templateFiles, templateFile =>
    {
        var relativeTemplatePath = templateFile.MakePathRelativeTo(rootPath);
        Console.WriteLine($"[update-all] Transforming {relativeTemplatePath}");
        var exitCode = RunProcessAndReturnExitCode(
            rootPath,
            "dotnet",
            [
                "run",
                "--project",
                "./src/Meziantou.Framework.Templating.Tool",
                "--framework",
                latestTargetFramework,
                "--",
                "--input",
                templateFile.Value,
                "--start-code-block-delimiter",
                "<#",
                "--end-code-block-delimiter",
                "#>",
            ]);

        if (exitCode != 0)
        {
            updatedFiles.Add(relativeTemplatePath);
        }
    });
}

void RunUpdateBomStep(FullPath rootPath)
{
    var srcRootPath = rootPath / "src";

    string[] extensions = ["*.cs", "*.csproj", "*.fsproj", "*.proj", "*.props", "*.targets", "*.save", "*.slnx", "*.ps1", "*.yml", "*.yaml", "*.md", "*.json"];
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
            updatedFiles.Add(FullPath.FromPath(file).MakePathRelativeTo(rootPath));
        }
    });
}

void RunUpdateTrimmableStep(FullPath rootPath)
{
    var srcPath = rootPath / "src";
    var trimmableCsprojPath = rootPath / "tests" / "Trimmable" / "Trimmable.csproj";
    var trimmableWpfCsprojPath = rootPath / "tests" / "Trimmable.Wpf" / "Trimmable.Wpf.csproj";
    var trimmableDir = trimmableCsprojPath.Parent;

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

    var wpfReferencedProjectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (File.Exists(trimmableWpfCsprojPath))
    {
        var wpfDoc = XDocument.Load(trimmableWpfCsprojPath);
        var wpfDir = trimmableWpfCsprojPath.Parent;
        foreach (var projRef in wpfDoc.Descendants("ProjectReference"))
        {
            var include = projRef.Attribute("Include")?.Value;
            if (include is not null)
            {
                string refFullPath = wpfDir / include;
                wpfReferencedProjectNames.Add(Path.GetFileNameWithoutExtension(refFullPath));
            }
        }
    }

    var projectsForTrimmable = trimmableProjects
        .Where(p => !wpfReferencedProjectNames.Contains(Path.GetFileNameWithoutExtension(p)))
        .OrderBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.Ordinal)
        .ToList();

    var lf = "\n";
    var sb = new StringBuilder();
    sb.Append($"<Project Sdk=\"Meziantou.NET.Sdk\">{lf}");
    sb.Append(lf);
    sb.Append($"  <PropertyGroup>{lf}");
    sb.Append($"    <OutputType>Exe</OutputType>{lf}");
    sb.Append($"    <TargetFramework>$(LatestTargetFramework)</TargetFramework>{lf}");
    sb.Append($"    <ImplicitUsings>enable</ImplicitUsings>{lf}");
    sb.Append($"    <IncludeDefaultTestReferences>false</IncludeDefaultTestReferences>{lf}");
    sb.Append(lf);
    sb.Append($"    <TrimmerSingleWarn>false</TrimmerSingleWarn>{lf}");
    sb.Append($"    <PublishTrimmed>true</PublishTrimmed>{lf}");
    sb.Append($"    <TrimMode>full</TrimMode>{lf}");
    sb.Append($"    <ExcludeFromGeneratedSlnx>true</ExcludeFromGeneratedSlnx>{lf}");
    sb.Append($"  </PropertyGroup>{lf}");
    sb.Append(lf);
    sb.Append($"  <ItemGroup>{lf}");

    foreach (var proj in projectsForTrimmable)
    {
        var relativePath = FullPath.FromPath(proj).MakePathRelativeTo(trimmableDir).Replace('/', '\\');
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

    var existingContent = "";
    if (File.Exists(trimmableCsprojPath))
    {
        var existingBytes = File.ReadAllBytes(trimmableCsprojPath);
        var offset = 0;
        if (existingBytes is [0xEF, 0xBB, 0xBF, ..])
        {
            offset = 3;
        }

        existingContent = Encoding.UTF8.GetString(existingBytes, offset, existingBytes.Length - offset);
    }

    var normalizedExisting = existingContent.Replace("\r\n", "\n", StringComparison.Ordinal);

    if (normalizedExisting != newContent)
    {
        File.WriteAllText(trimmableCsprojPath, newContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine("WARNING: tests/Trimmable/Trimmable.csproj was not up-to-date");

        var psi = new ProcessStartInfo("git", ["--no-pager", "diff", trimmableCsprojPath])
        {
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        process.WaitForExit();

        updatedFiles.Add("tests/Trimmable/Trimmable.csproj");
    }
}

void RunUpdateProjectSlnxStep(FullPath rootPath, string outputPath)
{
    var srcRootPath = rootPath / "src";
    var testsRootPath = rootPath / "tests";
    var toolsRootPath = rootPath / "tools";
    var outputRootPath = rootPath / outputPath;
    var mainSolutionPath = rootPath / "Meziantou.Framework.slnx";

    var srcProjects = GetProjectFiles(srcRootPath);
    var testsProjects = GetProjectFiles(testsRootPath);
    var toolsProjects = GetProjectFiles(toolsRootPath);

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

                    string fullPath = rootPath / projectPath;
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
    var projectReferencesCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    var toolProjectsBySrcName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Meziantou.Framework.HtmlToMarkdown"] = "Meziantou.Framework.HtmlToMarkdown.Emoji.Generator",
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

    var testsBySrcProject = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    foreach (var project in srcProjects)
    {
        testsBySrcProject[project] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    foreach (var testProject in testsProjects.Where(project => !IsExcludedProject(project)))
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
    var allProjectSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    allProjectSet.UnionWith(srcProjectSet);
    allProjectSet.UnionWith(testsProjectSet);
    allProjectSet.UnionWith(toolsProjects);

    foreach (var project in srcProjects.OrderBy(p => p, StringComparer.Ordinal))
    {
        var fileName = $"{Path.GetFileNameWithoutExtension(project)}.slnx";
        var outputFile = outputRootPath / fileName;
        generatedFiles.Add(outputFile);

        var allProjectsToInclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { project };
        foreach (var dependency in GetTransitiveReferences(project, srcProjectSet))
        {
            allProjectsToInclude.Add(dependency);
        }

        foreach (var testProject in testsBySrcProject[project])
        {
            allProjectsToInclude.Add(testProject);
            foreach (var dependency in GetTransitiveReferences(testProject, testsProjectSet))
            {
                allProjectsToInclude.Add(dependency);
            }
        }

        foreach (var toolProject in toolsBySrcProject[project])
        {
            allProjectsToInclude.Add(toolProject);
        }

        var expanded = new HashSet<string>(allProjectsToInclude, StringComparer.OrdinalIgnoreCase);
        foreach (var included in allProjectsToInclude)
        {
            foreach (var dep in GetTransitiveReferences(included, allProjectSet))
            {
                expanded.Add(dep);
            }
        }

        var projectsByFolder = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        AddProjectsToFolders(projectsByFolder, expanded);

        var content = GenerateSlnx(outputFile, projectsByFolder);
        WriteIfChanged(outputFile, content);
    }

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

    UpdateMainSolution();

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

                var normalizedInclude = include.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                var candidatePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, normalizedInclude));
                if (!File.Exists(candidatePath))
                    continue;

                references.Add(candidatePath);
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

        var relativePathFromRoot = FullPath.FromPath(projectFullPath).MakePathRelativeTo(rootPath).Replace('\\', '/');
        return relativePathFromRoot switch
        {
            _ when relativePathFromRoot.StartsWith("src/", StringComparison.Ordinal) => "/src/",
            _ when relativePathFromRoot.StartsWith("tests/SourceGenerators/", StringComparison.Ordinal) => "/tests/SourceGenerators/",
            _ when relativePathFromRoot.StartsWith("tests/", StringComparison.Ordinal) => "/tests/",
            _ when relativePathFromRoot.StartsWith("tools/", StringComparison.Ordinal) => "/tools/",
            _ when relativePathFromRoot.StartsWith("samples/", StringComparison.Ordinal) => "/samples/",
            _ when relativePathFromRoot.StartsWith("benchmarks/", StringComparison.Ordinal) => "/benchmarks/",
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
        var outputDir = FullPath.FromPath(outputFile).Parent;
        var sb = new StringBuilder();
        sb.Append("<Solution>\n");

        foreach (var folder in projectsByFolder.Keys.OrderBy(f => f, StringComparer.Ordinal))
        {
            var projects = projectsByFolder[folder];
            sb.Append("  <Folder Name=\"").Append(folder).Append("\">\n");

            var relativePaths = new List<string>(projects.Count);
            foreach (var project in projects)
            {
                relativePaths.Add(FullPath.FromPath(project).MakePathRelativeTo(outputDir).Replace('\\', '/'));
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
        updatedFiles.Add(FullPath.FromPath(path).MakePathRelativeTo(rootPath));
    }

    void UpdateMainSolution()
    {
        var allDiskProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in new[] { "src", "tests", "tools", "samples", "benchmarks" })
        {
            allDiskProjects.UnionWith(GetProjectFiles(rootPath / dir));
        }

        if (!File.Exists(mainSolutionPath))
            return;

        var doc = XDocument.Load(mainSolutionPath);
        var solutionElement = doc.Root;
        if (solutionElement is null)
            return;

        var projectsInSlnx = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folderElement in solutionElement.Elements("Folder"))
        {
            foreach (var projectElement in folderElement.Elements("Project").ToList())
            {
                var projectPath = projectElement.Attribute("Path")?.Value;
                if (projectPath is null)
                    continue;

                string fullPath = rootPath / projectPath;
                if (File.Exists(fullPath))
                {
                    projectsInSlnx.Add(fullPath);
                }
                else
                {
                    projectElement.Remove();
                }
            }
        }

        foreach (var projectOnDisk in allDiskProjects)
        {
            if (projectsInSlnx.Contains(projectOnDisk))
                continue;

            var relativePath = FullPath.FromPath(projectOnDisk).MakePathRelativeTo(rootPath).Replace('\\', '/');
            var folderName = GetMainSlnxFolder(relativePath);

            var folderElement = solutionElement.Elements("Folder")
                .FirstOrDefault(f => string.Equals(f.Attribute("Name")?.Value, folderName, StringComparison.Ordinal));

            if (folderElement is null)
            {
                folderElement = new XElement("Folder", new XAttribute("Name", folderName));
                solutionElement.Add(folderElement);
            }

            folderElement.Add(new XElement("Project", new XAttribute("Path", relativePath)));
        }

        foreach (var folderElement in solutionElement.Elements("Folder"))
        {
            var fileElements = folderElement.Elements("File").ToList();
            var projectElements = folderElement.Elements("Project").ToList();

            foreach (var elem in fileElements)
                elem.Remove();
            foreach (var elem in projectElements)
                elem.Remove();

            foreach (var fileElem in fileElements.OrderBy(f => f.Attribute("Path")?.Value, StringComparer.OrdinalIgnoreCase))
            {
                folderElement.Add(fileElem);
            }

            foreach (var projElem in projectElements.OrderBy(p => p.Attribute("Path")?.Value, StringComparer.OrdinalIgnoreCase))
            {
                folderElement.Add(projElem);
            }
        }

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = true,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
        };

        using var sw = new StringWriter();
        using (var writer = XmlWriter.Create(sw, settings))
        {
            doc.Save(writer);
        }

        WriteIfChanged(mainSolutionPath, sw.ToString() + "\n");
    }

    bool IsExcludedProject(string projectPath)
    {
        var value = GetProjectProperty(projectPath, "ExcludeFromGeneratedSlnx");
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    string? GetProjectProperty(string projectPath, string propertyName)
    {
        try
        {
            var doc = XDocument.Load(projectPath);
            return doc.Root?.Descendants(propertyName).FirstOrDefault()?.Value;
        }
        catch
        {
            return null;
        }
    }

    static string GetMainSlnxFolder(string relativePath)
    {
        if (relativePath.StartsWith("src/", StringComparison.Ordinal))
            return "/src/";

        if (relativePath.StartsWith("tests/", StringComparison.Ordinal))
        {
            var subPath = relativePath["tests/".Length..];
            var slashIndex = subPath.IndexOf('/', StringComparison.Ordinal);
            if (slashIndex > 0)
            {
                var folderName = subPath[..slashIndex];
                if (folderName.Contains("GeneratorTests", StringComparison.OrdinalIgnoreCase))
                    return "/tests/SourceGenerators/";
            }

            return "/tests/";
        }

        if (relativePath.StartsWith("tools/", StringComparison.Ordinal))
            return "/tools/";

        if (relativePath.StartsWith("samples/", StringComparison.Ordinal))
            return "/samples/";

        if (relativePath.StartsWith("benchmarks/", StringComparison.Ordinal))
            return "/benchmarks/";

        return "/other/";
    }
}

void RunValidateTestProjectsConfigurationStep(FullPath rootPath)
{
    var testsRootPath = rootPath / "tests";
    var tfmCache = new ConcurrentDictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    var testProjects = Directory.GetFiles(testsRootPath, "*.csproj", SearchOption.AllDirectories);

    var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 5 };
    Parallel.ForEach(testProjects, parallelOptions, proj =>
    {
        var testProjectTfms = GetProjectTargetFrameworksWithRetry(proj)
            .Select(SimplifyTfm)
            .ToList();

        var doc = XDocument.Load(proj);
        var projDir = FullPath.FromPath(proj).Parent;
        var references = doc.Descendants("ProjectReference")
            .Where(e => !string.Equals((string?)e.Attribute("OutputItemType"), "Analyzer", StringComparison.OrdinalIgnoreCase))
            .Where(e => !string.Equals((string?)e.Attribute("ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => v is not null)
            .Select(v => (string)(projDir / v!))
            .ToList();

        foreach (var refProj in references)
        {
            if (!Path.GetFileNameWithoutExtension(proj).StartsWith(Path.GetFileNameWithoutExtension(refProj), StringComparison.Ordinal))
                continue;

            var refTfms = GetProjectTargetFrameworksWithRetry(refProj);
            foreach (var refTfmRaw in refTfms)
            {
                var refTfm = SimplifyTfm(refTfmRaw);

                if (refTfm == "netstandard2.0")
                    continue;

                if (!testProjectTfms.Contains(refTfm, StringComparer.Ordinal))
                {
                    var errorMsg = $"Project {proj} does not target {refTfm}, but it references {refProj} which does. ({string.Join(", ", testProjectTfms)}) != ({string.Join(", ", refTfms)})";
                    Console.Error.WriteLine($"ERROR: {errorMsg}");
                    updatedFiles.Add(FullPath.FromPath(proj).MakePathRelativeTo(rootPath));
                }
            }
        }
    });

    string[] GetProjectTargetFrameworksWithRetry(string projectPath)
    {
        projectPath = Path.GetFullPath(projectPath);
        return tfmCache.GetOrAdd(projectPath, key => DotNetBuildGetTfmsWithRetry(key));
    }

    static string SimplifyTfm(string tfm)
    {
        var dashIndex = tfm.IndexOf('-', StringComparison.Ordinal);
        return dashIndex >= 0 ? tfm[..dashIndex] : tfm;
    }

    static string[] DotNetBuildGetTfmsWithRetry(string projectPath)
    {
        var delays = new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) };
        Exception? lastEx = null;
        for (var i = 0; i <= delays.Length; i++)
        {
            try
            {
                return DotNetBuildGetTfms(projectPath);
            }
            catch (Exception ex)
            {
                lastEx = ex;
                if (i == delays.Length)
                    break;

                Console.Error.WriteLine($"WARNING: Failed to evaluate target frameworks for '{projectPath}' (attempt {i + 1}/{delays.Length + 1}). Retrying in {delays[i].TotalSeconds:F0}s...");
                Console.Error.WriteLine(ex.Message);
                Thread.Sleep(delays[i]);
            }
        }

        throw new InvalidOperationException($"Unable to evaluate target frameworks for '{projectPath}' after retries.", lastEx);
    }

    static string[] DotNetBuildGetTfms(string projectPath)
    {
        using var process = Process.Start(new ProcessStartInfo("dotnet", $"msbuild \"{projectPath}\" -nologo -v:q -getProperty:TargetFramework -getProperty:TargetFrameworks")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Environment = { ["TERM"] = "dumb" },
        })!;

        process.WaitForExit();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet msbuild failed for '{projectPath}' with code {process.ExitCode}:{Environment.NewLine}{error}");
        }

        var targetFramework = Regex.Match(output, "\"TargetFramework\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, Timeout.InfiniteTimeSpan).Groups[1].Value;
        var targetFrameworks = Regex.Match(output, "\"TargetFrameworks\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, Timeout.InfiniteTimeSpan).Groups[1].Value;

        if (!string.IsNullOrEmpty(targetFramework))
        {
            return [targetFramework];
        }

        if (!string.IsNullOrEmpty(targetFrameworks))
        {
            return targetFrameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return [];
    }
}

async Task RunUpdateReadmeStep(FullPath rootPath)
{
    var srcRootPath = rootPath / "src";

    var nugetReadmeTask = Task.Run(UpdateNuGetReadme);
    var toolReadmeTask = Task.Run(UpdateToolReadmes);
    var analyzerDocumentationTask = Task.Run(() => UpdateAnalyzerDocumentation(srcRootPath));

    await Task.WhenAll(nugetReadmeTask, toolReadmeTask, analyzerDocumentationTask);

    void UpdateNuGetReadme()
    {
        var readmePath = rootPath / "README.md";
        Console.WriteLine("[update-nuget-readme] Starting NuGet README update");
        var nugetUpdateStopwatch = Stopwatch.StartNew();

        var csprojFiles = new List<string>(Directory.EnumerateFiles(srcRootPath, "*.csproj", SearchOption.AllDirectories));
        csprojFiles.Sort((a, b) => string.Compare(Path.GetFileNameWithoutExtension(a), Path.GetFileNameWithoutExtension(b), StringComparison.OrdinalIgnoreCase));
        Console.WriteLine($"[update-nuget-readme] NuGet discovery: found {csprojFiles.Count} project files");

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

        var originalLines = File.ReadAllLines(readmePath);
        var originalContent = string.Join("\n", originalLines);

        var newContentBuilder = new StringBuilder();
        var isInPackages = false;

        foreach (var line in originalLines)
        {
            if (line == "# NuGet packages")
            {
                newContentBuilder.Append(line).Append('\n').Append('\n').Append(sb).Append('\n');
                isInPackages = true;
            }
            else if (line.StartsWith('#', StringComparison.Ordinal))
            {
                isInPackages = false;
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
            updatedFiles.Add(FullPath.FromPath(readmePath).MakePathRelativeTo(rootPath));
        }
    }

    void UpdateToolReadmes()
    {
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
                .Any(node => string.Equals(
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
        var orderedToolProjects = toolProjects.OrderBy(project => project.Csproj, StringComparer.OrdinalIgnoreCase).ToArray();

        discoveryStopwatch.Stop();
        Console.WriteLine($"[update-tool-readme] Discovery metrics: scanned={scannedProjectCount}, executable={executableProjectCount}, system-commandline={commandLineProjectCount}, tool-projects={orderedToolProjects.Length}, elapsed={discoveryStopwatch.Elapsed.TotalSeconds:F2}s");

        if (!missingReadmeErrors.IsEmpty)
        {
            foreach (var error in missingReadmeErrors.OrderBy(error => error, StringComparer.Ordinal))
            {
                Console.Error.WriteLine(error);
            }

            throw new InvalidOperationException($"One or more tool projects are missing a readme.md file. See errors above.");
        }

        Console.WriteLine("[update-tool-readme] Starting parallel --help generation");
        var generationStopwatch = Stopwatch.StartNew();
        var helpSectionPattern = new Regex("(?<=<!-- help -->)(.*?)(?=<!-- help -->)", RegexOptions.Singleline | RegexOptions.ExplicitCapture, Timeout.InfiniteTimeSpan);
        var readmeUpdates = new ToolReadmeUpdate?[orderedToolProjects.Length];
        Parallel.For(0, orderedToolProjects.Length, parallelOptions, index =>
        {
            var project = orderedToolProjects[index];
            Console.WriteLine($"[update-tool-readme] [{index + 1}/{orderedToolProjects.Length}] Building tool project: {project.Csproj}");
            BuildToolProjectWithRetry(project.Csproj, latestTfm);

            Console.WriteLine($"[update-tool-readme] [{index + 1}/{orderedToolProjects.Length}] Generating help output for tool project: {project.Csproj}");

            var helpMarkdown = BuildToolHelpMarkdown(project.Csproj, latestTfm, project.ToolName);

            var toolReadmeContent = File.ReadAllText(project.ToolReadme);
            var newToolReadmeContent = helpSectionPattern.Replace(toolReadmeContent, $"\n{helpMarkdown}\n");
            newToolReadmeContent = newToolReadmeContent.TrimEnd(' ', '\t', '\r', '\n');
            readmeUpdates[index] = new ToolReadmeUpdate(project.ToolReadme, toolReadmeContent, newToolReadmeContent);
        });
        generationStopwatch.Stop();
        Console.WriteLine($"[update-tool-readme] --help generation metrics: tool-projects={orderedToolProjects.Length}, elapsed={generationStopwatch.Elapsed.TotalSeconds:F2}s");

        for (var i = 0; i < orderedToolProjects.Length; i++)
        {
            var update = readmeUpdates[i] ?? throw new InvalidOperationException($"Internal error: missing README update result for index {i}.");
            if (update.HasChanges)
            {
                File.WriteAllText(update.ToolReadme, update.NewContent);
                Console.WriteLine($"WARNING: {update.ToolReadme} was not up-to-date");
                updatedFiles.Add(FullPath.FromPath(update.ToolReadme).MakePathRelativeTo(rootPath));
            }
        }
    }
}

void UpdateAnalyzerDocumentation(FullPath srcRootPath)
{
    Console.WriteLine("[update-analyzer-docs] Starting analyzer documentation update");
    var analyzerDocumentationStopwatch = Stopwatch.StartNew();

    var candidateProjects = Directory.EnumerateFiles(srcRootPath, "*.csproj", SearchOption.AllDirectories)
        .Select(FullPath.FromPath)
        .Where(IsAnalyzerPackageProject)
        .OrderBy(path => path.Value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    Console.WriteLine($"[update-analyzer-docs] Discovery metrics: candidates={candidateProjects.Length}");

    var projectsWithRules = 0;
    foreach (var csproj in candidateProjects)
    {
        var projectName = csproj.NameWithoutExtension;
        var analyzerSourceDirectories = GetAnalyzerSourceDirectories(csproj);
        var rules = GetAnalyzerRulesFromProjectSource(analyzerSourceDirectories);
        if (rules.Count is 0)
        {
            Console.WriteLine($"[update-analyzer-docs] Project '{projectName}' does not expose DiagnosticAnalyzer rules, skipping");
            continue;
        }

        projectsWithRules++;
        var projectDirectory = csproj.Parent;
        var readmePath = projectDirectory / "readme.md";
        if (!File.Exists(readmePath))
        {
            throw new InvalidOperationException($"Project '{csproj}' exposes analyzer rules but has no readme.md file.");
        }

        var existingReadmeContent = File.ReadAllText(readmePath);
        var updatedReadmeContent = UpdateAnalyzerRulesSectionInReadme(existingReadmeContent, rules);
        if (WriteFileIfChanged(readmePath, updatedReadmeContent))
        {
            Console.WriteLine($"WARNING: {readmePath} was not up-to-date");
        }
    }

    analyzerDocumentationStopwatch.Stop();
    Console.WriteLine($"[update-analyzer-docs] Generation metrics: projects-with-rules={projectsWithRules}, elapsed={analyzerDocumentationStopwatch.Elapsed.TotalSeconds:F2}s");
}

static bool IsAnalyzerPackageProject(FullPath csprojPath)
{
    var doc = XDocument.Load(csprojPath);
    return doc.Descendants()
        .Any(node => node.Attribute("PackagePath")?.Value?.Contains("analyzers/dotnet/cs", StringComparison.OrdinalIgnoreCase) is true);
}

static IReadOnlyList<FullPath> GetAnalyzerSourceDirectories(FullPath packageProjectPath)
{
    var packageProjectDirectory = packageProjectPath.Parent;
    var sourceDirectories = new HashSet<FullPath> { packageProjectDirectory };

    var doc = XDocument.Load(packageProjectPath);
    var packagedAnalyzerAssemblyNames = doc.Descendants()
        .Where(node => node.Attribute("PackagePath")?.Value?.Contains("analyzers/dotnet/cs", StringComparison.OrdinalIgnoreCase) is true)
        .Select(node => node.Attribute("Include")?.Value)
        .Where(include => !string.IsNullOrWhiteSpace(include))
        .Select(include => Path.GetFileNameWithoutExtension(include))
        .Where(assemblyName => !string.IsNullOrWhiteSpace(assemblyName))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    foreach (var projectReference in doc.Descendants("ProjectReference"))
    {
        var include = projectReference.Attribute("Include")?.Value;
        if (string.IsNullOrWhiteSpace(include))
            continue;

        var normalizedInclude = include.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var referencedProjectPath = FullPath.Combine(packageProjectDirectory, normalizedInclude);
        if (!File.Exists(referencedProjectPath))
            continue;

        if (packagedAnalyzerAssemblyNames.Contains(referencedProjectPath.NameWithoutExtension))
        {
            sourceDirectories.Add(referencedProjectPath.Parent);
        }
    }

    return sourceDirectories
        .OrderBy(path => path.Value, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static IReadOnlyList<AnalyzerRule> GetAnalyzerRulesFromProjectSource(IReadOnlyList<FullPath> projectDirectories)
{
    var rules = new Dictionary<string, AnalyzerRule>(StringComparer.Ordinal);
    foreach (var projectDirectory in projectDirectories)
    {
        foreach (var rule in GetAnalyzerRulesFromProjectDirectory(projectDirectory))
        {
            rules.TryAdd(rule.Id, rule);
        }
    }

    return rules.Values
        .OrderBy(rule => rule.Id, StringComparer.Ordinal)
        .ToArray();
}

static IReadOnlyList<AnalyzerRule> GetAnalyzerRulesFromProjectDirectory(FullPath projectDirectory)
{
    var analyzerFiles = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
        .Where(file =>
        {
            var content = File.ReadAllText(file);
            return content.Contains("[DiagnosticAnalyzer", StringComparison.Ordinal) && content.Contains("DiagnosticDescriptor", StringComparison.Ordinal);
        })
        .ToArray();

    if (analyzerFiles.Length is 0)
    {
        return [];
    }

    var stringConstantsByName = GetStringConstantsByName(projectDirectory);
    var descriptorPattern = new Regex(
        """
        DiagnosticDescriptor\s+\w+\s*=\s*new\(
            \s*id:\s*(?<id>"[^"]+"|[\w\.]+)\s*,
            \s*title:\s*"(?<title>[^"]+)"\s*,
            \s*messageFormat:\s*"(?<message>[^"]+)"\s*,
            \s*category:\s*"(?<category>[^"]+)"\s*,
            \s*(?:defaultSeverity:\s*)?DiagnosticSeverity\.(?<severity>\w+)\s*,
            \s*isEnabledByDefault:\s*(?<enabled>true|false)\s*
        \)
        """,
        RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        Timeout.InfiniteTimeSpan);

    var rules = new Dictionary<string, AnalyzerRule>(StringComparer.Ordinal);
    foreach (var analyzerFile in analyzerFiles)
    {
        var content = File.ReadAllText(analyzerFile);
        foreach (Match match in descriptorPattern.Matches(content))
        {
            var idExpression = match.Groups["id"].Value;
            if (!TryResolveDiagnosticId(idExpression, stringConstantsByName, out var id))
            {
                throw new InvalidOperationException($"Cannot resolve analyzer diagnostic id '{idExpression}' from '{analyzerFile}'.");
            }

            var title = match.Groups["title"].Value;
            var category = match.Groups["category"].Value;
            var severity = match.Groups["severity"].Value;
            var isEnabledByDefault = bool.Parse(match.Groups["enabled"].Value);
            rules.TryAdd(id, new AnalyzerRule(id, category, title, severity, isEnabledByDefault));
        }
    }

    return rules.Values
        .OrderBy(rule => rule.Id, StringComparer.Ordinal)
        .ToArray();
}

static Dictionary<string, string> GetStringConstantsByName(FullPath projectDirectory)
{
    var constants = new Dictionary<string, string>(StringComparer.Ordinal);
    var constantPattern = new Regex(
        """
        \bconst\s+string\s+(?<name>\w+)\s*=\s*"(?<value>[^"]+)"\s*;
        """,
        RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        Timeout.InfiniteTimeSpan);

    foreach (var sourceFile in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
    {
        var content = File.ReadAllText(sourceFile);
        foreach (Match match in constantPattern.Matches(content))
        {
            var constantName = match.Groups["name"].Value;
            var constantValue = match.Groups["value"].Value;
            constants.TryAdd(constantName, constantValue);
        }
    }

    return constants;
}

static bool TryResolveDiagnosticId(string idExpression, IReadOnlyDictionary<string, string> stringConstantsByName, [NotNullWhen(true)] out string? diagnosticId)
{
    if (idExpression.StartsWith('"', StringComparison.Ordinal) && idExpression.EndsWith('"', StringComparison.Ordinal))
    {
        diagnosticId = idExpression.Trim('"');
        return true;
    }

    if (stringConstantsByName.TryGetValue(idExpression, out diagnosticId))
    {
        return true;
    }

    var separatorIndex = idExpression.LastIndexOf('.', StringComparison.Ordinal);
    if (separatorIndex >= 0 && separatorIndex < idExpression.Length - 1)
    {
        var constantName = idExpression[(separatorIndex + 1)..];
        if (stringConstantsByName.TryGetValue(constantName, out diagnosticId))
        {
            return true;
        }
    }

    diagnosticId = string.Empty;
    return false;
}

static string UpdateAnalyzerRulesSectionInReadme(string readmeContent, IReadOnlyList<AnalyzerRule> rules)
{
    var rulesTable = GenerateAnalyzerRulesTable(rules);
    var markerContentPattern = new Regex(
        $"(?<={Regex.Escape(AnalyzerRulesSectionMarker)})(.*?)(?={Regex.Escape(AnalyzerRulesSectionMarker)})",
        RegexOptions.Singleline | RegexOptions.ExplicitCapture,
        Timeout.InfiniteTimeSpan);

    if (markerContentPattern.IsMatch(readmeContent))
    {
        return markerContentPattern.Replace(readmeContent, $"\n{rulesTable}\n");
    }

    if (readmeContent.Contains("## Analyzer rules", StringComparison.OrdinalIgnoreCase) ||
        readmeContent.Contains("## Analyzer diagnostics", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("The README contains an analyzer section but does not use the analyzer-rules markers.");
    }

    var sb = new StringBuilder();
    sb.AppendLine(readmeContent.TrimEnd('\r', '\n'));
    sb.AppendLine();
    sb.AppendLine("## Analyzer rules");
    sb.AppendLine();
    sb.AppendLine(AnalyzerRulesSectionMarker);
    sb.AppendLine(rulesTable);
    sb.AppendLine(AnalyzerRulesSectionMarker);
    return sb.ToString().TrimEnd('\r', '\n');
}

static string GenerateAnalyzerRulesTable(IReadOnlyList<AnalyzerRule> rules)
{
    var sb = new StringBuilder();
    sb.AppendLine("| Id | Category | Description | Severity | Enabled |");
    sb.AppendLine("| -- | -- | -- | :--: | :--: |");
    foreach (var rule in rules)
    {
        sb.Append("| `")
            .Append(rule.Id)
            .Append("` | ")
            .Append(EscapeMarkdownTableCell(rule.Category))
            .Append(" | ")
            .Append(EscapeMarkdownTableCell(rule.Title))
            .Append(" | ")
            .Append(rule.DefaultSeverity)
            .Append(" | ")
            .Append(rule.IsEnabledByDefault ? "✔️" : "❌")
            .AppendLine(" |");
    }

    return sb.ToString().TrimEnd('\r', '\n');
}

bool WriteFileIfChanged(FullPath filePath, string content)
{
    var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    var normalizedContent = content.ReplaceLineEndings("\n").TrimEnd('\r', '\n') + "\n";

    if (File.Exists(filePath))
    {
        var existingContent = File.ReadAllText(filePath).ReplaceLineEndings("\n");
        if (string.Equals(existingContent, normalizedContent, StringComparison.Ordinal))
        {
            return false;
        }
    }
    else
    {
        filePath.CreateParentDirectory();
    }

    File.WriteAllText(filePath, normalizedContent, encoding);
    updatedFiles.Add(filePath.MakePathRelativeTo(rootPath));
    return true;
}

static string EscapeMarkdownTableCell(string value)
{
    return value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("\r\n", "<br />", StringComparison.Ordinal)
        .Replace("\n", "<br />", StringComparison.Ordinal)
        .Replace("\r", "<br />", StringComparison.Ordinal);
}

static int GetUpdateReadmeMaxDegreeOfParallelism()
{
    var value = Environment.GetEnvironmentVariable("UPDATE_README_MAX_PARALLELISM");
    if (string.IsNullOrWhiteSpace(value))
    {
        return Math.Clamp(Environment.ProcessorCount, 1, 4);
    }

    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue) && parsedValue > 0)
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

static void BuildToolProjectWithRetry(string csproj, string latestTfm)
{
    string[] buildArgs = ["build", csproj, "--framework", latestTfm, "-p:RunAnalyzers=false", "-p:RunAnalyzersDuringBuild=false"];
    TimeSpan[] retryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];
    _ = RunProcessAndCaptureOutput("dotnet", buildArgs, timeout: TimeSpan.FromMinutes(2), retryDelays: retryDelays);
}

static string RunProcessAndCaptureOutput(string fileName, string[] arguments, TimeSpan? timeout = null, IReadOnlyList<TimeSpan>? retryDelays = null)
{
    return RunProcessAndCaptureOutputs(fileName, arguments, timeout, retryDelays).StandardOutput;
}

static (string StandardOutput, string StandardError) RunProcessAndCaptureOutputs(string fileName, string[] arguments, TimeSpan? timeout = null, IReadOnlyList<TimeSpan>? retryDelays = null)
{
    retryDelays ??= [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)];
    for (var attempt = 0; ; attempt++)
    {
        try
        {
            return RunProcessAndCaptureOutputsCore(fileName, arguments, timeout);
        }
        catch (Exception ex) when ((ex is TimeoutException or InvalidOperationException) && attempt < retryDelays.Count)
        {
            var retryDelay = retryDelays[attempt];
            Console.Error.WriteLine($"WARNING: Process '{fileName} {string.Join(' ', arguments)}' failed on attempt {attempt + 1}/{retryDelays.Count + 1}. Retrying in {retryDelay.TotalSeconds:F0}s...");
            Console.Error.WriteLine(ex.Message);
            Thread.Sleep(retryDelay);
        }
    }
}

static (string StandardOutput, string StandardError) RunProcessAndCaptureOutputsCore(string fileName, string[] arguments, TimeSpan? timeout = null)
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
        throw new InvalidOperationException(BuildProcessFailureMessage(fileName, arguments, process.ExitCode, output, error));
    }

    return (output, error);
}

static string BuildProcessFailureMessage(string fileName, string[] arguments, int exitCode, string standardOutput, string standardError)
{
    var sb = new StringBuilder();
    sb.Append("Process '");
    sb.Append(fileName);
    sb.Append(' ');
    sb.Append(string.Join(' ', arguments));
    sb.Append("' exited with code ");
    sb.Append(exitCode);
    sb.Append('.');

    if (!string.IsNullOrWhiteSpace(standardOutput))
    {
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Standard output:");
        sb.AppendLine(standardOutput.TrimEnd());
    }

    if (!string.IsNullOrWhiteSpace(standardError))
    {
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Standard error:");
        sb.AppendLine(standardError.TrimEnd());
    }

    return sb.ToString();
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

static string TrimEndOfLines(string text)
{
    return Regex.Replace(text, "[ \\t]+(?=\\r?\\n|$)", string.Empty, RegexOptions.CultureInvariant, Timeout.InfiniteTimeSpan);
}

static string FormatCommandPath(string[] commandPath)
{
    return commandPath.Length is 0 ? "(root)" : string.Join(' ', commandPath);
}

static string GetLatestTargetFramework(FullPath rootPath)
{
    var directoryBuildProps = XDocument.Load(rootPath / "Directory.Build.props");
    return directoryBuildProps.Root?.Descendants("LatestTargetFramework").FirstOrDefault()?.Value ?? throw new InvalidOperationException("Cannot find LatestTargetFramework");
}

static int RunProcessAndReturnExitCode(FullPath workingDirectory, string fileName, string[] arguments)
{
    var psi = new ProcessStartInfo(fileName)
    {
        UseShellExecute = false,
        WorkingDirectory = workingDirectory,
        Environment = { ["TERM"] = "dumb" },
    };

    foreach (var argument in arguments)
    {
        psi.ArgumentList.Add(argument);
    }

    using var process = Process.Start(psi)!;
    process.WaitForExit();
    return process.ExitCode;
}

static FullPath GetRepositoryRoot()
{
    return FullPath.CurrentDirectory().FindRequiredGitRepositoryRoot();
}

internal readonly record struct AnalyzerRule(
    string Id,
    string Category,
    string Title,
    string DefaultSeverity,
    bool IsEnabledByDefault);

internal readonly record struct ToolProject(string Csproj, string? ToolName, string ToolReadme);

internal readonly record struct ToolReadmeUpdate(string ToolReadme, string OriginalContent, string NewContent)
{
    public bool HasChanges => !string.Equals(OriginalContent, NewContent, StringComparison.Ordinal);
}
