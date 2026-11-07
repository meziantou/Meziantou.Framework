using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.Geo.Maps.Tool;

/// <summary>
/// Downloads the sources at the versions of the pins: the pins embedded in the tool, or a file of pins. Each file is kept
/// in the cache folder, and its SHA-256 is checked on every use. To update the pins, the files are downloaded again and
/// their new hashes are pinned.
/// </summary>
internal sealed class Downloader
{
    public const string EmbeddedPinsName = "data-versions.json";

    private readonly string _pinsSource;
    private readonly FullPath _cacheDirectory;
    private readonly bool _update;
    private readonly TextWriter _log;
    private readonly SortedDictionary<string, string> _pins;

    private Downloader(string pinsSource, FullPath cacheDirectory, bool update, TextWriter log, string? naturalEarthCommit, string? cldrVersion, DateTimeOffset? date, SortedDictionary<string, string> pins)
    {
        _pinsSource = pinsSource;
        _cacheDirectory = cacheDirectory;
        _update = update;
        _log = log;
        NaturalEarthCommit = naturalEarthCommit;
        CldrVersion = cldrVersion;
        Date = date;
        _pins = pins;
    }

    /// <summary>Gets or sets the commit of the Natural Earth repository the files are read from.</summary>
    public string? NaturalEarthCommit { get; set; }

    /// <summary>Gets or sets the release of the JSON data of Unicode CLDR the regions are read from, such as <c>49.0.0</c>.</summary>
    public string? CldrVersion { get; set; }

    /// <summary>Gets the date the versions of the sources were pinned: the date of the data of the maps, so that the same pins give the same files.</summary>
    public DateTimeOffset? Date { get; private set; }

    /// <summary>Gets the pins embedded in the tool.</summary>
    public static string GetEmbeddedPins()
    {
        using var stream = typeof(Downloader).Assembly.GetManifestResourceStream(EmbeddedPinsName) ?? throw new InvalidOperationException("The pins are not embedded in the tool.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Creates a downloader from the JSON of pins.</summary>
    /// <param name="pins">The JSON of the pins, or <see langword="null"/> for none.</param>
    /// <param name="pinsSource">Where the pins come from, for the messages.</param>
    /// <param name="cacheDirectory">The folder of the downloaded files.</param>
    /// <param name="update">Whether the files are downloaded again and pinned: the pinned files and their hashes are ignored, but not the date.</param>
    /// <param name="log">Where the downloads are reported.</param>
    public static Downloader Create(string? pins, string pinsSource, FullPath cacheDirectory, bool update, TextWriter log)
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        string? commit = null;
        string? cldrVersion = null;
        DateTimeOffset? date = null;
        if (pins is not null)
        {
            JsonObject root;
            try
            {
                root = JsonNode.Parse(pins)?.AsObject() ?? throw new FormatException($"The pins of {pinsSource} are not a JSON object.");
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                throw new FormatException($"The pins of {pinsSource} are not valid JSON.", exception);
            }

            commit = (string?)root["naturalEarthCommit"];
            cldrVersion = (string?)root["cldrVersion"];
            if ((string?)root["date"] is { } text)
            {
                date = DateTimeOffset.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            }

            // To update, every file is pinned again: the files that are no longer used disappear
            if (!update && root["files"] is JsonObject pinnedFiles)
            {
                foreach (var (url, sha256) in pinnedFiles)
                {
                    files[url] = (string)sha256!;
                }
            }
        }

        return new Downloader(pinsSource, cacheDirectory, update, log, commit, cldrVersion, date, files);
    }

    /// <summary>Gets the content of a pinned file, from the cache when it is there.</summary>
    /// <param name="url">The URL of the file.</param>
    /// <param name="cancellationToken">The token to cancel the download.</param>
    public async Task<byte[]> GetAsync(string url, CancellationToken cancellationToken = default)
    {
        if (_update)
        {
            var content = await DownloadAsync(url, cancellationToken).ConfigureAwait(false);
            var sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
            await WriteCacheAsync(url, sha256, content, cancellationToken).ConfigureAwait(false);
            lock (_pins)
            {
                _pins[url] = sha256;
            }

            return content;
        }

        string? expected;
        lock (_pins)
        {
            _pins.TryGetValue(url, out expected);
        }

        if (expected is null)
            throw new InvalidOperationException($"{url} is not pinned in {_pinsSource}: update the pins with the update-pins command.");

        var path = GetCachePath(url, expected);
        if (File.Exists(path))
        {
            var cached = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            if (string.Equals(Convert.ToHexStringLower(SHA256.HashData(cached)), expected, StringComparison.Ordinal))
                return cached;
        }

        var downloaded = await DownloadAsync(url, cancellationToken).ConfigureAwait(false);
        var actual = Convert.ToHexStringLower(SHA256.HashData(downloaded));
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"The content of {url} does not match its pin in {_pinsSource} (SHA-256 {actual}, pinned {expected}).");

        await WriteCacheAsync(url, expected, downloaded, cancellationToken).ConfigureAwait(false);
        return downloaded;
    }

    /// <summary>Writes the pins to a file, and returns whether they changed. When the pins change, their date becomes the current date.</summary>
    public async Task<bool> SavePinsAsync(FullPath path, CancellationToken cancellationToken)
    {
        var current = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false) : null;
        if (string.Equals(current, FormatPins(Date), StringComparison.Ordinal))
            return false;

        Date = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        path.CreateParentDirectory();
        await File.WriteAllTextAsync(path, FormatPins(Date), cancellationToken).ConfigureAwait(false);
        return true;
    }

    private string FormatPins(DateTimeOffset? date)
    {
        var files = new JsonObject();
        foreach (var (url, sha256) in _pins)
        {
            files[url] = sha256;
        }

        var root = new JsonObject
        {
            ["date"] = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["naturalEarthCommit"] = NaturalEarthCommit,
            ["cldrVersion"] = CldrVersion,
            ["files"] = files,
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true }).ReplaceLineEndings("\n") + "\n";
    }

    private async Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        await _log.WriteLineAsync(("Downloading " + url).AsMemory(), cancellationToken).ConfigureAwait(false);
        return await SharedHttpClient.InstanceWithAutoRedirect.GetByteArrayAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteCacheAsync(string url, string sha256, byte[] content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_cacheDirectory);

        // Written under another name and moved, so that a download that is stopped leaves no partial file in the cache
        var path = GetCachePath(url, sha256);
        var temporaryPath = path.Value + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private FullPath GetCachePath(string url, string sha256)
    {
        var name = Path.GetFileName(new Uri(url).AbsolutePath);
        return _cacheDirectory / (sha256 + "-" + (name.Length == 0 ? "index" : name));
    }
}
