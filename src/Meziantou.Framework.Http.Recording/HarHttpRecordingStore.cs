using Meziantou.Framework.HttpArchive;

namespace Meziantou.Framework.Http.Recording;

/// <summary>Stores recorded HTTP entries in HAR (HTTP Archive) 1.2 format.</summary>
public sealed class HarHttpRecordingStore : IHttpRecordingStore
{
    private static readonly System.Text.UTF8Encoding Utf8EncodingThrowOnInvalid = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
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

        await using var stream = File.OpenRead(_filePath);
        var doc = await HarDocument.ParseAsync(stream, cancellationToken).ConfigureAwait(false);
        if (doc.Log?.Entries is not { } harEntries)
        {
            return [];
        }

        var entries = new List<HttpRecordingEntry>(harEntries.Count);
        foreach (var harEntry in harEntries)
        {
            entries.Add(ConvertFromHarEntry(harEntry));
        }

        return entries;
    }

    /// <inheritdoc />
    public async ValueTask SaveAsync(IReadOnlyList<HttpRecordingEntry> entries, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

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
                Creator = new HarCreator { Name = "Meziantou.Framework.Http.Recording", Version = "1.0.0" },
                Entries = harEntries,
            },
        };

        await using var stream = File.Create(_filePath);
        await doc.WriteToAsync(stream, indented: true, cancellationToken).ConfigureAwait(false);
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
            RecordedAt = harEntry.StartedDateTime,
            RequestHeaders = ConvertFromHarHeaders(request?.Headers),
            ResponseHeaders = ConvertFromHarHeaders(response?.Headers),
        };

        // Request body
        if (request?.PostData.TryGetRawData(out var requestBody) is true)
        {
            entry.RequestBody = requestBody;
        }

        // Response body
        if (response?.Content?.Text is { } responseText)
        {
            entry.ResponseBody = string.Equals(response.Content.Encoding, "base64", StringComparison.OrdinalIgnoreCase)
                ? Convert.FromBase64String(responseText)
                : System.Text.Encoding.UTF8.GetBytes(responseText);
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
        };

        var response = new HarResponse
        {
            Status = entry.StatusCode,
            StatusText = "",
            Headers = ConvertToHarHeaders(entry.ResponseHeaders),
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
                postData.ExtensionData = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
                {
                    [HarPostDataExtensions.DefaultEncodingExtensionName] = System.Text.Json.JsonDocument.Parse("\"base64\"").RootElement.Clone(),
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
        catch (System.Text.DecoderFallbackException)
        {
            text = null;
            return false;
        }
    }

}
