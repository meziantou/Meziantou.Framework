using Meziantou.Framework.Versioning;

namespace Meziantou.Framework.DependencyScanning.Tool;

/// <summary>
/// Parses versions that may omit the minor or the patch component, such as the Docker tag <c>8.0</c>
/// or the npm range <c>~7</c>. <see cref="SemanticVersion.TryParse(string, out SemanticVersion)"/> requires
/// all three components, so those values would otherwise be rejected as unsupported.
/// </summary>
internal static class PartialSemanticVersion
{
    /// <summary>Parses <paramref name="value"/>, padding the missing components with zeros.</summary>
    /// <param name="componentCount">The number of numeric components present in <paramref name="value"/>, so callers can write an updated version back in the same shape.</param>
    public static bool TryParse(string? value, out SemanticVersion? version, out int componentCount)
    {
        version = null;
        componentCount = 0;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var firstSuffixIndex = value.IndexOfAny(['-', '+']);
        var numericPart = firstSuffixIndex >= 0 ? value[..firstSuffixIndex] : value;
        componentCount = numericPart.Count(c => c == '.') + 1;
        if (componentCount > 3)
            return false;

        if (numericPart.Split('.').Any(static part => !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            return false;

        if (!SemanticVersion.TryParse(PadToThreeComponents(value, componentCount), out var parsedVersion))
            return false;

        version = parsedVersion;
        return true;
    }

    private static string PadToThreeComponents(string value, int componentCount)
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
