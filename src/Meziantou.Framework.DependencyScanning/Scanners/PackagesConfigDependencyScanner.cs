using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

public sealed class PackagesConfigDependencyScanner : DependencyScanner
{
    private static readonly Version VersionZero = new(0, 0, 0, 0);
    private static readonly Version VersionOne = new(1, 0, 0, 0);

    private static readonly XName PackageXName = XName.Get("package");
    private static readonly XName IdXName = XName.Get("id");
    private static readonly XName VersionXName = XName.Get("version");
    private static readonly XName IncludeXName = XName.Get("Include");
    private static readonly XName ProjectXName = XName.Get("Project");
    private static readonly XName ConditionXName = XName.Get("Condition");
    private static readonly XName TextXName = XName.Get("Text");

    public bool SearchForReferencesInAssociatedCsprojFiles { get; set; } = true;

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("packages.config", ignoreCase: true);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        var doc = await XmlUtilities.LoadDocumentWithoutClosingStreamAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        if (doc == null)
            return;

        IReadOnlyList<(string Path, XDocument Document)>? csprojs = null;
        foreach (var package in doc.Descendants(PackageXName))
        {
            var packageNameAttribute = package.Attribute(IdXName);
            var packageName = packageNameAttribute?.Value;
            var versionAttribute = package.Attribute(VersionXName);
            var version = versionAttribute?.Value;

            if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(version))
                continue;

            var dependency = new Dependency(packageName, version, DependencyType.NuGet,
                nameLocation: new XmlLocation(context.FileSystem, context.FullPath, package, packageNameAttribute),
                versionLocation: new XmlLocation(context.FileSystem, context.FullPath, package, versionAttribute));
            context.ReportDependency(dependency);

            if (SearchForReferencesInAssociatedCsprojFiles)
            {
                csprojs ??= await LoadAssociatedCsProjAsync(context).ConfigureAwait(false);
                foreach (var (file, csproj) in csprojs)
                {
                    FindInReferences(context, dependency, file, csproj);
                    FindInImports(context, dependency, file, csproj);
                    FindInErrors(context, dependency, file, csproj);
                }
            }
        }
    }

    private static async Task<IReadOnlyList<(string Path, XDocument Document)>> LoadAssociatedCsProjAsync(ScanFileContext context)
    {
        var directory = Path.GetDirectoryName(context.FullPath);
        if (directory == null)
            return Array.Empty<(string, XDocument)>();

        var files = context.FileSystem.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly);
        var result = new List<(string, XDocument)>();
        foreach (var file in files)
        {
            var stream = context.FileSystem.OpenRead(file);
            try
            {
                var doc = await XmlUtilities.TryLoadDocumentWithoutClosingStream(stream, context.CancellationToken).ConfigureAwait(false);
                if (doc == null)
                    continue;

                result.Add((file, doc));
            }
            finally
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }

        return result;
    }

    private static void FindInReferences(ScanFileContext context, Dependency dependency, string csprojPath, XDocument csproj)
    {
        var hints = csproj.Descendants()
            .Where(element => element.Name.LocalName == "Reference")
            .Elements()
            .Where(element => element.Name.LocalName == "HintPath");

        foreach (var hint in hints)
        {
            if (FindDependencyInElementValue(context, dependency, csprojPath, hint))
            {
                FindDependencyInAssemblyName(context, dependency, csprojPath, hint.Parent?.Attribute(IncludeXName));
            }
        }
    }

    private static void FindInImports(ScanFileContext context, Dependency dependency, string file, XDocument doc)
    {
        var imports = doc.Descendants().Where(element => element.Name.LocalName == "Import");
        foreach (var import in imports)
        {
            FindDependencyInAttributeValue(context, dependency, file, import.Attribute(ProjectXName));
            FindDependencyInAttributeValue(context, dependency, file, import.Attribute(ConditionXName));
        }
    }

    private static void FindInErrors(ScanFileContext context, Dependency dependency, string file, XDocument doc)
    {
        var errors = doc.Descendants()
            .Where(element => element.Name.LocalName == "Target")
            .Elements()
            .Where(element => element.Name.LocalName == "Error");

        foreach (var error in errors)
        {
            FindDependencyInAttributeValue(context, dependency, file, error.Attribute(TextXName));
            FindDependencyInAttributeValue(context, dependency, file, error.Attribute(ConditionXName));
        }
    }

    private static bool FindDependencyInElementValue(ScanFileContext context, Dependency dependency, string file, XElement element)
    {
        if (element == null)
            return false;

        var value = element.Value;
        var indexOf = value.IndexOf(dependency.Name + '.' + dependency.Version, StringComparison.OrdinalIgnoreCase);
        if (indexOf < 0)
            return false;

        var versionStartColumn = indexOf + (dependency.Name + '.').Length;
        var versionLocation = new XmlLocation(context.FileSystem, file, element, column: versionStartColumn, length: dependency.Version!.Length);
        context.ReportDependency(new Dependency(dependency.Name, dependency.Version, dependency.Type, nameLocation: new NonUpdatableLocation(context), versionLocation));
        return true;
    }

    private static void FindDependencyInAttributeValue(ScanFileContext context, Dependency dependency, string file, XAttribute? attribute)
    {
        if (attribute == null)
            return;

        var value = attribute.Value;
        var indexOf = value.IndexOf(dependency.Name + '.' + dependency.Version, StringComparison.OrdinalIgnoreCase);
        if (indexOf < 0)
            return;

        var versionStartColumn = indexOf + (dependency.Name + '.').Length;
        Debug.Assert(attribute.Parent != null);
        var versionLocation = new XmlLocation(context.FileSystem, file, attribute.Parent, attribute, column: versionStartColumn, length: dependency.Version!.Length);
        context.ReportDependency(new Dependency(dependency.Name, dependency.Version, dependency.Type, nameLocation: new NonUpdatableLocation(context), versionLocation));
    }

    private static void FindDependencyInAssemblyName(ScanFileContext context, Dependency dependency, string file, XAttribute? attribute)
    {
        if (attribute == null)
            return;

        var value = attribute.Value;
        var match = Regex.Match(value, "(?<=Version=)(?<Version>[0-9.]+)", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
        if (match.Success)
        {
            var version = match.Groups["Version"].Value;
            if (Version.TryParse(version, out var v) && v != VersionZero && v != VersionOne)
            {
                Debug.Assert(attribute.Parent != null);
                var versionLocation = new AssemblyVersionXmlLocation(context.FileSystem, file, attribute.Parent, attribute, column: match.Index, length: match.Value.Length);
                context.ReportDependency(new Dependency(dependency.Name, dependency.Version, dependency.Type, nameLocation: new NonUpdatableLocation(context), versionLocation));
            }
        }
    }
}
