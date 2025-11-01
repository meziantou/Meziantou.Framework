namespace Meziantou.Framework.DependencyScanning;

/// <summary>Provides extension methods for ordering dependencies by their location in source files.</summary>
public static class LocationExtensions
{
    /// <summary>Orders dependencies by their version location in ascending order (by file path, line number, and line position).</summary>
    /// <param name="dependencies">The dependencies to order.</param>
    /// <returns>The ordered dependencies.</returns>
    public static IEnumerable<Dependency> OrderByVersionLocation(this IEnumerable<Dependency> dependencies)
    {
        return dependencies
            .Select(dep =>
            {
                var location = dep.VersionLocation ?? dep.NameLocation;
                var lineInfo = location as ILocationLineInfo;

                return (Dependency: dep, location?.FilePath, LineNumber: lineInfo?.LineNumber ?? int.MaxValue, LinePosition: lineInfo?.LinePosition ?? int.MaxValue);
            })
            .OrderBy(item => item.FilePath, StringComparer.Ordinal)
            .ThenBy(item => item.LineNumber)
            .ThenBy(item => item.LinePosition)
            .Select(item => item.Dependency);
    }

    /// <summary>Orders dependencies by their version location in descending order (by file path, line number, and line position).</summary>
    /// <param name="dependencies">The dependencies to order.</param>
    /// <returns>The ordered dependencies.</returns>
    public static IEnumerable<Dependency> OrderByVersionLocationDescending(this IEnumerable<Dependency> dependencies)
    {
        return dependencies
            .Select(dep =>
            {
                var location = dep.VersionLocation ?? dep.NameLocation;
                var lineInfo = location as ILocationLineInfo;

                return (Dependency: dep, location?.FilePath, LineNumber: lineInfo?.LineNumber ?? int.MaxValue, LinePosition: lineInfo?.LinePosition ?? int.MaxValue);
            })
            .OrderByDescending(item => item.FilePath, StringComparer.Ordinal)
            .ThenByDescending(item => item.LineNumber)
            .ThenByDescending(item => item.LinePosition)
            .Select(item => item.Dependency);
    }
}
