namespace Meziantou.Framework.Versioning;

/// <summary>
/// Provides extension methods for <see cref="SemanticVersion"/>.
/// </summary>
public static class SemanticVersionExtensions
{
    /// <summary>
    /// Gets the next patch version. If the current version is a prerelease, returns the release version without incrementing the patch number.
    /// </summary>
    /// <param name="semanticVersion">The current semantic version.</param>
    /// <returns>The next patch version.</returns>
    public static SemanticVersion NextPatchVersion(this SemanticVersion semanticVersion)
    {
        if (semanticVersion.IsPrerelease)
        {
            return new SemanticVersion(semanticVersion.Major, semanticVersion.Minor, semanticVersion.Patch);
        }

        return new SemanticVersion(semanticVersion.Major, semanticVersion.Minor, semanticVersion.Patch + 1);
    }

    /// <summary>
    /// Gets the next minor version. If the current version is a prerelease, returns the release version without incrementing the minor number.
    /// </summary>
    /// <param name="semanticVersion">The current semantic version.</param>
    /// <returns>The next minor version.</returns>
    public static SemanticVersion NextMinorVersion(this SemanticVersion semanticVersion)
    {
        if (semanticVersion.IsPrerelease)
        {
            return new SemanticVersion(semanticVersion.Major, semanticVersion.Minor, 0);
        }

        return new SemanticVersion(semanticVersion.Major, semanticVersion.Minor + 1, 0);
    }

    /// <summary>
    /// Gets the next major version. If the current version is a prerelease, returns the release version without incrementing the major number.
    /// </summary>
    /// <param name="semanticVersion">The current semantic version.</param>
    /// <returns>The next major version.</returns>
    public static SemanticVersion NextMajorVersion(this SemanticVersion semanticVersion)
    {
        if (semanticVersion.IsPrerelease)
        {
            return new SemanticVersion(semanticVersion.Major, 0, 0);
        }

        return new SemanticVersion(semanticVersion.Major + 1, 0, 0);
    }
}
