using NuGet.Versioning;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal sealed class NuGetVersioningStrategy : VersioningStrategy
{
    public static NuGetVersioningStrategy Instance { get; } = new();

    private NuGetVersioningStrategy()
    {
    }

    public override bool IsSupportedVersion(string? version)
    {
        return NuGetVersion.TryParse(version, out _);
    }

    public override int CompareVersions(string? x, string? y)
    {
        if (!NuGetVersion.TryParse(x, out var left))
            throw new ArgumentException($"Version '{x}' is not a valid NuGet version", nameof(x));

        if (!NuGetVersion.TryParse(y, out var right))
            throw new ArgumentException($"Version '{y}' is not a valid NuGet version", nameof(y));

        return left.CompareTo(right);
    }

    public override bool IsCompatibleVersion(string? currentVersion, string candidateVersion)
    {
        if (!NuGetVersion.TryParse(currentVersion, out var current))
            return false;

        if (!NuGetVersion.TryParse(candidateVersion, out var candidate))
            return false;

        if (candidate <= current)
            return false;

        if (!current.IsPrerelease && candidate.IsPrerelease)
            return false;

        if (candidate.IsPrerelease && current.IsPrerelease && (candidate.Major, candidate.Minor, candidate.Patch) != (current.Major, current.Minor, current.Patch))
            return false;

        return true;
    }
}
