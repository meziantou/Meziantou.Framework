using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Meziantou.Framework;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal static class NuGetPackageSourceResolver
{
    private static readonly StringComparer Comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static NuGetSourceResolution Resolve(FullPath dependencyFile, string packageName)
    {
        var packageSources = new Dictionary<string, string>(Comparer);
        var packageMappings = new Dictionary<string, List<string>>(Comparer);

        var configFiles = EnumerateConfigFiles(dependencyFile.Parent).Reverse();
        foreach (var configFile in configFiles)
        {
            ParseConfig(configFile, packageSources, packageMappings);
        }

        var sources = ResolveSourcesForPackage(packageName, packageSources, packageMappings);
        return new NuGetSourceResolution([.. sources], [.. packageSources.Values], HasSourceMappings: packageMappings.Count > 0);
    }

    private static IEnumerable<FullPath> EnumerateConfigFiles(FullPath directory)
    {
        var current = directory;
        while (!current.IsEmpty)
        {
            var nugetConfig = current / "nuget.config";
            var nuGetConfig = current / "NuGet.config";
            var nuGetConfigUpper = current / "NuGet.Config";

            if (File.Exists(nugetConfig))
                yield return nugetConfig;

            if (File.Exists(nuGetConfig) && nuGetConfig != nugetConfig)
                yield return nuGetConfig;

            if (File.Exists(nuGetConfigUpper) && nuGetConfigUpper != nugetConfig && nuGetConfigUpper != nuGetConfig)
                yield return nuGetConfigUpper;

            current = current.Parent;
        }
    }

    private static void ParseConfig(FullPath configFile, Dictionary<string, string> packageSources, Dictionary<string, List<string>> packageMappings)
    {
        var document = XDocument.Load(configFile);
        var root = document.Root;
        if (root is null)
            return;

        var packageSourcesElement = root.Element("packageSources");
        if (packageSourcesElement is not null)
        {
            ApplyClearBehavior(packageSourcesElement, packageSources);
            foreach (var addElement in packageSourcesElement.Elements("add"))
            {
                var key = addElement.Attribute("key")?.Value;
                var value = addElement.Attribute("value")?.Value;
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                    continue;

                packageSources[key] = value;
            }
        }

        var mappingElement = root.Element("packageSourceMapping");
        if (mappingElement is null)
            return;

        ApplyClearBehavior(mappingElement, packageMappings);
        foreach (var sourceElement in mappingElement.Elements("packageSource"))
        {
            var sourceKey = sourceElement.Attribute("key")?.Value;
            if (string.IsNullOrWhiteSpace(sourceKey))
                continue;

            if (!packageMappings.TryGetValue(sourceKey, out var patterns))
            {
                patterns = [];
                packageMappings[sourceKey] = patterns;
            }

            foreach (var packageElement in sourceElement.Elements("package"))
            {
                var pattern = packageElement.Attribute("pattern")?.Value;
                if (!string.IsNullOrWhiteSpace(pattern))
                {
                    patterns.Add(pattern);
                }
            }
        }
    }

    private static List<string> ResolveSourcesForPackage(string packageName, Dictionary<string, string> packageSources, Dictionary<string, List<string>> packageMappings)
    {
        if (packageMappings.Count is 0)
            return [.. packageSources.Values];

        var matches = new List<string>();
        foreach (var (sourceName, patterns) in packageMappings)
        {
            if (!packageSources.TryGetValue(sourceName, out var sourceUrl))
                continue;

            if (IsMatch(packageName, patterns))
            {
                matches.Add(sourceUrl);
            }
        }

        if (matches.Count is 0)
            return [];

        return [.. matches.Distinct(Comparer)];
    }

    private static bool IsMatch(string packageName, IReadOnlyCollection<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (GlobMatch(packageName, pattern))
            {
                return true;
            }
        }

        return false;
    }

    private static bool GlobMatch(string text, string pattern)
    {
        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(text, regexPattern, OperatingSystem.IsWindows() ? RegexOptions.IgnoreCase : RegexOptions.None, TimeSpan.FromSeconds(1));
    }

    private static void ApplyClearBehavior<TValue>(XElement element, Dictionary<string, TValue> data)
    {
        if (element.Elements("clear").Any())
        {
            data.Clear();
        }
    }
}

internal sealed record NuGetSourceResolution(IReadOnlyList<string> PackageSources, IReadOnlyList<string> AllConfiguredSources, bool HasSourceMappings);
