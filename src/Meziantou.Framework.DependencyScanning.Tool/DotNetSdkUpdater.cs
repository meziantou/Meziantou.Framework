using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Meziantou.Framework;
using Meziantou.Framework.DependencyScanning;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal sealed class DotNetSdkUpdater : PackageUpdater
{
    private static readonly HttpClient HttpClient = new();

    public override async IAsyncEnumerable<string> GetVersionsAsync(string packageName, [EnumeratorCancellation] CancellationToken cancellationToken)
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
                yield return release.LatestSdk;
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
    }
}
