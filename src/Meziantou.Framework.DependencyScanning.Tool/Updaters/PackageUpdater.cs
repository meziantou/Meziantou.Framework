using Meziantou.Framework;
using Meziantou.Framework.DependencyScanning;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal abstract class PackageUpdater
{
    public abstract VersioningStrategy VersioningStrategy { get; set; }

    public virtual async Task<string?> UpdateAsync(Dependency dependency, CancellationToken cancellationToken)
    {
        if (dependency.Name is null || !IsSupported(dependency))
            return null;

        var versioningStrategy = VersioningStrategy ?? throw new InvalidOperationException($"{nameof(VersioningStrategy)} cannot be null");
        if (!versioningStrategy.IsSupportedVersion(dependency.Version))
            return null;

        string? rawMaxVersion = null;
        await foreach (var version in GetVersionsAsync(dependency, cancellationToken).ConfigureAwait(false))
        {
            if (!versioningStrategy.IsSupportedVersion(version))
                continue;

            if (!versioningStrategy.IsCompatibleVersion(dependency.Version, version))
                continue;

            if (rawMaxVersion is null || versioningStrategy.CompareVersions(version, rawMaxVersion) > 0)
            {
                rawMaxVersion = version;
            }
        }

        if (rawMaxVersion is null)
            return null;

        var updatedReference = versioningStrategy.GetUpdateReferenceText(dependency.Version, rawMaxVersion);
        if (string.Equals(updatedReference, dependency.Version, StringComparison.Ordinal))
            return null;

        await dependency.UpdateVersionAsync(updatedReference, cancellationToken).ConfigureAwait(false);
        return updatedReference;
    }

    protected virtual IAsyncEnumerable<string> GetVersionsAsync(Dependency dependency, CancellationToken cancellationToken)
    {
        return GetVersionsAsync(dependency.Name!, cancellationToken);
    }

    public abstract IAsyncEnumerable<string> GetVersionsAsync(string packageName, CancellationToken cancellationToken);

    public abstract Task UpdateLockFileAsync(FullPath rootDirectory, IEnumerable<Dependency> updatedDependencies, CancellationToken cancellationToken);

    protected abstract bool IsSupported(Dependency dependency);
}
