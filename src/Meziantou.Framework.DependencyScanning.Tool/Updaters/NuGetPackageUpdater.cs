using System.Runtime.CompilerServices;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal sealed class NuGetPackageUpdater : PackageUpdater
{
    private const string NuGetOrgSource = "https://api.nuget.org/v3/index.json";
    public override VersioningStrategy VersioningStrategy { get; set; } = NuGetVersioningStrategy.Instance;

    protected override bool IsSupported(Dependency dependency) => dependency.Type is DependencyType.NuGet && dependency.Name is not null;

    protected override async IAsyncEnumerable<PackageVersion> GetVersionsAsync(Dependency dependency, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var dependencyLocation = dependency.VersionLocation?.FilePath ?? dependency.NameLocation?.FilePath;
        if (dependencyLocation is null || dependency.Name is null)
            yield break;

        var resolution = NuGetPackageSourceResolver.Resolve(FullPath.FromPath(dependencyLocation), dependency.Name);
        IReadOnlyList<string> sources;
        if (resolution.PackageSources.Count > 0)
        {
            sources = resolution.PackageSources;
        }
        else if (resolution.HasSourceMappings)
        {
            sources = [];
        }
        else if (resolution.AllConfiguredSources.Count > 0)
        {
            sources = resolution.AllConfiguredSources;
        }
        else
        {
            sources = [NuGetOrgSource];
        }

        foreach (var source in sources)
        {
            await foreach (var versionInfo in GetVersionsFromSourceWithMetadataAsync(source, dependency.Name, cancellationToken).ConfigureAwait(false))
            {
                yield return versionInfo;
            }
        }
    }

    public override async Task UpdateLockFileAsync(FullPath rootDirectory, IEnumerable<Dependency> updatedDependencies, CancellationToken cancellationToken)
    {
        if (!updatedDependencies.Any(dep => dep.Type is DependencyType.NuGet))
            return;

        // Enumerated lazily and tolerantly: Directory.GetFiles materializes the whole tree eagerly and throws
        // on the first directory it cannot read, which aborted the restore pass over a single bad permission.
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.None,
        };

        foreach (var lockFilePath in Directory.EnumerateFiles(rootDirectory, "packages.lock.json", enumerationOptions))
        {
            var lockFile = FullPath.FromPath(lockFilePath);

            // '*.*proj' rather than '*.csproj': F#, VB and the other MSBuild project types use lock files too.
            foreach (var project in Directory.EnumerateFiles(lockFile.Parent, "*.*proj", SearchOption.TopDirectoryOnly))
            {
                var result = await ProcessWrapper.Create("dotnet")
                    .WithArguments("restore", project, "--no-cache")
                    .WithValidation(ProcessValidationMode.None)
                    .ExecuteBufferedAsync(cancellationToken);

                if (!result.ExitCode.IsSuccess)
                {
                    Console.Error.WriteLine($"Unable to update lock file '{lockFile}':\n{result.Output}");
                }
            }
        }
    }

    private static async IAsyncEnumerable<PackageVersion> GetVersionsFromSourceWithMetadataAsync(string sourceUrl, string packageName, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var cache = new SourceCacheContext { NoCache = true };
        var repository = Repository.Factory.GetCoreV3(sourceUrl);
        var metadataResource = await repository.GetResourceAsync<PackageMetadataResource>(cancellationToken).ConfigureAwait(false);

        if (metadataResource is null)
        {
            // Fallback to non-metadata version retrieval
            var findResource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken).ConfigureAwait(false);
            if (findResource is not null)
            {
                IEnumerable<NuGetVersion> versions = await findResource.GetAllVersionsAsync(packageName, cache, NullLogger.Instance, cancellationToken).ConfigureAwait(false);
                foreach (var version in versions)
                {
                    yield return new PackageVersion(version.ToString(), PublishedDate: null);
                }
            }

            yield break;
        }

        IEnumerable<IPackageSearchMetadata>? metadata = null;
        try
        {
            metadata = await metadataResource.GetMetadataAsync(packageName, includePrerelease: true, includeUnlisted: false, cache, NullLogger.Instance, cancellationToken).ConfigureAwait(false);
        }
        catch (FatalProtocolException)
        {
            // The source has no usable metadata resource (a V2 feed, typically): fall back to the plain
            // version list. Narrow on purpose - a bare catch also swallowed cancellation, and it turned a
            // transient failure into a silent loss of publication dates, which disables --minimum-age.
        }

        if (metadata is null)
        {
            var findResource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken).ConfigureAwait(false);
            if (findResource is not null)
            {
                IEnumerable<NuGetVersion> versions = await findResource.GetAllVersionsAsync(packageName, cache, NullLogger.Instance, cancellationToken).ConfigureAwait(false);
                foreach (var version in versions)
                {
                    yield return new PackageVersion(version.ToString(), PublishedDate: null);
                }
            }

            yield break;
        }

        foreach (var versionMetadata in metadata)
        {
            yield return new PackageVersion(versionMetadata.Identity.Version.ToString(), versionMetadata.Published?.UtcDateTime);
        }
    }
}

