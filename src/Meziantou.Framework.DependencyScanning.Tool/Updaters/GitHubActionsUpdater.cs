using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal sealed class GitHubActionsUpdater : PackageUpdater
{
    private static readonly HttpClient HttpClient = new();
    public override VersioningStrategy VersioningStrategy { get; set; } = GitHubActionsVersioningStrategy.Instance;

    protected override bool IsSupported(Dependency dependency) => dependency.Type is DependencyType.GitHubActions;

    protected override async IAsyncEnumerable<PackageVersion> GetVersionsAsync(Dependency dependency, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (dependency.Name is null)
            yield break;

        if (!TryParseRepository(dependency.Name, out var owner, out var repository))
            yield break;

        var tagsUri = new Uri($"https://api.github.com/repos/{owner}/{repository}/tags?per_page=100");
        using var request = new HttpRequestMessage(HttpMethod.Get, tagsUri);
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd("Meziantou.Framework.DependencyScanning.Tool");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        var tagsWithDates = await GetTagsWithDatesAsync(request, cancellationToken).ConfigureAwait(false);
        if (tagsWithDates is null)
            yield break;

        foreach (var (tag, date) in tagsWithDates)
        {
            yield return new PackageVersion(tag, date);
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

    private static async Task<(string Tag, DateTime? PublishedDate)[]?> GetTagsWithDatesAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                return null;

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (document.RootElement.ValueKind is not JsonValueKind.Array)
                return null;

            var result = new List<(string, DateTime?)>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("name", out var nameElement))
                    continue;

                var name = nameElement.GetString();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                DateTime? publishedDate = null;
                // Try to get published_at from release data (if available)
                if (item.TryGetProperty("commit", out var commitElement))
                {
                    if (commitElement.TryGetProperty("committer", out var committerElement))
                    {
                        if (committerElement.TryGetProperty("date", out var dateElement))
                        {
                            if (DateTime.TryParse(dateElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
                            {
                                publishedDate = date;
                            }
                        }
                    }
                }

                result.Add((name, publishedDate));
            }

            return [.. result];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
