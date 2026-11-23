#!/usr/bin/env dotnet
#:sdk Meziantou.NET.Sdk
#:project ../../tools/Meziantou.Framework.TestCorpus/Meziantou.Framework.TestCorpus.csproj
// Materializes the conformance corpora of the interop tests of Meziantou.Framework.Imaging, named by their SHA-256:
// - tests/Meziantou.Framework.Imaging.External/jxl-conformance/objects: the files of the JPEG XL conformance corpus pinned
//   in tests/Meziantou.Framework.Imaging.InteropTests/JpegXlConformance/corpus.json (the codestreams and the descriptions
//   of the commit of libjxl/conformance, the reference images and profiles of the bucket of the project)
// - tests/Meziantou.Framework.Imaging.External/jpegls-conformance: the JPEG-LS conformance streams and images of CharLS
//   pinned in tests/Meziantou.Framework.Imaging.InteropTests/JpegLsConformance/corpus.json
// The interop tests read a file from these folders when they have it, and download it else (JpegXlConformanceCorpus,
// JpegLsInteropTests): this script fetches them all from their snapshots in the mirror repository of
// eng/imaging/corpus.mirror.json, so the tests do not depend on the upstream servers
// (tools/Meziantou.Framework.TestCorpus/readme.md). When the mirror cannot be used, it warns and the tests download the files.
//
// Usage: dotnet run eng/imaging/fetch-tests.cs [-- [--publish] [--mirror <owner/name|url|path>]]
//   --publish  downloads the corpora that the mirror has no snapshot of from upstream, pushes their snapshots, and writes eng/imaging/corpus.mirror.json
//   --mirror   uses another mirror repository (a fork, or a local repository to test the mirror)
using System.Security.Cryptography;
using System.Text.Json;
using Meziantou.Framework.TestCorpus;

var root = FindRoot(Environment.CurrentDirectory) ?? FindRoot(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Cannot find the root of the repository (Meziantou.Framework.slnx)");
var testsDirectory = Path.Combine(root, "tests", "Meziantou.Framework.Imaging.InteropTests");
var externalDirectory = Path.Combine(root, "tests", "Meziantou.Framework.Imaging.External");

var publish = false;
string? mirrorOverride = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--publish":
            publish = true;
            break;

        case "--mirror" when i + 1 < args.Length:
            mirrorOverride = args[++i];
            break;

        default:
            Console.Error.WriteLine($"Unknown argument '{args[i]}'. Usage: dotnet run eng/imaging/fetch-tests.cs [-- [--publish] [--mirror <owner/name|url|path>]]");
            return 1;
    }
}

var mirror = CorpusMirror.Load(Path.Combine(root, "eng", "imaging", "corpus.mirror.json"), "dotnet run eng/imaging/fetch-tests.cs -- --publish", mirrorOverride);
using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
Corpus[] corpora =
[
    new("jxl-conformance", Path.Combine(testsDirectory, "JpegXlConformance", "corpus.json"), Path.Combine(externalDirectory, "jxl-conformance", "objects"), DownloadJpegXlConformance),
    new("jpegls-conformance", Path.Combine(testsDirectory, "JpegLsConformance", "corpus.json"), Path.Combine(externalDirectory, "jpegls-conformance"), DownloadJpegLsConformance),
];
foreach (var corpus in corpora)
{
    using var pins = JsonDocument.Parse(File.ReadAllText(corpus.PinsPath));
    var repository = pins.RootElement.GetProperty("repository").GetString()!;
    var commit = pins.RootElement.GetProperty("commit").GetString()!;
    string[] pin = [$"{repository}@{commit}", CorpusMirror.HashFile(corpus.PinsPath)];
    if (publish && !mirror.HasSnapshot(corpus.Name, pin))
    {
        Directory.CreateDirectory(corpus.Directory);
        var files = await corpus.Download(httpClient, pins.RootElement, corpus.Directory);
        FileStore.Publish(corpus.Directory, mirror, corpus.Name, pin, files, $"Files of the conformance corpus of {repository} at {commit}");
    }
    else if (!FileStore.TryFetch(corpus.Directory, mirror, corpus.Name, pin))
    {
        Log.Warning($"The tests download the files of {corpus.Name} from upstream");
    }
}

if (publish)
{
    mirror.Retain(corpora.Select(corpus => corpus.Name));
    mirror.Save();
}

