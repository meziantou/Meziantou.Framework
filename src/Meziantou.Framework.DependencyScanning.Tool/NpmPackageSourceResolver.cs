using Meziantou.Framework;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal static class NpmPackageSourceResolver
{
    private static readonly StringComparer Comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Resolves the registry of a package, preferring the registry declared next to the dependency, such as by a plugin marketplace, over the .npmrc files.</summary>
    public static Uri ResolveRegistry(FullPath dependencyFile, string packageName, string? declaredRegistry)
    {
        if (!string.IsNullOrWhiteSpace(declaredRegistry) && Uri.TryCreate(declaredRegistry.EndsWith('/', StringComparison.Ordinal) ? declaredRegistry : declaredRegistry + "/", UriKind.Absolute, out var registry))
            return registry;

        return ResolveRegistry(dependencyFile, packageName);
    }

    public static Uri ResolveRegistry(FullPath dependencyFile, string packageName)
    {
        var scopedRegistries = new Dictionary<string, string>(Comparer);
        string? defaultRegistry = null;

        var npmrcFiles = EnumerateNpmrcFiles(dependencyFile.Parent).Reverse();
        foreach (var npmrcFile in npmrcFiles)
        {
            ParseNpmrcFile(npmrcFile, scopedRegistries, ref defaultRegistry);
        }

        var scope = TryGetScope(packageName);
        if (scope is not null && scopedRegistries.TryGetValue(scope, out var scopeRegistry))
            return NormalizeRegistry(scopeRegistry);

        if (!string.IsNullOrWhiteSpace(defaultRegistry))
            return NormalizeRegistry(defaultRegistry);

        return new Uri("https://registry.npmjs.org/");
    }

    private static IEnumerable<FullPath> EnumerateNpmrcFiles(FullPath startDirectory)
    {
        var current = startDirectory;
        while (!current.IsEmpty)
        {
            var npmrc = current / ".npmrc";
            if (File.Exists(npmrc))
            {
                yield return npmrc;
            }

            current = current.Parent;
        }
    }

    private static void ParseNpmrcFile(FullPath filePath, Dictionary<string, string> scopedRegistries, ref string? defaultRegistry)
    {
        foreach (var line in File.ReadLines(filePath))
        {
            var span = line.AsSpan().Trim();
            if (span.IsEmpty || span[0] is '#' or ';')
                continue;

            var index = span.IndexOf('=');
            if (index <= 0)
                continue;

            var key = span[..index].Trim().ToString();
            var value = span[(index + 1)..].Trim().ToString();
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if ((value.Length >= 2 && value[0] == '"' && value[^1] == '"') || (value.Length >= 2 && value[0] == '\'' && value[^1] == '\''))
            {
                value = value[1..^1];
            }

            if (key.Equals("registry", StringComparison.OrdinalIgnoreCase))
            {
                defaultRegistry = value;
                continue;
            }

            if (key.EndsWith(":registry", StringComparison.OrdinalIgnoreCase))
            {
                var scope = key[..^":registry".Length];
                if (!scope.StartsWith('@', StringComparison.Ordinal))
                {
                    scope = "@" + scope;
                }

                if (!string.IsNullOrWhiteSpace(scope))
                {
                    scopedRegistries[scope] = value;
                }
            }
        }
    }

    private static string? TryGetScope(string packageName)
    {
        if (!packageName.StartsWith('@', StringComparison.Ordinal))
            return null;

        var index = packageName.IndexOf('/', StringComparison.Ordinal);
        if (index <= 1)
            return null;

        return packageName[..index];
    }

    private static Uri NormalizeRegistry(string registry)
    {
        if (!registry.EndsWith('/', StringComparison.Ordinal))
        {
            registry += "/";
        }

        return new Uri(registry, UriKind.Absolute);
    }
}
