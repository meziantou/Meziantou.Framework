using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans legacy packages.config and packages.&lt;project&gt;.config files for NuGet package dependencies.</summary>
public sealed partial class PackagesConfigDependencyScanner : DependencyScanner
{
    private const string AssemblyVersionMetadataName = "assemblyVersion";
    private const string AssemblyVersionLocationMetadataName = "assemblyVersionLocation";

    private static readonly XName PackageXName = XName.Get("package");
    private static readonly XName IdXName = XName.Get("id");
    private static readonly XName VersionXName = XName.Get("version");
    private static readonly XName IncludeXName = XName.Get("Include");
    private static readonly XName ProjectXName = XName.Get("Project");
    private static readonly XName ConditionXName = XName.Get("Condition");
    private static readonly XName TextXName = XName.Get("Text");

    public bool SearchForReferencesInAssociatedCsprojFiles { get; set; } = true;

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.NuGet];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        // A project can use packages.<project name>.config instead of packages.config, for instance when several projects share a directory
        return context.HasFileName("packages.config", ignoreCase: true)
            || (context.FileName.StartsWith("packages.", StringComparison.OrdinalIgnoreCase) && context.FileName.EndsWith(".config", StringComparison.OrdinalIgnoreCase) && context.FileName.Length > "packages..config".Length);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        var doc = await XmlUtilities.TryLoadDocumentWithoutClosingStream(context.Content, context.CancellationToken).ConfigureAwait(false);
        if (doc is null)
            return;

        IReadOnlyList<AssociatedProject>? csprojs = null;
        foreach (var package in doc.Descendants(PackageXName))
        {
            var packageNameAttribute = package.Attribute(IdXName);
            var packageName = packageNameAttribute?.Value;
            var versionAttribute = package.Attribute(VersionXName);
            var version = versionAttribute?.Value;

            if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(version))
                continue;

            context.ReportDependency(this, packageName, version, DependencyType.NuGet,
                nameLocation: new XmlLocation(context.FileSystem, context.FullPath, package, packageNameAttribute),
                versionLocation: new XmlLocation(context.FileSystem, context.FullPath, package, versionAttribute));

            if (SearchForReferencesInAssociatedCsprojFiles)
            {
                var rootDependency = new DependencyRoot(packageName, version, DependencyType.NuGet);
                csprojs ??= await LoadAssociatedCsprojAsync(context).ConfigureAwait(false);
                foreach (var csproj in csprojs)
                {
                    FindInReferences(context, rootDependency, csproj);
                    FindInImports(context, rootDependency, csproj);
                    FindInErrors(context, rootDependency, csproj);
                }
            }
        }
    }

    private static async Task<IReadOnlyList<AssociatedProject>> LoadAssociatedCsprojAsync(ScanFileContext context)
    {
        var directory = Path.GetDirectoryName(context.FullPath);
        if (directory is null)
            return [];

        var result = new List<AssociatedProject>();
        foreach (var file in GetAssociatedProjectFiles(context, directory))
        {
            Stream stream;
            try
            {
                stream = context.FileSystem.OpenRead(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable project file (e.g. a dangling symbolic link) must not prevent reporting the packages.config dependencies
                continue;
            }

            try
            {
                var doc = await XmlUtilities.TryLoadDocumentWithoutClosingStream(stream, context.CancellationToken).ConfigureAwait(false);
                if (doc is null)
                    continue;

                result.Add(new AssociatedProject(file, doc));
            }
            finally
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the project files that use the scanned file. Like NuGet, a project uses packages.&lt;project name&gt;.config when it exists
    /// (spaces in the project name may be replaced by underscores), and packages.config otherwise.
    /// </summary>
    private static IEnumerable<string> GetAssociatedProjectFiles(ScanFileContext context, string directory)
    {
        var projectFiles = context.FileSystem.GetFiles(directory, "*proj", SearchOption.TopDirectoryOnly)
            .Where(IsMsBuildProjectFile)
            .ToArray();
        if (projectFiles.Length == 0)
            return [];

        var configFileName = Path.GetFileName(context.FullPath);
        if (!configFileName.Equals("packages.config", StringComparison.OrdinalIgnoreCase))
        {
            var projectName = configFileName["packages.".Length..^".config".Length];
            return projectFiles.Where(file => IsPackagesConfigNameForProject(projectName, file));
        }

        var projectSpecificConfigNames = context.FileSystem.GetFiles(directory, "packages.*.config", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => name.Length > "packages..config".Length)
            .Select(name => name["packages.".Length..^".config".Length])
            .ToArray();
        return projectFiles.Where(file => !projectSpecificConfigNames.Any(projectName => IsPackagesConfigNameForProject(projectName, file)));

        static bool IsMsBuildProjectFile(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".pbxproj", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".vdproj", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsPackagesConfigNameForProject(string projectNameInConfigFileName, string projectPath)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            return projectName.Equals(projectNameInConfigFileName, StringComparison.OrdinalIgnoreCase)
                || projectName.Replace(' ', '_').Equals(projectNameInConfigFileName, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void FindInReferences(ScanFileContext context, DependencyRoot dependency, AssociatedProject project)
    {
        foreach (var hint in project.HintPaths)
        {
            var index = IndexOfPackageFolder(hint.Value, dependency);
            if (index < 0)
                continue;

            var versionStartColumn = index + dependency.Name.Length + 1;
            var versionLocation = new XmlLocation(context.FileSystem, project.Path, hint, column: versionStartColumn, length: dependency.Version.Length);

            // The assembly version of the reference is not the package version, so it is reported as metadata. It is not
            // updated with the package version, as the assembly version of the new version of the package is unknown.
            var assemblyVersion = FindAssemblyVersion(context, project.Path, hint.Parent?.Attribute(IncludeXName));
            if (assemblyVersion is var (assemblyVersionValue, assemblyVersionLocation))
            {
                context.ReportDependency(this, dependency.Name, dependency.Version, dependency.Type, nameLocation: new NonUpdatableLocation(context), versionLocation,
                    tags: [],
                    metadata: [new(AssemblyVersionMetadataName, assemblyVersionValue), new(AssemblyVersionLocationMetadataName, assemblyVersionLocation)]);
            }
            else
            {
                context.ReportDependency(this, dependency.Name, dependency.Version, dependency.Type, nameLocation: new NonUpdatableLocation(context), versionLocation);
            }
        }
    }

    private void FindInImports(ScanFileContext context, DependencyRoot dependency, AssociatedProject project)
    {
        foreach (var attribute in project.ImportAttributes)
        {
            FindDependencyInAttributeValue(context, dependency, project.Path, attribute);
        }
    }

    private void FindInErrors(ScanFileContext context, DependencyRoot dependency, AssociatedProject project)
    {
        foreach (var attribute in project.ErrorAttributes)
        {
            FindDependencyInAttributeValue(context, dependency, project.Path, attribute);
        }
    }

    /// <summary>
    /// The elements of a project file that may reference a package, collected once so that they are not
    /// re-queried for every package declared in the packages.config file.
    /// </summary>
    private sealed class AssociatedProject
    {
        public AssociatedProject(string path, XDocument document)
        {
            Path = path;

            // Item types and metadata names are case-insensitive, but MSBuild keywords such as Import, Target and Error are not
            HintPaths = [.. document.Descendants()
                .Where(element => element.Name.LocalName.Equals("Reference", StringComparison.OrdinalIgnoreCase))
                .Elements()
                .Where(element => element.Name.LocalName.Equals("HintPath", StringComparison.OrdinalIgnoreCase))];

            ImportAttributes = [.. document.Descendants()
                .Where(element => element.Name.LocalName == "Import")
                .SelectMany(element => new[] { element.Attribute(ProjectXName), element.Attribute(ConditionXName) })
                .OfType<XAttribute>()];

            ErrorAttributes = [.. document.Descendants()
                .Where(element => element.Name.LocalName == "Target")
                .Elements()
                .Where(element => element.Name.LocalName == "Error")
                .SelectMany(element => new[] { element.Attribute(TextXName), element.Attribute(ConditionXName) })
                .OfType<XAttribute>()];
        }

        public string Path { get; }
        public XElement[] HintPaths { get; }
        public XAttribute[] ImportAttributes { get; }
        public XAttribute[] ErrorAttributes { get; }
    }

    private void FindDependencyInAttributeValue(ScanFileContext context, DependencyRoot dependency, string file, XAttribute attribute)
    {
        var index = IndexOfPackageFolder(attribute.Value, dependency);
        if (index < 0)
            return;

        var versionStartColumn = index + dependency.Name.Length + 1;
        Debug.Assert(attribute.Parent is not null);
        var versionLocation = new XmlLocation(context.FileSystem, file, attribute.Parent, attribute, column: versionStartColumn, length: dependency.Version.Length);
        context.ReportDependency(this, dependency.Name, dependency.Version, dependency.Type, nameLocation: new NonUpdatableLocation(context), versionLocation);
    }

    /// <summary>
    /// Finds the package folder, "&lt;id&gt;.&lt;version&gt;", in a path. The folder name must be a whole path segment,
    /// so that package "Owin" 1.0 does not match the folder of package "Microsoft.Owin" 1.0.
    /// </summary>
    private static int IndexOfPackageFolder(string value, DependencyRoot dependency)
    {
        var folderName = dependency.Name + '.' + dependency.Version;
        var index = 0;
        while ((index = value.IndexOf(folderName, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var end = index + folderName.Length;
            if ((index == 0 || IsSegmentStart(value[index - 1])) && (end == value.Length || IsSegmentEnd(value[end])))
                return index;

            index++;
        }

        return -1;

        static bool IsSegmentStart(char c) => c is '\\' or '/' or '\'' or '"' or ';' or ')';
        static bool IsSegmentEnd(char c) => c is '\\' or '/' or '\'' or '"' or ';';
    }

    private static (string Version, Location Location)? FindAssemblyVersion(ScanFileContext context, string file, XAttribute? attribute)
    {
        if (attribute is null)
            return null;

        var match = VersionInAssemblyNameRegex().Match(attribute.Value);
        if (!match.Success || !Version.TryParse(match.Value, out _))
            return null;

        Debug.Assert(attribute.Parent is not null);
        return (match.Value, new AssemblyVersionXmlLocation(context.FileSystem, file, attribute.Parent, attribute, column: match.Index, length: match.Length));
    }

    private record struct DependencyRoot(string Name, string Version, DependencyType Type);

    [GeneratedRegex(@"(?<=,\s*Version\s*=\s*)[0-9]+(\.[0-9]+){1,3}(?![0-9.])", RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: -1)]
    private static partial Regex VersionInAssemblyNameRegex();
}
