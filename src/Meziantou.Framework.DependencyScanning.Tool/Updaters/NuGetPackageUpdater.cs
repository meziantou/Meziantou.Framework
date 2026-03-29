using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Meziantou.Framework;
using Meziantou.Framework.DependencyScanning;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal sealed class NuGetPackageUpdater : PackageUpdater
{
    private const string NuGetOrgSource = "https://api.nuget.org/v3/index.json";
    public override VersioningStrategy VersioningStrategy { get; set; } = NuGetVersioningStrategy.Instance;

    protected override bool IsSupported(Dependency dependency) => dependency.Type is DependencyType.NuGet;

    protected override async IAsyncEnumerable<string> GetVersionsAsync(Dependency dependency, [EnumeratorCancellation] CancellationToken cancellationToken)
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
            await foreach (var version in GetVersionsFromSourceAsync(source, dependency.Name, cancellationToken).ConfigureAwait(false))
            {
                yield return version;
            }
        }
    }

    public override async IAsyncEnumerable<string> GetVersionsAsync(string packageName, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var version in GetVersionsFromSourceAsync(NuGetOrgSource, packageName, cancellationToken).ConfigureAwait(false))
        {
            yield return version;
        }
    }

    public override async Task UpdateLockFileAsync(FullPath rootDirectory, IEnumerable<Dependency> updatedDependencies, CancellationToken cancellationToken)
    {
        if (!updatedDependencies.Any(dep => dep.Type is DependencyType.NuGet))
            return;

        var lockFiles = Directory.GetFiles(rootDirectory, "packages.lock.json", SearchOption.AllDirectories).Select(FullPath.FromPath);
        foreach (var lockFile in lockFiles)
        {
            var csprojs = Directory.GetFiles(lockFile.Parent, "*.csproj", SearchOption.TopDirectoryOnly);
            foreach (var csproj in csprojs)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    ArgumentList =
                    {
                        "restore",
                        csproj,
                        "--no-cache",
                    },
                };
                var result = await psi.RunAsTaskAsync(cancellationToken).ConfigureAwait(false);
                if (result.ExitCode is not 0)
                {
                    Console.WriteLine($"Unable to update lock file '{lockFile}':\n{result.Output}");
                }
            }
        }
    }

    private static async IAsyncEnumerable<string> GetVersionsFromSourceAsync(string sourceUrl, string packageName, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var cache = new SourceCacheContext { NoCache = true };
        var repository = Repository.Factory.GetCoreV3(sourceUrl);
        var resource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken).ConfigureAwait(false);

        if (resource is null)
            yield break;

        IEnumerable<NuGetVersion> versions = await resource.GetAllVersionsAsync(packageName, cache, NullLogger.Instance, cancellationToken).ConfigureAwait(false);

        foreach (var version in versions)
        {
            yield return version.ToString();
        }
    }
}