return 0;

static string? FindRoot(string directory)
{
    for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
    {
        if (File.Exists(Path.Combine(current.FullName, "Meziantou.Framework.slnx")))
            return current.FullName;
    }

    return null;
}

// The files of the commit (the codestreams, their descriptions and the few files the repository holds), then the files
// that the descriptions list (the reference images and profiles of the bucket)
static async Task<IReadOnlyCollection<string>> DownloadJpegXlConformance(HttpClient httpClient, JsonElement pins, string directory)
{
    var fileUrl = pins.GetProperty("files").GetString()!.Replace("{commit}", pins.GetProperty("commit").GetString()!, StringComparison.Ordinal);
    var objectUrl = pins.GetProperty("objects").GetString()!;
    var files = new Dictionary<string, string>(StringComparer.Ordinal);
    var descriptions = new List<string>();
    foreach (var testCase in pins.GetProperty("cases").EnumerateObject())
    {
        files.TryAdd(testCase.Value.GetProperty("inputSha256").GetString()!, fileUrl.Replace("{path}", testCase.Value.GetProperty("input").GetString()!, StringComparison.Ordinal));
        var testSha256 = testCase.Value.GetProperty("testSha256").GetString()!;
        files.TryAdd(testSha256, fileUrl.Replace("{path}", $"testcases/{testCase.Name}/test.json", StringComparison.Ordinal));
        descriptions.Add(testSha256);
        if (testCase.Value.TryGetProperty("files", out var caseFiles))
        {
            foreach (var file in caseFiles.EnumerateObject())
            {
                files.TryAdd(file.Value.GetString()!, fileUrl.Replace("{path}", $"testcases/{testCase.Name}/{file.Name}", StringComparison.Ordinal));
            }
        }
    }

    await DownloadAll(httpClient, directory, files);
    var objects = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var description in descriptions)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, description)));
        foreach (var property in document.RootElement.GetProperty("sha256sums").EnumerateObject())
        {
            var sha256 = property.Value.GetString()!;
            if (!files.ContainsKey(sha256))
            {
                objects.TryAdd(sha256, objectUrl.Replace("{sha256}", sha256, StringComparison.Ordinal));
            }
        }
    }

    await DownloadAll(httpClient, directory, objects);
    return [.. files.Keys, .. objects.Keys];
}

static async Task<IReadOnlyCollection<string>> DownloadJpegLsConformance(HttpClient httpClient, JsonElement pins, string directory)
{
    var fileUrl = pins.GetProperty("files").GetString()!.Replace("{commit}", pins.GetProperty("commit").GetString()!, StringComparison.Ordinal);
    var files = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var path in pins.GetProperty("paths").EnumerateObject())
    {
        files.TryAdd(path.Value.GetString()!, fileUrl.Replace("{path}", path.Name, StringComparison.Ordinal));
    }

    await DownloadAll(httpClient, directory, files);
    return files.Keys;
}

// Downloads the files that the directory does not have, and checks them against their SHA-256
static async Task DownloadAll(HttpClient httpClient, string directory, Dictionary<string, string> files)
{
    var missing = files.Where(file => !File.Exists(Path.Combine(directory, file.Key)) || Hash(Path.Combine(directory, file.Key)) != file.Key).ToList();
    Console.WriteLine($"Downloading {missing.Count} of {files.Count} files");
    await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (file, cancellationToken) =>
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var content = await httpClient.GetByteArrayAsync(file.Value, cancellationToken);
                var actual = Convert.ToHexStringLower(SHA256.HashData(content));
                if (actual != file.Key)
                    throw new InvalidDataException($"'{file.Value}' has the SHA-256 {actual} instead of the pinned {file.Key}");

                await File.WriteAllBytesAsync(Path.Combine(directory, file.Key), content, cancellationToken);
                return;
            }
            catch (HttpRequestException) when (attempt < 4)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 << attempt), cancellationToken);
            }
        }
    });
}

static string Hash(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexStringLower(SHA256.HashData(stream));
}

// A conformance corpus: its name in the mirror, its pin file, the directory of its files, and the download of its files
internal sealed record Corpus(string Name, string PinsPath, string Directory, Func<HttpClient, JsonElement, string, Task<IReadOnlyCollection<string>>> Download);
