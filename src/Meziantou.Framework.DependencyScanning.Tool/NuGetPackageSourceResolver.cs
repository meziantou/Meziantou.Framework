using NuGet.Configuration;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal static class NuGetPackageSourceResolver
{
    // The search pattern is matched case-sensitively on Unix, so 'NuGet.Config' would not match '*.config'.
    private static readonly EnumerationOptions ConfigFileEnumerationOptions = new() { MatchCasing = MatchCasing.CaseInsensitive };

    public static NuGetSourceResolution Resolve(FullPath dependencyFile, string packageName)
    {
        // Only the configuration files in the repository are considered. NuGet would also load the
        // user-wide and machine-wide files, but the tool must resolve the same sources whichever
        // machine it runs on.
        var configFiles = EnumerateConfigFiles(dependencyFile.Parent);
        if (configFiles.Count is 0)
            return new NuGetSourceResolution([], [], HasSourceMappings: false);

        var settings = Settings.LoadSettingsGivenConfigPaths(configFiles);
        var sourceProvider = new PackageSourceProvider(settings);
        var allSources = sourceProvider.LoadPackageSources().Where(source => source.IsEnabled).ToArray();

        // PackageSourceMapping is NuGet's own pattern matching, so the tool queries exactly the sources
        // a restore would. Matching a package against every source whose patterns happen to match would
        // send an internally-mapped package name to a wildcard-mapped public feed, which is exactly what
        // source mapping exists to prevent.
        var mapping = PackageSourceMapping.GetPackageSourceMapping(settings);
        if (!mapping.IsEnabled)
            return new NuGetSourceResolution(allSources, allSources, HasSourceMappings: false);

        var mappedNames = mapping.GetConfiguredPackageSources(packageName);
        var mappedSources = allSources.Where(source => mappedNames.Contains(source.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
        return new NuGetSourceResolution(mappedSources, allSources, HasSourceMappings: true);
    }

    /// <summary>
    /// Walks up from <paramref name="directory"/> and returns every NuGet configuration file, nearest first.
    /// That is the order <see cref="Settings.LoadSettingsGivenConfigPaths"/> expects: the closest file wins.
    /// </summary>
    private static List<string> EnumerateConfigFiles(FullPath directory)
    {
        var configFiles = new List<string>();
        var current = directory;
        while (!current.IsEmpty)
        {
            if (Directory.Exists(current))
            {
                // The file name is matched case-insensitively on every platform, the way NuGet does it,
                // so 'nuget.config' and 'NuGet.Config' both work on case-sensitive file systems. Sorted
                // because the enumeration order is undefined when a directory holds several casings.
                var matches = Directory.EnumerateFiles(current, "*.config", ConfigFileEnumerationOptions)
                    .Where(file => Path.GetFileName(file.AsSpan()).Equals(Settings.DefaultSettingsFileName, StringComparison.OrdinalIgnoreCase))
                    .Order(StringComparer.Ordinal);

                configFiles.AddRange(matches);
            }

            current = current.Parent;
        }

        return configFiles;
    }
}
