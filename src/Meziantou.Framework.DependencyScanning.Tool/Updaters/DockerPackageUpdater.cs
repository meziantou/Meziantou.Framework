using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meziantou.Framework;
using Meziantou.Framework.DependencyScanning;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal sealed class DockerPackageUpdater : PackageUpdater
{
    private static readonly HttpClient HttpClient = new();
    public override VersioningStrategy VersioningStrategy { get; set; } = DockerVersioningStrategy.Instance;

    protected override bool IsSupported(Dependency dependency) => dependency.Type is DependencyType.DockerImage;

    public override async IAsyncEnumerable<string> GetVersionsAsync(string packageName, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageName))
            yield break;

        packageName = NormalizePackageName(packageName);
        var (registry, repository) = ParseImageName(packageName);

        var tagsListUri = new UriBuilder(Uri.UriSchemeHttps, registry) { Path = $"v2/{repository}/tags/list" }.Uri;
        using var initialResponse = await SendTagsListRequestAsync(tagsListUri, accessToken: null, cancellationToken).ConfigureAwait(false);
        if (initialResponse.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            yield break;

        if (initialResponse.StatusCode is HttpStatusCode.Unauthorized)
        {
            var accessToken = await GetRegistryTokenAsync(initialResponse, repository, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(accessToken))
                yield break;

            using var authenticatedResponse = await SendTagsListRequestAsync(tagsListUri, accessToken, cancellationToken).ConfigureAwait(false);
            if (authenticatedResponse.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
                yield break;

            authenticatedResponse.EnsureSuccessStatusCode();
            await foreach (var tag in ParseTagsAsync(authenticatedResponse, cancellationToken).ConfigureAwait(false))
            {
                yield return tag;
            }

            yield break;
        }

        initialResponse.EnsureSuccessStatusCode();
        await foreach (var tag in ParseTagsAsync(initialResponse, cancellationToken).ConfigureAwait(false))
        {
            yield return tag;
        }
    }

    public override Task UpdateLockFileAsync(FullPath rootDirectory, IEnumerable<Dependency> updatedDependencies, CancellationToken cancellationToken) => Task.CompletedTask;

    private static string NormalizePackageName(string packageName)
    {
        const string DockerPrefix = "docker://";
        if (packageName.StartsWith(DockerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            packageName = packageName[DockerPrefix.Length..];
        }

        var atIndex = packageName.LastIndexOf('@');
        if (atIndex > 0)
            return packageName[..atIndex];

        return packageName;
    }

    private static (string Registry, string Repository) ParseImageName(string packageName)
    {
        var slashIndex = packageName.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex < 0)
            return ("registry-1.docker.io", $"library/{packageName}");

        var firstSegment = packageName[..slashIndex];
        var hasExplicitRegistry = firstSegment.Contains('.', StringComparison.Ordinal) || firstSegment.Contains(':', StringComparison.Ordinal) || firstSegment.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        if (hasExplicitRegistry)
            return (firstSegment, packageName[(slashIndex + 1)..]);

        return ("registry-1.docker.io", packageName);
    }

    private static async Task<HttpResponseMessage> SendTagsListRequestAsync(Uri tagsListUri, string? accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, tagsListUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Docker-Distribution-Api-Version", "registry/2.0");
        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<string> ParseTagsAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (document.RootElement.TryGetProperty("tags", out var tagsElement) && tagsElement.ValueKind is JsonValueKind.Array)
        {
            foreach (var tagElement in tagsElement.EnumerateArray())
            {
                var tag = tagElement.GetString();
                if (!string.IsNullOrEmpty(tag))
                {
                    yield return tag;
                }
            }
        }
    }

    private static async Task<string?> GetRegistryTokenAsync(HttpResponseMessage unauthorizedResponse, string repository, CancellationToken cancellationToken)
    {
        var challenge = unauthorizedResponse.Headers.WwwAuthenticate.FirstOrDefault(static item => item.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase));
        if (challenge is null)
            return null;

        var challengeParameters = ParseChallengeParameters(challenge.Parameter);
        if (!challengeParameters.TryGetValue("realm", out var realm))
            return null;

        challengeParameters.TryGetValue("service", out var service);
        if (!challengeParameters.TryGetValue("scope", out var scope))
        {
            scope = $"repository:{repository}:pull";
        }

        var tokenUri = BuildTokenUri(realm, service, scope);
        using var tokenResponse = await HttpClient.GetAsync(tokenUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        tokenResponse.EnsureSuccessStatusCode();

        await using var tokenStream = await tokenResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var tokenResult = await JsonSerializer.DeserializeAsync<DockerRegistryToken>(tokenStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return tokenResult?.Token ?? tokenResult?.AccessToken;
    }

    private static Dictionary<string, string> ParseChallengeParameters(string? value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
            return result;

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = part.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0 || separatorIndex >= part.Length - 1)
                continue;

            var key = part[..separatorIndex].Trim();
            var currentValue = part[(separatorIndex + 1)..].Trim();
            if (currentValue.Length >= 2 && currentValue[0] == '"' && currentValue[^1] == '"')
            {
                currentValue = currentValue[1..^1];
            }

            result[key] = currentValue;
        }

        return result;
    }

    private static Uri BuildTokenUri(string realm, string? service, string? scope)
    {
        var queryParameters = new List<string>(2);
        if (!string.IsNullOrEmpty(service))
        {
            queryParameters.Add($"service={Uri.EscapeDataString(service)}");
        }

        if (!string.IsNullOrEmpty(scope))
        {
            queryParameters.Add($"scope={Uri.EscapeDataString(scope)}");
        }

        if (queryParameters.Count is 0)
            return new Uri(realm, UriKind.Absolute);

        var separator = realm.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return new Uri($"{realm}{separator}{string.Join('&', queryParameters)}", UriKind.Absolute);
    }

    private sealed class DockerRegistryToken
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }

        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }
}
