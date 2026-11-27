using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Meziantou.Framework.Yaml;
using Meziantou.Framework.Yaml.Nodes;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal sealed class GitHubActionsUpdater : PackageUpdater
{
    // Anonymous calls share a 60 requests/hour budget per IP, which a repository with a few dozen
    // actions exhausts in a single run. A token raises that to 5000.
    private static readonly string[] TokenEnvironmentVariables = ["GITHUB_TOKEN", "GH_TOKEN"];
    // GitHub does not document its tag ordering, so on a repository with more than one page of tags the
    // newest release can sit behind the Link header rather than in the first response.
    private const int MaxPages = 10;

    public override VersioningStrategy VersioningStrategy { get; set; } = GitHubActionsVersioningStrategy.Instance;

    protected override bool IsSupported(Dependency dependency) => dependency.Type is DependencyType.GitHubActions && dependency.Name is not null;

    protected override async IAsyncEnumerable<PackageVersion> GetVersionsAsync(Dependency dependency, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (dependency.Name is null)
            yield break;

        if (!TryParseRepository(dependency.Name, out var owner, out var repository))
            yield break;

        var nextUri = new Uri($"https://api.github.com/repos/{owner}/{repository}/tags?per_page=100");
        for (var page = 0; page < MaxPages && nextUri is not null; page++)
        {
            var result = await GetTagsWithDatesAsync(nextUri, cancellationToken).ConfigureAwait(false);
            if (result is null)
                yield break;

            foreach (var (tag, date) in result.Value.Tags)
            {
                yield return new PackageVersion(tag, date);
            }

            nextUri = result.Value.NextPage;
        }
    }

    private static HttpRequestMessage CreateTagsRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd("Meziantou.Framework.DependencyScanning.Tool");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        if (GetToken() is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    public override async Task UpdateLockFileAsync(FullPath rootDirectory, IEnumerable<Dependency> updatedDependencies, CancellationToken cancellationToken)
    {
        foreach (var update in GetLockFileUpdates(updatedDependencies))
        {
            try
            {
                // Only the workflows already in the lock file are passed: a full-directory run would also lock the
                // workflows the repository has not onboarded, which the runner would then enforce.
                // --no-narrow keeps the refs as the update wrote them, instead of rewriting "v5" to a full tag.
                var result = await ProcessWrapper.Create("gh")
                    .WithWorkingDirectory(update.RepositoryRoot)
                    .WithArguments(["actions-lock", "--no-narrow", .. update.Workflows])
                    .WithValidation(ProcessValidationMode.None)
                    .ExecuteBufferedAsync(cancellationToken);

                if (!result.ExitCode.IsSuccess)
                {
                    Console.Error.WriteLine($"Unable to update lock file '{update.LockFile}':\n{result.Output}");
                }
            }
            catch (Win32Exception ex)
            {
                Console.Error.WriteLine($"Unable to run 'gh actions-lock' to update lock file '{update.LockFile}': {ex.Message}");
            }
        }
    }

    /// <summary>Finds the <c>.github/workflows/actions.lock</c> files of the repositories whose workflows or actions were updated.</summary>
    internal static List<ActionsLockFileUpdate> GetLockFileUpdates(IEnumerable<Dependency> updatedDependencies)
    {
        var lockFiles = new HashSet<FullPath>();
        foreach (var dependency in updatedDependencies)
        {
            if (dependency.Type is not DependencyType.GitHubActions || dependency.VersionLocation is null)
                continue;

            // A workflow is in the .github/workflows folder of the repository. A composite action can be anywhere in it.
            var lockFile = TryFindLockFile(FullPath.FromPath(dependency.VersionLocation.FilePath).Parent);
            if (!lockFile.IsEmpty)
            {
                lockFiles.Add(lockFile);
            }
        }

        var result = new List<ActionsLockFileUpdate>();
        foreach (var lockFile in lockFiles.OrderBy(path => path.Value, StringComparer.Ordinal))
        {
            var workflows = GetLockedWorkflows(lockFile);
            if (workflows.Length > 0)
            {
                result.Add(new ActionsLockFileUpdate(lockFile, lockFile.Parent.Parent.Parent, workflows));
            }
        }

        return result;
    }

    private static FullPath TryFindLockFile(FullPath currentDirectory)
    {
        while (!currentDirectory.IsEmpty)
        {
            var filePath = currentDirectory / ".github" / "workflows" / "actions.lock";
            if (System.IO.File.Exists(filePath))
                return filePath;

            currentDirectory = currentDirectory.Parent;
        }

        return FullPath.Empty;
    }

    /// <summary>Reads the keys of the <c>workflows</c> section, which are the paths of the workflows the repository has onboarded.</summary>
    private static string[] GetLockedWorkflows(FullPath lockFile)
    {
        YamlStream stream;
        try
        {
            using var reader = System.IO.File.OpenText(lockFile);
            stream = YamlStream.Load(reader);
        }
        catch (YamlException)
        {
            return [];
        }

        var workflows = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var document in stream)
        {
            if (document.Contents is not YamlMapping root || root["workflows"] is not YamlMapping workflowsSection)
                continue;

            foreach (var workflow in workflowsSection)
            {
                // The paths are passed to gh as arguments, so a value that is not a workflow path, such as an option, is ignored
                if (workflow.Key is YamlValue { Value: { } path } && IsWorkflowPath(path))
                {
                    workflows.Add(path);
                }
            }
        }

        return [.. workflows];
    }

    private static bool IsWorkflowPath(string path)
    {
        const string Prefix = ".github/workflows/";
        if (!path.StartsWith(Prefix, StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal))
            return false;

        return path.EndsWith(".yml", StringComparison.Ordinal) || path.EndsWith(".yaml", StringComparison.Ordinal);
    }

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

    private static string? GetToken()
    {
        foreach (var name in TokenEnvironmentVariables)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
                return value;
        }

        return null;
    }

    private static async Task<((string Tag, DateTime? PublishedDate)[] Tags, Uri? NextPage)?> GetTagsWithDatesAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using var request = CreateTagsRequest(uri);
            using var response = await SharedHttpClient.InstanceWithAutoRedirect.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                // Without this the caller cannot tell "rate limited" from "no newer tag exists"
                Console.Error.WriteLine($"GitHub API returned {(int)response.StatusCode} for '{request.RequestUri}'; GitHub Actions versions were not checked. Set GITHUB_TOKEN to raise the rate limit.");
                return null;
            }

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
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

            return ([.. result], LinkHeader.TryGetNextPageUri(response, uri));
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
