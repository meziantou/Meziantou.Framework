using System.Text;
using System.Globalization;
using Meziantou.Framework.Versioning;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal sealed class GitHubActionsVersioningStrategy : VersioningStrategy
{
    public static GitHubActionsVersioningStrategy Instance { get; } = new();

    private GitHubActionsVersioningStrategy()
    {
    }

    public override bool IsSupportedVersion(string? version)
    {
        return TryParseVersion(version, out _, out _, out _);
    }

    public override int CompareVersions(string? x, string? y)
    {
        if (!TryParseVersion(x, out var left, out _, out _))
            throw new ArgumentException($"Version '{x}' is not a valid GitHub Actions version", nameof(x));

        if (!TryParseVersion(y, out var right, out _, out _))
            throw new ArgumentException($"Version '{y}' is not a valid GitHub Actions version", nameof(y));

        return left!.CompareTo(right);
    }

    public override bool IsCompatibleVersion(string? currentVersion, string candidateVersion)
    {
        if (!TryParseVersion(currentVersion, out var current, out var currentHasPrefix, out var currentComponentCount))
            return false;

        if (!TryParseVersion(candidateVersion, out var candidate, out var candidateHasPrefix, out var candidateComponentCount))
            return false;

        if (currentHasPrefix != candidateHasPrefix)
            return false;

        if (currentComponentCount != candidateComponentCount)
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

    private static bool TryParseVersion(string? value, out SemanticVersion? version, out bool hasPrefix, out int componentCount)
    {
        version = null;
        hasPrefix = false;
        componentCount = 0;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (value is ['v' or 'V', .. var suffix])
        {
            hasPrefix = true;
            value = suffix;
        }

        var firstSuffixIndex = value.IndexOfAny(['-', '+']);
        var numericPart = firstSuffixIndex >= 0 ? value[..firstSuffixIndex] : value;
        componentCount = numericPart.Count(c => c == '.') + 1;
        if (componentCount is < 1 or > 3)
            return false;

        if (numericPart.Split('.').Any(static part => !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            return false;

        var normalizedVersion = NormalizeToThreeComponents(value, componentCount);

        if (!SemanticVersion.TryParse(normalizedVersion, out var parsedVersion))
            return false;

        version = parsedVersion;
        return true;
    }

    private static string NormalizeToThreeComponents(string value, int componentCount)
    {
        if (componentCount is 3)
            return value;

        var firstSuffixIndex = value.IndexOfAny(['-', '+']);
        var core = firstSuffixIndex >= 0 ? value[..firstSuffixIndex] : value;
        var suffix = firstSuffixIndex >= 0 ? value[firstSuffixIndex..] : string.Empty;

        var sb = new StringBuilder(core);
        for (var i = componentCount; i < 3; i++)
        {
            sb.Append(".0");
        }

        sb.Append(suffix);
        return sb.ToString();
    }
}
