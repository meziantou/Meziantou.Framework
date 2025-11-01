namespace Meziantou.Framework.Versioning;

/// <summary>Provides extension methods for <see cref="SemanticVersion"/>.</summary>
public static class SemanticVersionExtensions
{
    /// <summary>Gets the next patch version. For prerelease versions, returns the version without the prerelease tag. For stable versions, increments the patch number.</summary>
    public static SemanticVersion NextPatchVersion(this SemanticVersion semanticVersion)
    {
        if (semanticVersion.IsPrerelease)
        {
            return new SemanticVersion(semanticVersion.Major, semanticVersion.Minor, semanticVersion.Patch);
        }

        return new SemanticVersion(semanticVersion.Major, semanticVersion.Minor, semanticVersion.Patch + 1);
    }

    /// <summary>Gets the next minor version. For prerelease versions, returns the version without the prerelease tag. For stable versions, increments the minor number and resets the patch number to zero.</summary>
    public static SemanticVersion NextMinorVersion(this SemanticVersion semanticVersion)
    {
        if (semanticVersion.IsPrerelease)
        {
            return new SemanticVersion(semanticVersion.Major, semanticVersion.Minor, 0);
        }

        return new SemanticVersion(semanticVersion.Major, semanticVersion.Minor + 1, 0);
    }

    /// <summary>Gets the next major version. For prerelease versions, returns the version without the prerelease tag. For stable versions, increments the major number and resets the minor and patch numbers to zero.</summary>
    public static SemanticVersion NextMajorVersion(this SemanticVersion semanticVersion)
    {
        if (semanticVersion.IsPrerelease)
        {
            return new SemanticVersion(semanticVersion.Major, 0, 0);
        }

        return new SemanticVersion(semanticVersion.Major + 1, 0, 0);
    }
}
