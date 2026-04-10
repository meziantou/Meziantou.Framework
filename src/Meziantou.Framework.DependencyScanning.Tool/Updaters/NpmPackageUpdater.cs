using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meziantou.Framework;
using Meziantou.Framework.DependencyScanning;

namespace Meziantou.Framework.DependencyScanning.Tool;

internal sealed class NpmPackageUpdater : PackageUpdater
{
    private static readonly HttpClient HttpClient = new();
    private static readonly Uri DefaultRegistryUri = new("https://registry.npmjs.org/");
    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        Converters =
        {
            new NpmPackageRepositoryJsonConverter(),
        },
    };
    public override VersioningStrategy VersioningStrategy { get; set; } = NpmVersioningStrategy.Instance;

    protected override bool IsSupported(Dependency dependency) => dependency.Type is DependencyType.Npm;

    protected override async IAsyncEnumerable<string> GetVersionsAsync(Dependency dependency, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var dependencyLocation = dependency.VersionLocation?.FilePath ?? dependency.NameLocation?.FilePath;
        if (dependencyLocation is null || dependency.Name is null)
            yield break;

        var registry = NpmPackageSourceResolver.ResolveRegistry(FullPath.FromPath(dependencyLocation), dependency.Name);
        await foreach (var version in GetVersionsFromRegistryAsync(registry, dependency.Name, cancellationToken).ConfigureAwait(false))
        {
            yield return version;
        }
    }

    public override async IAsyncEnumerable<string> GetVersionsAsync(string packageName, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var version in GetVersionsFromRegistryAsync(DefaultRegistryUri, packageName, cancellationToken).ConfigureAwait(false))
        {
            yield return version;
        }
    }

    private static async IAsyncEnumerable<string> GetVersionsFromRegistryAsync(Uri registryUri, string packageName, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var packageUri = new Uri(registryUri, packageName);
        using var packageResponse = await HttpClient.GetAsync(packageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (packageResponse.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.BadRequest)
            yield break;

        packageResponse.EnsureSuccessStatusCode();
        var package = await packageResponse.Content.ReadFromJsonAsync<NpmPackage>(options: DefaultJsonOptions, cancellationToken).ConfigureAwait(false);
        if (package is null)
            yield break;

        foreach (var version in package.Versions)
            yield return version.Key;
    }

    public override async Task UpdateLockFileAsync(FullPath rootDirectory, IEnumerable<Dependency> updatedDependencies, CancellationToken cancellationToken)
    {
        var files = updatedDependencies
            .Where(dep => dep.Type is DependencyType.Npm && dep.VersionLocation is not null)
            .Select(dep => FullPath.FromPath(dep.VersionLocation!.FilePath))
            .Distinct()
            .ToArray();

        foreach (var file in files)
        {
            var lockFile = TryFindLockFile(file.Parent, "package-lock.json");
            if (!lockFile.IsEmpty)
            {
                var result = await ProcessWrapper.Create(OperatingSystem.IsWindows() ? @"C:\Program Files\nodejs\npm.cmd" : "npm")
                    .WithWorkingDirectory(file.Parent)
                    .WithArguments("install", "--no-audit", "--force")
                    .WithValidation(ProcessValidationMode.None)
                    .ExecuteBufferedAsync(cancellationToken);

                if (result.ExitCode is not 0)
                {
                    Console.WriteLine($"Unable to update lock file '{lockFile}':\n{result.Output}");
                }
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

        [JsonPropertyName("repository")]
        public NpmPackageRepository[]? Repository { get; set; }

        [JsonPropertyName("versions")]
        public IReadOnlyDictionary<string, NpmPackageVersion> Versions { get; set; } = null!;

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

        [JsonPropertyName("repository")]
        public NpmPackageRepository[]? Repository { get; set; }

        public override string ToString()
        {
            return Id;
        }
    }

    private sealed class NpmPackageRepository
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = null!;

        [JsonPropertyName("url")]
        public string Url { get; set; } = null!;
    }

    private sealed class NpmPackageRepositoryJsonConverter : JsonConverter<NpmPackageRepository[]>
    {
        public override bool HandleNull => false;

        public override NpmPackageRepository[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.StartArray)
            {
                reader.Read();
                var result = new List<NpmPackageRepository>();
                while (reader.TokenType is not JsonTokenType.EndArray)
                {
                    var item = ReadSingleItem(ref reader);
                    if (item is not null)
                    {
                        result.Add(item);
                    }

                    if (reader.TokenType is JsonTokenType.EndObject)
                    {
                        reader.Read();
                    }
                }

                return [.. result];
            }

            var value = ReadSingleItem(ref reader);
            if (value is not null)
                return [value];

            throw new NotSupportedException($"Token {reader.TokenType} is not supported");
        }

        private static NpmPackageRepository? ReadSingleItem(ref Utf8JsonReader reader)
        {
            // Repository can be a string or an object
            if (reader.TokenType is JsonTokenType.StartObject)
            {
                return JsonSerializer.Deserialize<NpmPackageRepository>(ref reader);
            }

            if (reader.TokenType is JsonTokenType.String)
            {
                var str = reader.GetString();
                return new NpmPackageRepository { Url = str! };
            }

            throw new NotSupportedException($"Token {reader.TokenType} is not supported");
        }

        public override void Write(Utf8JsonWriter writer, NpmPackageRepository[]? value, JsonSerializerOptions options)
        {
            throw new NotSupportedException();
        }
    }
}
