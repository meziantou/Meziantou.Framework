using Meziantou.Framework.Versioning;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal sealed class NpmVersioningStrategy : VersioningStrategy
{
    private static readonly string[] VersionPrefixes = ["~", "^", "<=", "<", ">=", ">", "="];

    public static NpmVersioningStrategy Instance { get; } = new();

    private NpmVersioningStrategy()
    {
    }

    public override bool IsSupportedVersion(string? version)
    {
        return TryGetSemanticVersion(version, out _, out _);
    }

    public override int CompareVersions(string? x, string? y)
    {
        if (!TryGetSemanticVersion(x, out var left, out _))
            throw new ArgumentException($"Version '{x}' is not a valid npm semantic version", nameof(x));

        if (!TryGetSemanticVersion(y, out var right, out _))
            throw new ArgumentException($"Version '{y}' is not a valid npm semantic version", nameof(y));

        return left!.CompareTo(right);
    }

    public override bool IsCompatibleVersion(string? currentVersion, string candidateVersion)
    {
        if (!TryGetSemanticVersion(currentVersion, out var current, out _))
            return false;

        if (!TryGetSemanticVersion(candidateVersion, out var candidate, out _))
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

    public override string GetUpdateReferenceText(string? currentVersion, string newVersion)
    {
        if (!TryGetSemanticVersion(currentVersion, out _, out var currentPrefix) || !TryGetSemanticVersion(newVersion, out _, out _))
            return newVersion;

        return string.IsNullOrEmpty(currentPrefix) ? newVersion : currentPrefix + newVersion;
    }

    private static bool TryGetSemanticVersion(string? value, out SemanticVersion? semanticVersion, out string? prefix)
    {
        semanticVersion = null;
        prefix = null;

        if (string.IsNullOrEmpty(value))
            return false;

        if (TryParseVersion(value, out semanticVersion))
            return true;

        foreach (var knownPrefix in VersionPrefixes)
        {
            if (!value.StartsWith(knownPrefix, StringComparison.Ordinal))
                continue;

            var unprefixedVersion = value[knownPrefix.Length..];
            if (TryParseVersion(unprefixedVersion, out semanticVersion))
            {
                prefix = knownPrefix;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseVersion(string value, out SemanticVersion? semanticVersion)
    {
        if (value is ['v' or 'V', .. var suffix])
        {
            value = suffix;
        }

        return SemanticVersion.TryParse(value, out semanticVersion);
    }
}
