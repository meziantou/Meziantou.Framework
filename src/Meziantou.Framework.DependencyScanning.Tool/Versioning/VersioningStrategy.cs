namespace Meziantou.Framework.DependencyScanning.Tool;

internal abstract class VersioningStrategy
{
    public abstract bool IsSupportedVersion(string? version);

    public abstract int CompareVersions(string? x, string? y);

    public virtual bool IsCompatibleVersion(string? currentVersion, string candidateVersion)
    {
        if (!IsSupportedVersion(currentVersion) || !IsSupportedVersion(candidateVersion))
            return false;

        if (CompareVersions(candidateVersion, currentVersion) <= 0)
            return false;

        return true;
    }

    public virtual string GetUpdateReferenceText(string? currentVersion, string newVersion)
    {
        return newVersion;
    }
}
