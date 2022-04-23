using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Meziantou.Framework.DependencyScanning.Internals;

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
        return context.FileName.Equals("packages.config", StringComparison.OrdinalIgnoreCase);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        var doc = await XmlUtilities.LoadDocumentWithoutClosingStreamAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        if (doc == null)
            return;

        IReadOnlyList<(string Path, XDocument Document)>? csprojs = null;
        foreach (var package in doc.Descendants(PackageXName))
        {
            var packageName = package.Attribute(IdXName)?.Value;
            var version = package.Attribute(VersionXName)?.Value;

            if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(version))
                continue;

            var dependency = new Dependency(packageName, version, DependencyType.NuGet, new XmlLocation(context.FullPath, package, "version"));
            await context.ReportDependency(dependency).ConfigureAwait(false);

            if (SearchForReferencesInAssociatedCsprojFiles)
            {
                if (csprojs == null)
                {
                    csprojs = await LoadAssociatedCsProjAsync(context).ConfigureAwait(false);
                }

                foreach (var (file, csproj) in csprojs)
                {
                    await FindInReferences(context, dependency, file, csproj).ConfigureAwait(false);
                    await FindInImports(context, dependency, file, csproj).ConfigureAwait(false);
                    await FindInErrors(context, dependency, file, csproj).ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task<IReadOnlyList<(string Path, XDocument Document)>> LoadAssociatedCsProjAsync(ScanFileContext context)
    {
        var directory = Path.GetDirectoryName(context.FullPath);
        if (directory == null)
            return Array.Empty<(string, XDocument)>();

        var files = context.FileSystem.GetFiles(directory, "*.csproj");
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

    private static async ValueTask FindInReferences(ScanFileContext context, Dependency dependency, string csprojPath, XDocument csproj)
    {
        var hints = csproj.Descendants()
            .Where(element => element.Name.LocalName == "Reference")
            .Elements()
            .Where(element => element.Name.LocalName == "HintPath");

        foreach (var hint in hints)
        {
            if (await FindDependencyInElementValue(context, dependency, csprojPath, hint).ConfigureAwait(false))
            {
                await FindDependencyInAssemblyName(context, dependency, csprojPath, hint.Parent?.Attribute(IncludeXName)).ConfigureAwait(false);
            }
        }
    }

    private static async ValueTask FindInImports(ScanFileContext context, Dependency dependency, string file, XDocument doc)
    {
        var imports = doc.Descendants().Where(element => element.Name.LocalName == "Import");
        foreach (var import in imports)
        {
            await FindDependencyInAttributeValue(context, dependency, file, import.Attribute(ProjectXName)).ConfigureAwait(false);
            await FindDependencyInAttributeValue(context, dependency, file, import.Attribute(ConditionXName)).ConfigureAwait(false);
        }
    }

    private static async ValueTask FindInErrors(ScanFileContext context, Dependency dependency, string file, XDocument doc)
    {
        var errors = doc.Descendants()
            .Where(element => element.Name.LocalName == "Target")
            .Elements()
            .Where(element => element.Name.LocalName == "Error");

        foreach (var error in errors)
        {
            await FindDependencyInAttributeValue(context, dependency, file, error.Attribute(TextXName)).ConfigureAwait(false);
            await FindDependencyInAttributeValue(context, dependency, file, error.Attribute(ConditionXName)).ConfigureAwait(false);
        }
    }

    private static async ValueTask<bool> FindDependencyInElementValue(ScanFileContext context, Dependency dependency, string file, XElement element)
    {
        if (element == null)
            return false;

        var value = element.Value;
        var indexOf = value.IndexOf(dependency.Name + '.' + dependency.Version, StringComparison.OrdinalIgnoreCase);
        if (indexOf < 0)
            return false;

        var versionStartColumn = indexOf + (dependency.Name + '.').Length;
        var location = new XmlLocation(file, element, column: versionStartColumn, length: dependency.Version.Length);
        await context.ReportDependency(new Dependency(dependency.Name, dependency.Version, dependency.Type, location)).ConfigureAwait(false);
        return true;
    }

    private static async ValueTask FindDependencyInAttributeValue(ScanFileContext context, Dependency dependency, string file, XAttribute? attribute)
    {
        if (attribute == null)
            return;

        var value = attribute.Value;
        var indexOf = value.IndexOf(dependency.Name + '.' + dependency.Version, StringComparison.OrdinalIgnoreCase);
        if (indexOf < 0)
            return;

        var versionStartColumn = indexOf + (dependency.Name + '.').Length;
        Debug.Assert(attribute.Parent != null);
        var location = new XmlLocation(file, attribute.Parent, attribute.Name.LocalName, column: versionStartColumn, length: dependency.Version.Length);
        await context.ReportDependency(new Dependency(dependency.Name, dependency.Version, dependency.Type, location)).ConfigureAwait(false);
    }

    private static async ValueTask FindDependencyInAssemblyName(ScanFileContext context, Dependency dependency, string file, XAttribute? attribute)
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
                var location = new AssemblyVersionXmlLocation(file, attribute.Parent, attribute.Name.LocalName, column: match.Index, length: match.Value.Length);
                await context.ReportDependency(new Dependency(dependency.Name, dependency.Version, dependency.Type, location)).ConfigureAwait(false);
            }
        }
    }
}
