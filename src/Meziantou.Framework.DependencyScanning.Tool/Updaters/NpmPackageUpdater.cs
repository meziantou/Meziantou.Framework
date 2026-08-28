using System.ComponentModel;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Meziantou.Framework;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal sealed class NpmPackageUpdater : PackageUpdater
{
    private static readonly HttpClient HttpClient = new();

    public override VersioningStrategy VersioningStrategy { get; set; } = NpmVersioningStrategy.Instance;

    protected override bool IsSupported(Dependency dependency) => dependency.Type is DependencyType.Npm;

    protected override async IAsyncEnumerable<PackageVersion> GetVersionsAsync(Dependency dependency, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var dependencyLocation = dependency.VersionLocation?.FilePath ?? dependency.NameLocation?.FilePath;
        if (dependencyLocation is null || dependency.Name is null)
            yield break;

        var registry = NpmPackageSourceResolver.ResolveRegistry(FullPath.FromPath(dependencyLocation), dependency.Name);
        await foreach (var versionInfo in GetVersionsFromRegistryWithMetadataAsync(registry, dependency.Name, cancellationToken).ConfigureAwait(false))
        {
            yield return versionInfo;
        }
    }

    private static async IAsyncEnumerable<PackageVersion> GetVersionsFromRegistryWithMetadataAsync(Uri registryUri, string packageName, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var packageUri = new Uri(registryUri, packageName);
        using var packageResponse = await HttpClient.GetAsync(packageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (packageResponse.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.BadRequest)
            yield break;

        packageResponse.EnsureSuccessStatusCode();
        var package = await packageResponse.Content.ReadFromJsonAsync<NpmPackage>(cancellationToken).ConfigureAwait(false);
        if (package is null)
            yield break;

        foreach (var version in package.Versions)
        {
            DateTime? publishedDate = package.Time is not null && package.Time.TryGetValue(version.Key, out var date)
                ? date
                : null;
            yield return new PackageVersion(version.Key, publishedDate);
        }
    }

    public override async Task UpdateLockFileAsync(FullPath rootDirectory, IEnumerable<Dependency> updatedDependencies, CancellationToken cancellationToken)
    {
        var lockFiles = new HashSet<FullPath>();
        foreach (var dependency in updatedDependencies)
        {
            if (dependency.Type is not DependencyType.Npm || dependency.VersionLocation is null)
                continue;

            var lockFile = TryFindLockFile(FullPath.FromPath(dependency.VersionLocation.FilePath).Parent, "package-lock.json");
            if (!lockFile.IsEmpty)
            {
                lockFiles.Add(lockFile);
            }
        }

        foreach (var lockFile in lockFiles)
        {
            try
            {
                // npm has to run where the lock file lives. In an npm-workspaces repository the lock file
                // sits at the root, and running in a sub-package would create a second one there.
                var result = await ProcessWrapper.Create(OperatingSystem.IsWindows() ? "npm.cmd" : "npm")
                    .WithWorkingDirectory(lockFile.Parent)
                    .WithArguments("install", "--no-audit")
                    .WithValidation(ProcessValidationMode.None)
                    .ExecuteBufferedAsync(cancellationToken);

                if (!result.ExitCode.IsSuccess)
                {
                    Console.Error.WriteLine($"Unable to update lock file '{lockFile}':\n{result.Output}");
                }
            }
            catch (Win32Exception ex)
            {
                Console.Error.WriteLine($"Unable to run npm to update lock file '{lockFile}': {ex.Message}");
            }
        }
    }

    private static FullPath TryFindLockFile(FullPath currentDirectory, string fileName)
    {
        while (!currentDirectory.IsEmpty)
        {
            var filePath = currentDirectory / fileName;
            if (System.IO.File.Exists(filePath))
                return filePath;

            currentDirectory = currentDirectory.Parent;
        }

        return FullPath.Empty;
    }

    private sealed class NpmPackage
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = null!;

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("dist-tags")]
        public IDictionary<string, string> DistTags { get; set; } = null!;

        [JsonPropertyName("readme")]
        public string? Readme { get; set; }

        [JsonPropertyName("homepage")]
        public string? Homepage { get; set; }

        [JsonPropertyName("versions")]
        public IReadOnlyDictionary<string, NpmPackageVersion> Versions { get; set; } = null!;

        [JsonPropertyName("time")]
        public IReadOnlyDictionary<string, DateTime>? Time { get; set; }

        public override string ToString()
        {
            return Id;
        }
    }

    private sealed class NpmPackageVersion
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = null!;

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("version")]
        public string Version { get; set; } = null!;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        public override string ToString()
        {
            return Id;
        }
    }
}
