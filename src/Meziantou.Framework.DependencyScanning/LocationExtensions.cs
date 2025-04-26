namespace Meziantou.Framework.DependencyScanning;
public static class LocationExtensions
{
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
