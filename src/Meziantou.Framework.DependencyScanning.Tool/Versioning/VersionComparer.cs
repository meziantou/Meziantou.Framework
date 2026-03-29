using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.DependencyScanning;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal sealed class VersionComparer : IComparer<string>, IEqualityComparer<string>
{
    private readonly VersioningStrategy _strategy;

    public VersionComparer(VersioningStrategy strategy)
    {
        _strategy = strategy;
    }

    public static VersionComparer GetComparer(DependencyType type)
    {
        return new VersionComparer(VersioningStrategies.GetStrategy(type));
    }

    public int Compare(string? x, string? y)
    {
        return _strategy.CompareVersions(x, y);
    }

    public bool Equals(string? x, string? y)
    {
        return _strategy.CompareVersions(x, y) is 0;
    }

    public int GetHashCode([DisallowNull] string obj)
    {
        return 0;
    }
}
