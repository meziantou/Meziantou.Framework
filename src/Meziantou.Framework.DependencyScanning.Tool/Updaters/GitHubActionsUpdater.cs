using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Meziantou.Framework;
using Meziantou.Framework.DependencyScanning;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal sealed class GitHubActionsUpdater : PackageUpdater
{
    private static readonly HttpClient HttpClient = new();
    public override VersioningStrategy VersioningStrategy { get; set; } = GitHubActionsVersioningStrategy.Instance;

    protected override bool IsSupported(Dependency dependency) => dependency.Type is DependencyType.GitHubActions;

    public override async IAsyncEnumerable<string> GetVersionsAsync(string packageName, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageName))
            yield break;

        if (!TryParseRepository(packageName, out var owner, out var repository))
            yield break;

        var tagsUri = new Uri($"https://api.github.com/repos/{owner}/{repository}/tags?per_page=100");
        using var request = new HttpRequestMessage(HttpMethod.Get, tagsUri);
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd("Meziantou.Framework.DependencyScanning.Tool");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            yield break;

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind is not JsonValueKind.Array)
            yield break;

        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("name", out var nameElement))
            {
                var name = nameElement.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    yield return name;
                }
            }
        }
    }

    public override Task UpdateLockFileAsync(FullPath rootDirectory, IEnumerable<Dependency> updatedDependencies, CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool TryParseRepository(string value, out string owner, out string repository)
    {
        owner = string.Empty;
        repository = string.Empty;

        var separatorIndex = value.IndexOf('/', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex >= value.Length - 1)
            return false;

        owner = value[..separatorIndex];
        var remaining = value[(separatorIndex + 1)..];
        var secondSeparatorIndex = remaining.IndexOf('/', StringComparison.Ordinal);
        if (secondSeparatorIndex < 0)
        {
            repository = remaining;
        }
        else
        {
            repository = remaining[..secondSeparatorIndex];
        }

        return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repository);
    }
}
