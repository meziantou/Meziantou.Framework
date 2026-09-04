using System.Reflection;
using System.Text.Json;
using Meziantou.Framework.HttpArchive;

namespace Meziantou.Framework.Http.Recording;

/// <summary>Stores recorded HTTP entries in HAR (HTTP Archive) 1.2 format.</summary>
public sealed class HarHttpRecordingStore : IHttpRecordingStore
{
    /// <summary>HAR 1.2 uses -1 for "information not available". 0 is a factual claim that tooling will plot.</summary>
    private const long UnknownSize = -1;

    private static readonly UTF8Encoding Utf8EncodingThrowOnInvalid = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonElement Base64EncodingValue = CreateBase64EncodingValue();
    private static readonly string CreatorVersion = GetCreatorVersion();

    private readonly string _filePath;

    public HarHttpRecordingStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        _filePath = filePath;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<HttpRecordingEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        HarDocument doc;
        await using (var stream = File.OpenRead(_filePath))
        {
            try
            {
                doc = await HarDocument.ParseAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"The recording file '{_filePath}' is not a valid HAR document. It may have been truncated by an interrupted save.", ex);
            }
        }

        if (doc.Log?.Entries is not { } harEntries)
        {
            throw new InvalidDataException($"The recording file '{_filePath}' has no 'log.entries' array. Delete it to start a new recording.");
        }

        var entries = new List<HttpRecordingEntry>(harEntries.Count);
        foreach (var harEntry in harEntries)
        {
            entries.Add(ConvertFromHarEntry(harEntry));
        }

        HttpRecordingStoreHelpers.ValidateEntries(entries, _filePath);
        return entries;
    }

    /// <inheritdoc />
    public ValueTask SaveAsync(IReadOnlyList<HttpRecordingEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var harEntries = new List<HarEntry>(entries.Count);
        foreach (var entry in entries)
        {
            harEntries.Add(ConvertToHarEntry(entry));
        }

        var doc = new HarDocument
        {
            Log = new HarLog
            {
                Version = "1.2",
                Creator = new HarCreator { Name = "Meziantou.Framework.Http.Recording", Version = CreatorVersion },
                Entries = harEntries,
            },
        };

        return HttpRecordingStoreHelpers.WriteAtomicallyAsync(
            _filePath,
            async (stream, token) => await doc.WriteToAsync(stream, indented: true, token).ConfigureAwait(false),
            cancellationToken);
    }

    private static HttpRecordingEntry ConvertFromHarEntry(HarEntry harEntry)
    {
        var request = harEntry.Request;
        var response = harEntry.Response;

        var entry = new HttpRecordingEntry
        {
            Method = request?.Method ?? "",
            RequestUri = request?.Url ?? "",
            StatusCode = response?.Status ?? 0,
            ReasonPhrase = string.IsNullOrEmpty(response?.StatusText) ? null : response.StatusText,
            HttpVersion = ParseHttpVersion(response?.HttpVersion),
            RecordedAt = harEntry.StartedDateTime,
            RequestHeaders = ConvertFromHarHeaders(request?.Headers),
            ResponseHeaders = ConvertFromHarHeaders(response?.Headers),
        };

        // Request body
        if (request?.PostData.TryGetRawData(out var requestBody) is true)
        {
            entry.RequestBody = requestBody;
        }

        // Response body. Uses the same helper as the request side: it reports an unrecognized encoding or malformed
        // base64 as "no body" instead of throwing or silently returning the undecoded text as bytes.
        if (response?.Content.TryGetRawData(out var responseBody) is true)
        {
            entry.ResponseBody = responseBody;
        }

        return entry;
    }

    /// <summary>Groups the archived headers by name. Headers without a name are skipped: there is nothing to look them up by.</summary>
    private static Dictionary<string, string[]>? ConvertFromHarHeaders(List<HarHeader>? headers)
    {
        if (headers is null || headers.Count is 0)
            return null;

        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            if (header.Name is not { } name)
                continue;

            var value = header.Value ?? "";
            result[name] = result.TryGetValue(name, out var existing) ? [.. existing, value] : [value];
        }

        return result.Count is 0 ? null : result;
    }

    private static HarEntry ConvertToHarEntry(HttpRecordingEntry entry)
    {
        var request = new HarRequest
        {
            Method = entry.Method,
            Url = entry.RequestUri,
            Headers = ConvertToHarHeaders(entry.RequestHeaders),
            HeadersSize = UnknownSize,
            BodySize = entry.RequestBody?.Length ?? UnknownSize,
        };

        var response = new HarResponse
        {
            Status = entry.StatusCode,
            StatusText = entry.ReasonPhrase ?? "",
            HttpVersion = entry.HttpVersion is { } version ? "HTTP/" + version : "",
            Headers = ConvertToHarHeaders(entry.ResponseHeaders),
            HeadersSize = UnknownSize,
            BodySize = entry.ResponseBody?.Length ?? UnknownSize,
        };

        // Request body
        if (entry.RequestBody is { Length: > 0 })
        {
            var contentType = GetContentType(entry.RequestHeaders);

            var postData = new HarPostData
            {
                MimeType = contentType,
            };

            if (IsTextMediaType(contentType) && TryDecodeUtf8(entry.RequestBody, out var requestText))
            {
                postData.Text = requestText;
            }
            else
            {
                postData.Text = Convert.ToBase64String(entry.RequestBody);
                postData.ExtensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    [HarPostDataExtensions.DefaultEncodingExtensionName] = Base64EncodingValue,
                };
            }

            request.PostData = postData;
        }

        // Response body
        if (entry.ResponseBody is { Length: > 0 })
        {
            var mimeType = GetContentType(entry.ResponseHeaders);

            var content = new HarContent
            {
                Size = entry.ResponseBody.Length,
                MimeType = mimeType,
            };

            if (IsTextMediaType(mimeType) && TryDecodeUtf8(entry.ResponseBody, out var responseText))
            {
                content.Text = responseText;
            }
            else
            {
                content.Encoding = "base64";
                content.Text = Convert.ToBase64String(entry.ResponseBody);
            }

            response.Content = content;
        }

        return new HarEntry
        {
            StartedDateTime = entry.RecordedAt,
            Time = UnknownSize,
            Request = request,
            Response = response,
        };
    }

    private static List<HarHeader> ConvertToHarHeaders(Dictionary<string, string[]>? headers)
    {
        var result = new List<HarHeader>();
        if (headers is not null)
        {
            foreach (var (name, values) in headers)
            {
                foreach (var value in values)
                {
                    result.Add(new HarHeader { Name = name, Value = value });
                }
            }
        }

        return result;
    }

    private static string GetContentType(Dictionary<string, string[]>? headers)
    {
        if (headers is not null && headers.TryGetValue("Content-Type", out var contentTypes) && contentTypes.Length > 0)
        {
            return contentTypes[0];
        }

        return "application/octet-stream";
    }

    private static bool IsTextMediaType(string contentType)
    {
        var separatorIndex = contentType.IndexOf(';', StringComparison.Ordinal);
        var mediaType = separatorIndex >= 0 ? contentType[..separatorIndex].Trim() : contentType.Trim();

        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mediaType, "application/javascript", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mediaType, "application/xml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mediaType, "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) ||
               mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase) ||
               mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryDecodeUtf8(byte[] bytes, out string? text)
    {
        try
        {
            text = Utf8EncodingThrowOnInvalid.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = null;
            return false;
        }
    }

    /// <summary>Converts the HAR <c>httpVersion</c> field (e.g. <c>HTTP/1.1</c>) to the version string used by <see cref="HttpRecordingEntry.HttpVersion"/>.</summary>
    private static string? ParseHttpVersion(string? httpVersion)
    {
        if (string.IsNullOrEmpty(httpVersion))
            return null;

        const string Prefix = "HTTP/";
        var value = httpVersion.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? httpVersion[Prefix.Length..] : httpVersion;
        return Version.TryParse(value, out _) ? value : null;
    }

    private static JsonElement CreateBase64EncodingValue()
    {
        using var document = JsonDocument.Parse("\"base64\"");
        return document.RootElement.Clone();
    }

    private static string GetCreatorVersion()
    {
        var informationalVersion = typeof(HarHttpRecordingStore).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informationalVersion))
        {
            // Strip the source-revision suffix that the SDK appends (1.2.3+abcdef).
            var separatorIndex = informationalVersion.IndexOf('+', StringComparison.Ordinal);
            return separatorIndex >= 0 ? informationalVersion[..separatorIndex] : informationalVersion;
        }

        return typeof(HarHttpRecordingStore).Assembly.GetName().Version?.ToString() ?? "";
    }
}
