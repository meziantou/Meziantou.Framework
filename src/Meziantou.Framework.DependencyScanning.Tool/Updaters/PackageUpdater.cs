namespace Meziantou.Framework.DependencyScanning.Tool;

internal abstract class PackageUpdater
{
    public abstract VersioningStrategy VersioningStrategy { get; set; }
    public int MinimumAge { get; set; }
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    public virtual async Task<string?> UpdateAsync(Dependency dependency, CancellationToken cancellationToken)
    {
        var updatedVersion = await GetUpdatedVersionAsync(dependency, cancellationToken).ConfigureAwait(false);
        if (updatedVersion is null)
            return null;

        await dependency.UpdateVersionAsync(updatedVersion, cancellationToken).ConfigureAwait(false);
        return updatedVersion;
    }

    public async Task<string?> GetUpdatedVersionAsync(Dependency dependency, CancellationToken cancellationToken)
    {
        if (dependency.Name is null || !IsSupported(dependency))
            return null;

        var versioningStrategy = VersioningStrategy ?? throw new InvalidOperationException($"{nameof(VersioningStrategy)} cannot be null");
        if (!versioningStrategy.IsSupportedVersion(dependency.Version))
            return null;

        var minimumAgeTimeSpan = MinimumAge > 0 ? TimeSpan.FromDays(MinimumAge) : (TimeSpan?)null;
        var now = TimeProvider.GetUtcNow().UtcDateTime;

        string? rawMaxVersion = null;
        await foreach (var (version, publishedDate) in GetVersionsAsync(dependency, cancellationToken).ConfigureAwait(false))
        {
            if (!versioningStrategy.IsSupportedVersion(version))
                continue;

            if (!versioningStrategy.IsCompatibleVersion(dependency.Version, version))
                continue;

            // Filter by minimum age if enabled
            if (minimumAgeTimeSpan.HasValue && publishedDate.HasValue)
            {
                var age = now - publishedDate.Value;
                if (age < minimumAgeTimeSpan.Value)
                    continue;
            }

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

        return updatedReference;
    }

    protected abstract IAsyncEnumerable<PackageVersion> GetVersionsAsync(Dependency dependency, CancellationToken cancellationToken);

    public abstract Task UpdateLockFileAsync(FullPath rootDirectory, IEnumerable<Dependency> updatedDependencies, CancellationToken cancellationToken);

    protected abstract bool IsSupported(Dependency dependency);
}
