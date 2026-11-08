using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Internals;

/// <summary>Reads the <c>name@specifier</c> values of the JavaScript package managers, such as <c>@scope/name@^1.0.0</c>.</summary>
internal static class NpmPackageSpecifier
{
    /// <summary>Splits the value on the first <c>@</c> that does not start a scope.</summary>
    public static bool TrySplit(string value, [NotNullWhen(true)] out string? name, [NotNullWhen(true)] out string? specifier)
    {
        var index = value.Length > 1 ? value.IndexOf('@', 1, StringComparison.Ordinal) : -1;
        if (index <= 0)
        {
            name = null;
            specifier = null;
            return false;
        }

        name = value[..index];
        specifier = value[(index + 1)..];
        return true;
    }

    /// <summary>Gets the name of the install folder from the end of an install path, such as <c>@scope/b</c> in <c>a/@scope/b</c>.</summary>
    public static string GetFolderName(string path)
    {
        var index = path.LastIndexOf('/', StringComparison.Ordinal);
        if (index > 0)
        {
            var scopeIndex = path.LastIndexOf('/', index - 1, StringComparison.Ordinal);
            if (path[scopeIndex + 1] is '@')
                return path[(scopeIndex + 1)..];
        }

        return path[(index + 1)..];
    }

    /// <summary>Reports a package of a lock file, whose name and version are not updatable.</summary>
    public static void ReportLockedPackage(DependencyScanner scanner, ScanFileContext context, string name, string version, string? alias)
    {
        context.ReportDependency(scanner, name, version, DependencyType.Npm,
            nameLocation: new NonUpdatableLocation(context),
            versionLocation: new NonUpdatableLocation(context),
            tags: [],
            metadata: alias is not null && !string.Equals(alias, name, StringComparison.Ordinal) ? [KeyValuePair.Create<string, object?>("alias", alias)] : []);
    }
}
