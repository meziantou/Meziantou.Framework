using Meziantou.Framework;
using Meziantou.Framework.DependencyScanning;
using NuGet.Versioning;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal abstract class PackageUpdater
{
    public virtual async Task<string?> UpdateAsync(Dependency dependency, CancellationToken cancellationToken)
    {
        if (dependency.Name is null || !IsSupported(dependency))
            return null;

        SemanticVersion? maxVersion = null;
        string? rawMaxVersion = null;
        await foreach (var version in GetVersionsAsync(dependency, cancellationToken).ConfigureAwait(false))
        {
            var parsedVersion = ParseVersion(version);
            if (parsedVersion is null)
                continue;

            if (ConsiderVersion(dependency, parsedVersion))
            {
                if (maxVersion is null || maxVersion < parsedVersion)
                {
                    maxVersion = parsedVersion;
                    rawMaxVersion = version;
                }
            }
        }

        if (rawMaxVersion is null)
            return null;

        await dependency.UpdateVersionAsync(rawMaxVersion, cancellationToken).ConfigureAwait(false);
        return rawMaxVersion;
    }

    protected virtual IAsyncEnumerable<string> GetVersionsAsync(Dependency dependency, CancellationToken cancellationToken)
    {
        return GetVersionsAsync(dependency.Name!, cancellationToken);
    }

    public abstract IAsyncEnumerable<string> GetVersionsAsync(string packageName, CancellationToken cancellationToken);

    public abstract Task UpdateLockFileAsync(FullPath rootDirectory, IEnumerable<Dependency> updatedDependencies, CancellationToken cancellationToken);

    protected virtual SemanticVersion? ParseVersion(string? value)
    {
        if (NuGetVersion.TryParse(value, out var version))
            return version;

        return null;
    }

    protected abstract bool IsSupported(Dependency dependency);

    protected virtual bool ConsiderVersion(Dependency dependency, SemanticVersion version)
    {
        var currentVersion = ParseVersion(dependency.Version);
        if (currentVersion == null)
            return true;

        // Only consider newer version
        if (version <= currentVersion)
            return false;

        // Stable version are always considered
        if (version.IsPrerelease)
        {
            // 1.0.0-alpha -> 1.0.0-beta OK
            // 1.0.0-alpha -> 2.0.0      OK
            // 1.0.0       -> 2.0.0-beta KO
            // 1.0.0-alpha -> 2.0.0-beta KO
            if (!currentVersion.IsPrerelease)
                return false;

            if ((version.Major, version.Minor, version.Patch) == (currentVersion.Major, currentVersion.Minor, currentVersion.Patch))
                return true;
        }
        return true;
    }
}
