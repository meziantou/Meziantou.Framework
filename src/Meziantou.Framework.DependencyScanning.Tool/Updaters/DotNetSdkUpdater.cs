using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal sealed class DotNetSdkUpdater : PackageUpdater
{
    private static readonly HttpClient HttpClient = new();
    public override VersioningStrategy VersioningStrategy { get; set; } = SemanticVersioningStrategy.Strict;

    protected override async IAsyncEnumerable<PackageVersion> GetVersionsAsync(Dependency dependency, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Only get latest SDK
        var index = await HttpClient.GetFromJsonAsync<DotNetReleaseIndex>("https://raw.githubusercontent.com/dotnet/core/main/release-notes/releases-index.json", cancellationToken).ConfigureAwait(false);
        if (index?.Releases is null)
        {
            yield break;
        }

        foreach (var release in index.Releases)
        {
            if (!string.IsNullOrEmpty(release.LatestSdk))
            {
                // latest-release-date is the publication date of latest-sdk for the channel
                yield return new PackageVersion(release.LatestSdk, release.LatestReleaseDate);
            }
        }
    }

    public override Task UpdateLockFileAsync(FullPath rootDirectory, IEnumerable<Dependency> updatedDependencies, CancellationToken cancellationToken) => Task.CompletedTask;

    protected override bool IsSupported(Dependency dependency) => dependency.Type is DependencyType.DotNetSdk;

    private sealed class DotNetReleaseIndex
    {
        [JsonPropertyName("releases-index")]
        public IReadOnlyCollection<DotNetReleaseEntry>? Releases { get; set; }
    }

    private sealed class DotNetReleaseEntry
    {
        [JsonPropertyName("channel-version")]
        public string? ChannelVersion { get; set; }

        [JsonPropertyName("latest-sdk")]
        public string? LatestSdk { get; set; }

        [JsonPropertyName("latest-release-date")]
        public DateTime? LatestReleaseDate { get; set; }
    }
}
