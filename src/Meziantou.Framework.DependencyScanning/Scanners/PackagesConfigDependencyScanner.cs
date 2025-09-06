using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

public sealed partial class PackagesConfigDependencyScanner : DependencyScanner
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

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.NuGet];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("packages.config", ignoreCase: true);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        var doc = await XmlUtilities.LoadDocumentWithoutClosingStreamAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        if (doc is null)
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

            context.ReportDependency(this, packageName, version, DependencyType.NuGet,
                nameLocation: new XmlLocation(context.FileSystem, context.FullPath, package, packageNameAttribute),
                versionLocation: new XmlLocation(context.FileSystem, context.FullPath, package, versionAttribute));

            if (SearchForReferencesInAssociatedCsprojFiles)
            {
                var rootDependency = new DependencyRoot(packageName, version, DependencyType.NuGet);
                csprojs ??= await LoadAssociatedCsprojAsync(context).ConfigureAwait(false);
                foreach (var (file, csproj) in csprojs)
                {

                    FindInReferences(context, rootDependency, file, csproj);
                    FindInImports(context, rootDependency, file, csproj);
                    FindInErrors(context, rootDependency, file, csproj);
                }
            }
        }
    }

    private static async Task<IReadOnlyList<(string Path, XDocument Document)>> LoadAssociatedCsprojAsync(ScanFileContext context)
    {
        var directory = Path.GetDirectoryName(context.FullPath);
        if (directory is null)
            return Array.Empty<(string, XDocument)>();

        var files = context.FileSystem.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly);
        var result = new List<(string, XDocument)>();
        foreach (var file in files)
        {
            var stream = context.FileSystem.OpenRead(file);
            try
            {
                var doc = await XmlUtilities.TryLoadDocumentWithoutClosingStream(stream, context.CancellationToken).ConfigureAwait(false);
                if (doc is null)
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

    private void FindInReferences(ScanFileContext context, DependencyRoot dependency, string csprojPath, XDocument csproj)
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

    private void FindInImports(ScanFileContext context, DependencyRoot dependency, string file, XDocument doc)
    {
        var imports = doc.Descendants().Where(element => element.Name.LocalName == "Import");
        foreach (var import in imports)
        {
            FindDependencyInAttributeValue(context, dependency, file, import.Attribute(ProjectXName));
            FindDependencyInAttributeValue(context, dependency, file, import.Attribute(ConditionXName));
        }
    }

    private void FindInErrors(ScanFileContext context, DependencyRoot dependency, string file, XDocument doc)
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

    private bool FindDependencyInElementValue(ScanFileContext context, DependencyRoot dependency, string file, XElement element)
    {
        if (element is null)
            return false;

        var value = element.Value;
        var indexOf = value.IndexOf(dependency.Name + '.' + dependency.Version, StringComparison.OrdinalIgnoreCase);
        if (indexOf < 0)
            return false;

        var versionStartColumn = indexOf + (dependency.Name + '.').Length;
        var versionLocation = new XmlLocation(context.FileSystem, file, element, column: versionStartColumn, length: dependency.Version!.Length);
        context.ReportDependency(this, dependency.Name, dependency.Version, dependency.Type, nameLocation: new NonUpdatableLocation(context), versionLocation);
        return true;
    }

    private void FindDependencyInAttributeValue(ScanFileContext context, DependencyRoot dependency, string file, XAttribute? attribute)
    {
        if (attribute is null)
            return;

        var value = attribute.Value;
        var indexOf = value.IndexOf(dependency.Name + '.' + dependency.Version, StringComparison.OrdinalIgnoreCase);
        if (indexOf < 0)
            return;

        var versionStartColumn = indexOf + (dependency.Name + '.').Length;
        Debug.Assert(attribute.Parent is not null);
        var versionLocation = new XmlLocation(context.FileSystem, file, attribute.Parent, attribute, column: versionStartColumn, length: dependency.Version!.Length);
        context.ReportDependency(this, dependency.Name, dependency.Version, dependency.Type, nameLocation: new NonUpdatableLocation(context), versionLocation);
    }

    private void FindDependencyInAssemblyName(ScanFileContext context, DependencyRoot dependency, string file, XAttribute? attribute)
    {
        if (attribute is null)
            return;

        var value = attribute.Value;
        var match = VersionInAssemblyNameRegex().Match(value);
        if (match.Success)
        {
            var version = match.Groups["Version"].Value;
            if (Version.TryParse(version, out var v) && v != VersionZero && v != VersionOne)
            {
                Debug.Assert(attribute.Parent is not null);
                var versionLocation = new AssemblyVersionXmlLocation(context.FileSystem, file, attribute.Parent, attribute, column: match.Index, length: match.Value.Length);
                context.ReportDependency(this, dependency.Name, match.Value, dependency.Type, nameLocation: new NonUpdatableLocation(context), versionLocation);
            }
        }
    }

    private record struct DependencyRoot(string Name, string Version, DependencyType Type);

    [GeneratedRegex("(?<=Version=)(?<Version>[0-9.]+)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: -1)]
    private static partial Regex VersionInAssemblyNameRegex();
}
