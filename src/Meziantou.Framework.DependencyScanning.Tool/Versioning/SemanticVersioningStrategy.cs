using Meziantou.Framework.Versioning;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal sealed class SemanticVersioningStrategy : VersioningStrategy
{
    private readonly bool _allowVPrefix;

    public static SemanticVersioningStrategy Strict { get; } = new(allowVPrefix: false);

    public static SemanticVersioningStrategy PrefixAllowed { get; } = new(allowVPrefix: true);

    private SemanticVersioningStrategy(bool allowVPrefix)
    {
        _allowVPrefix = allowVPrefix;
    }

    public override bool IsSupportedVersion(string? version)
    {
        return TryParseVersion(version, out _);
    }

    public override int CompareVersions(string? x, string? y)
    {
        if (!TryParseVersion(x, out var left))
            throw new ArgumentException($"Version '{x}' is not a valid semantic version", nameof(x));

        if (!TryParseVersion(y, out var right))
            throw new ArgumentException($"Version '{y}' is not a valid semantic version", nameof(y));

        return left!.CompareTo(right);
    }

    public override bool IsCompatibleVersion(string? currentVersion, string candidateVersion)
    {
        if (!TryParseVersion(currentVersion, out var current))
            return false;

        if (!TryParseVersion(candidateVersion, out var candidate))
            return false;

        if (current is null || candidate is null)
            return false;

        if (candidate <= current)
            return false;

        if (!current.IsPrerelease && candidate.IsPrerelease)
            return false;

        if (candidate.IsPrerelease && current.IsPrerelease && (candidate.Major, candidate.Minor, candidate.Patch) != (current.Major, current.Minor, current.Patch))
            return false;

        return true;
    }

    private bool TryParseVersion(string? value, out SemanticVersion? version)
    {
        version = null;

        // SemanticVersion.TryParse skips a leading 'v' on its own, so stripping it here made no difference
        // and 'Strict' silently accepted 'v1.0.0'. Rejecting it is what makes the two strategies differ.
        if (!_allowVPrefix && value is ['v' or 'V', ..])
            return false;

        return SemanticVersion.TryParse(value, out version);
    }
}
