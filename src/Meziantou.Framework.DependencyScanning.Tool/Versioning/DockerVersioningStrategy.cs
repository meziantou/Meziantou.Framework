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
        return TryGetSemanticVersion(version, out _, out _, out _);
    }

    public override int CompareVersions(string? x, string? y)
    {
        if (!TryGetSemanticVersion(x, out var left, out _, out _))
            throw new ArgumentException($"Version '{x}' is not a valid docker version", nameof(x));

        if (!TryGetSemanticVersion(y, out var right, out _, out _))
            throw new ArgumentException($"Version '{y}' is not a valid docker version", nameof(y));

        return left!.CompareTo(right);
    }

    public override bool IsCompatibleVersion(string? currentVersion, string candidateVersion)
    {
        if (!TryGetSemanticVersion(currentVersion, out var current, out var currentSuffix, out var currentComponentCount))
            return false;

        if (!TryGetSemanticVersion(candidateVersion, out var candidate, out var candidateSuffix, out var candidateComponentCount))
            return false;

        if (!string.Equals(currentSuffix, candidateSuffix, StringComparison.Ordinal))
            return false;

        // The candidate is written back verbatim as the image tag, so it must have the same shape as the
        // current tag: '8.0' must not be replaced by '8.1.2', which pins the image far more tightly.
        if (currentComponentCount != candidateComponentCount)
            return false;

        return candidate > current;
    }

    private static bool TryGetSemanticVersion(string? value, out SemanticVersion? semanticVersion, out string? suffix, out int componentCount)
    {
        semanticVersion = null;
        suffix = null;
        componentCount = 0;

        if (string.IsNullOrEmpty(value))
            return false;

        var hyphenIndex = value.IndexOf('-', StringComparison.Ordinal);
        if (hyphenIndex < 0)
            return PartialSemanticVersion.TryParse(value, out semanticVersion, out componentCount);

        suffix = value[(hyphenIndex + 1)..];
        return PartialSemanticVersion.TryParse(value[..hyphenIndex], out semanticVersion, out componentCount);
    }
}
