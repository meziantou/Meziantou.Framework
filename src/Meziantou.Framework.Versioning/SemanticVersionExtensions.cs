namespace Meziantou.Framework.Versioning;

/// <summary>Provides extension methods for <see cref="SemanticVersion"/>.</summary>
public static class SemanticVersionExtensions
{
    /// <summary>Gets the next patch version. For prerelease versions, returns the version without the prerelease tag. For stable versions, increments the patch number.</summary>
    /// <exception cref="OverflowException">The patch number is <see cref="int.MaxValue"/>.</exception>
    public static SemanticVersion NextPatchVersion(this SemanticVersion semanticVersion)
    {
        if (semanticVersion.IsPrerelease)
        {
            return new SemanticVersion(semanticVersion.Major, semanticVersion.Minor, semanticVersion.Patch);
        }

        return new SemanticVersion(semanticVersion.Major, semanticVersion.Minor, checked(semanticVersion.Patch + 1));
    }

    /// <summary>Gets the next minor version: the smallest stable version that is greater than this version and whose patch number is zero. For a prerelease of a minor version (e.g., <c>1.2.0-beta</c>), returns that minor version (<c>1.2.0</c>). Otherwise, increments the minor number and resets the patch number to zero (<c>1.2.3-beta</c> gives <c>1.3.0</c>).</summary>
    /// <exception cref="OverflowException">The minor number is <see cref="int.MaxValue"/> and has to be incremented.</exception>
    public static SemanticVersion NextMinorVersion(this SemanticVersion semanticVersion)
    {
        // 1.2.3-beta precedes 1.2.3 but follows 1.2.0, so its next minor version is 1.3.0
        if (semanticVersion.IsPrerelease && semanticVersion.Patch == 0)
        {
            return new SemanticVersion(semanticVersion.Major, semanticVersion.Minor, 0);
        }

        return new SemanticVersion(semanticVersion.Major, checked(semanticVersion.Minor + 1), 0);
    }

    /// <summary>Gets the next major version: the smallest stable version that is greater than this version and whose minor and patch numbers are zero. For a prerelease of a major version (e.g., <c>2.0.0-beta</c>), returns that major version (<c>2.0.0</c>). Otherwise, increments the major number and resets the minor and patch numbers to zero (<c>1.2.3-beta</c> gives <c>2.0.0</c>).</summary>
    /// <exception cref="OverflowException">The major number is <see cref="int.MaxValue"/> and has to be incremented.</exception>
    public static SemanticVersion NextMajorVersion(this SemanticVersion semanticVersion)
    {
        // 1.2.3-beta follows 1.0.0, so its next major version is 2.0.0
        if (semanticVersion.IsPrerelease && semanticVersion.Minor == 0 && semanticVersion.Patch == 0)
        {
            return new SemanticVersion(semanticVersion.Major, 0, 0);
        }

        return new SemanticVersion(checked(semanticVersion.Major + 1), 0, 0);
    }
}
