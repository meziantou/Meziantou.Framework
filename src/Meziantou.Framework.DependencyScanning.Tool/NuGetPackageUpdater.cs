using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Meziantou.Framework;
using Meziantou.Framework.DependencyScanning;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal sealed class NuGetPackageUpdater : PackageUpdater
{
    protected override bool IsSupported(Dependency dependency) => dependency.Type is DependencyType.NuGet;

    public override async IAsyncEnumerable<string> GetVersionsAsync(string packageName, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var cache = new SourceCacheContext { NoCache = true };
        var repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");

        var resource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken).ConfigureAwait(false);
        var versions = await resource.GetAllVersionsAsync(packageName, cache, NullLogger.Instance, cancellationToken).ConfigureAwait(false);
        foreach (var version in versions)
        {
            yield return version.ToString();
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
}
