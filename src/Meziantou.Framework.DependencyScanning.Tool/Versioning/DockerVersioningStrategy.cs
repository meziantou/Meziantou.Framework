using Meziantou.Framework.Versioning;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal sealed class DockerVersioningStrategy : VersioningStrategy
{
    public static DockerVersioningStrategy Instance { get; } = new();

    private DockerVersioningStrategy()
    {
    }

    public override bool IsSupportedVersion(string? version)
    {
        return TryGetSemanticVersion(version, out _, out _);
    }

    public override int CompareVersions(string? x, string? y)
    {
        if (!TryGetSemanticVersion(x, out var left, out _))
            throw new ArgumentException($"Version '{x}' is not a valid docker version", nameof(x));

        if (!TryGetSemanticVersion(y, out var right, out _))
            throw new ArgumentException($"Version '{y}' is not a valid docker version", nameof(y));

        return left!.CompareTo(right);
    }

    public override bool IsCompatibleVersion(string? currentVersion, string candidateVersion)
    {
        if (!TryGetSemanticVersion(currentVersion, out var current, out var currentSuffix))
            return false;

        if (!TryGetSemanticVersion(candidateVersion, out var candidate, out var candidateSuffix))
            return false;

        if (!string.Equals(currentSuffix, candidateSuffix, StringComparison.Ordinal))
            return false;

        return candidate > current;
    }

    private static bool TryGetSemanticVersion(string? value, out SemanticVersion? semanticVersion, out string? suffix)
    {
        semanticVersion = null;
        suffix = null;

        if (string.IsNullOrEmpty(value))
            return false;

        var hyphenIndex = value.IndexOf('-', StringComparison.Ordinal);
        if (hyphenIndex < 0)
            return SemanticVersion.TryParse(value, out semanticVersion);

        suffix = value[(hyphenIndex + 1)..];
        return SemanticVersion.TryParse(value[..hyphenIndex], out semanticVersion);
    }
}
