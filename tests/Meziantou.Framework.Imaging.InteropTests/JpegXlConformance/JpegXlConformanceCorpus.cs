using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Meziantou.Framework.Imaging.TestHarness;

namespace Meziantou.Framework.Imaging.InteropTests.JpegXlConformance;

/// <summary>
/// The conformance corpus of the JPEG XL project (<c>libjxl/conformance</c>) at the commit pinned in <c>corpus.json</c>:
/// the test codestreams and their descriptions come from that commit, the reference images and profiles from the bucket
/// of the project, by the SHA-256 the descriptions list (or from the commit, for the few files the repository holds:
/// <c>files</c> in <c>corpus.json</c>). Nothing is committed: each file is downloaded the first time a
/// test needs it, checked against its pinned hash, and kept in <c>tests/Meziantou.Framework.Imaging.External/jxl-conformance</c>
/// (the CI caches that folder). A download failure or a hash mismatch fails the test.
/// </summary>
internal static class JpegXlConformanceCorpus
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(5) };
    private static readonly ConcurrentDictionary<string, Lazy<Task<FullPath>>> Downloads = new(StringComparer.Ordinal);
    private static readonly Pins CorpusPins = ReadPins();

    /// <summary>Gets the folder where the downloaded files are kept, named by their SHA-256.</summary>
    public static FullPath ObjectsDirectory => RepositoryPaths.Root / "tests" / "Meziantou.Framework.Imaging.External" / "jxl-conformance" / "objects";

    /// <summary>Gets the names of the test cases of the corpus.</summary>
    public static IReadOnlyCollection<string> CaseNames => CorpusPins.Cases.Keys;

    /// <summary>Downloads the codestream and the description of a test case.</summary>
    /// <param name="name">The name of the test case (its folder in <c>testcases</c>).</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The test case.</returns>
    public static async Task<JpegXlConformanceCase> GetAsync(string name, CancellationToken cancellationToken)
    {
        var pin = CorpusPins.Cases[name];
        var input = await GetFileAsync(pin.Input, pin.InputSha256, cancellationToken).ConfigureAwait(false);
        var description = await GetFileAsync($"testcases/{name}/test.json", pin.TestSha256, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(description, cancellationToken).ConfigureAwait(false));
        var frames = document.RootElement.GetProperty("frames").EnumerateArray()
            .Select(frame => new JpegXlConformanceTolerance(frame.GetProperty("peak_error").GetDouble(), frame.GetProperty("rms_error").GetDouble()))
            .ToArray();
        var files = new Dictionary<string, Func<CancellationToken, Task<FullPath>>>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.GetProperty("sha256sums").EnumerateObject())
        {
            var sha256 = property.Value.GetString()!;
            files.Add(property.Name, token => GetObjectAsync(sha256, token));
        }

        foreach (var (fileName, sha256) in pin.Files)
        {
            files.Add(fileName, token => GetFileAsync($"testcases/{name}/{fileName}", sha256, token));
        }

        return new JpegXlConformanceCase(name, input, frames, files);
    }

    private static Task<FullPath> GetObjectAsync(string sha256, CancellationToken cancellationToken)
        => GetAsync(sha256, CorpusPins.ObjectUrl.Replace("{sha256}", sha256, StringComparison.Ordinal), cancellationToken);

    private static Task<FullPath> GetFileAsync(string path, string sha256, CancellationToken cancellationToken)
        => GetAsync(sha256, CorpusPins.FileUrl.Replace("{commit}", CorpusPins.Commit, StringComparison.Ordinal).Replace("{path}", path, StringComparison.Ordinal), cancellationToken);

    private static Task<FullPath> GetAsync(string sha256, string url, CancellationToken cancellationToken)
    {
        // One download per file and per process; the cancellation of the first caller does not cancel the others
        var lazy = Downloads.GetOrAdd(sha256, _ => new Lazy<Task<FullPath>>(() => DownloadAsync(sha256, url)));
        return lazy.Value.WaitAsync(cancellationToken);
    }

    private static async Task<FullPath> DownloadAsync(string sha256, string url)
    {
        var path = ObjectsDirectory / sha256;
        if (File.Exists(path) && await HashAsync(path).ConfigureAwait(false) == sha256)
            return path;

        Directory.CreateDirectory(ObjectsDirectory);
        const int MaxAttempts = 3;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            // Another test process (the other target framework) may download the same file: each writes its own
            // temporary file and moves it into place once its hash is checked
            var temporary = ObjectsDirectory / $"{sha256}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    await using (stream.ConfigureAwait(false))
                    {
                        var file = File.Create(temporary);
                        await using (file.ConfigureAwait(false))
                        {
                            await stream.CopyToAsync(file).ConfigureAwait(false);
                        }
                    }
                }

                var actual = await HashAsync(temporary).ConfigureAwait(false);
                if (actual != sha256)
                    throw new InvalidDataException($"'{url}' has the SHA-256 {actual} instead of the pinned {sha256}.");

                File.Move(temporary, path, overwrite: true);
                return path;
            }
            catch (Exception ex) when (attempt < MaxAttempts && ex is HttpRequestException or IOException or TaskCanceledException)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 << attempt)).ConfigureAwait(false);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        // The last attempt rethrows its exception
        throw new UnreachableException();
    }

    private static async Task<string> HashAsync(FullPath path)
    {
        var stream = File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream).ConfigureAwait(false));
        }
    }

    private static Pins ReadPins()
    {
        var path = RepositoryPaths.Root / "tests" / "Meziantou.Framework.Imaging.InteropTests" / "JpegXlConformance" / "corpus.json";
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        var cases = root.GetProperty("cases").EnumerateObject().ToDictionary(
            property => property.Name,
            property => new CasePin(
                property.Value.GetProperty("input").GetString()!,
                property.Value.GetProperty("inputSha256").GetString()!,
                property.Value.GetProperty("testSha256").GetString()!,
                property.Value.TryGetProperty("files", out var files) ? files.EnumerateObject().ToDictionary(file => file.Name, file => file.Value.GetString()!, StringComparer.Ordinal) : []),
            StringComparer.Ordinal);
        return new Pins(root.GetProperty("commit").GetString()!, root.GetProperty("files").GetString()!, root.GetProperty("objects").GetString()!, cases);
    }

    private sealed record Pins(string Commit, string FileUrl, string ObjectUrl, Dictionary<string, CasePin> Cases);

    private sealed record CasePin(string Input, string InputSha256, string TestSha256, Dictionary<string, string> Files);
}
