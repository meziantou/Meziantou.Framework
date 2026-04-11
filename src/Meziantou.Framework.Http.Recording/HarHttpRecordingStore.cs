using Meziantou.Framework.HttpArchive;

namespace Meziantou.Framework.Http.Recording;

/// <summary>Stores recorded HTTP entries in HAR (HTTP Archive) 1.2 format.</summary>
public sealed class HarHttpRecordingStore : IHttpRecordingStore
{
    private static readonly System.Text.UTF8Encoding Utf8EncodingThrowOnInvalid = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private const string MeziantouEncodingExtensionName = "x-meziantou-encoding";
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

        var entries = new List<HttpRecordingEntry>(doc.Log.Entries.Count);
        foreach (var harEntry in doc.Log.Entries)
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

        var doc = new HarDocument();
        doc.Log.Version = "1.2";
        doc.Log.Creator = new HarCreator { Name = "Meziantou.Framework.Http.Recording", Version = "1.0.0" };

        foreach (var entry in entries)
        {
            doc.Log.Entries.Add(ConvertToHarEntry(entry));
        }

        await using var stream = File.Create(_filePath);
        await doc.WriteToAsync(stream, indented: true, cancellationToken).ConfigureAwait(false);
    }

    private static HttpRecordingEntry ConvertFromHarEntry(HarEntry harEntry)
    {
        var entry = new HttpRecordingEntry
        {
            Method = harEntry.Request.Method,
            RequestUri = harEntry.Request.Url,
            StatusCode = harEntry.Response.Status,
            RecordedAt = harEntry.StartedDateTime,
        };

        // Request headers
        if (harEntry.Request.Headers.Count > 0)
        {
            entry.RequestHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in harEntry.Request.Headers)
            {
                if (entry.RequestHeaders.TryGetValue(header.Name, out var existing))
                {
                    entry.RequestHeaders[header.Name] = [.. existing, header.Value];
                }
                else
                {
                    entry.RequestHeaders[header.Name] = [header.Value];
                }
            }
        }

        // Request body
        if (harEntry.Request.PostData?.Text is not null)
        {
            if (TryGetMeziantouEncoding(harEntry.Request.PostData, out var requestEncoding) &&
                string.Equals(requestEncoding, "base64", StringComparison.OrdinalIgnoreCase))
            {
                entry.RequestBody = Convert.FromBase64String(harEntry.Request.PostData.Text);
            }
            else
            {
                entry.RequestBody = System.Text.Encoding.UTF8.GetBytes(harEntry.Request.PostData.Text);
            }
        }

        // Response headers
        if (harEntry.Response.Headers.Count > 0)
        {
            entry.ResponseHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in harEntry.Response.Headers)
            {
                if (entry.ResponseHeaders.TryGetValue(header.Name, out var existing))
                {
                    entry.ResponseHeaders[header.Name] = [.. existing, header.Value];
                }
                else
                {
                    entry.ResponseHeaders[header.Name] = [header.Value];
                }
            }
        }

        // Response body
        if (harEntry.Response.Content.Text is not null)
        {
            if (string.Equals(harEntry.Response.Content.Encoding, "base64", StringComparison.OrdinalIgnoreCase))
            {
                entry.ResponseBody = Convert.FromBase64String(harEntry.Response.Content.Text);
            }
            else
            {
                entry.ResponseBody = System.Text.Encoding.UTF8.GetBytes(harEntry.Response.Content.Text);
            }
        }

        return entry;
    }

    private static HarEntry ConvertToHarEntry(HttpRecordingEntry entry)
    {
        var harEntry = new HarEntry
        {
            StartedDateTime = entry.RecordedAt,
            Request =
            {
                Method = entry.Method,
                Url = entry.RequestUri,
            },
            Response =
            {
                Status = entry.StatusCode,
                StatusText = "",
            },
        };

        // Request headers
        if (entry.RequestHeaders is not null)
        {
            foreach (var (name, values) in entry.RequestHeaders)
            {
                foreach (var value in values)
                {
                    harEntry.Request.Headers.Add(new HarHeader { Name = name, Value = value });
                }
            }
        }

        // Request body
        if (entry.RequestBody is { Length: > 0 })
        {
            var contentType = GetContentType(entry.RequestHeaders);

            harEntry.Request.PostData = new HarPostData
            {
                MimeType = contentType,
            };

            if (IsTextMediaType(contentType) && TryDecodeUtf8(entry.RequestBody, out var requestText))
            {
                harEntry.Request.PostData.Text = requestText;
            }
            else
            {
                harEntry.Request.PostData.Text = Convert.ToBase64String(entry.RequestBody);
                harEntry.Request.PostData.ExtensionData = new Dictionary<string, System.Text.Json.JsonElement>
                {
                    [MeziantouEncodingExtensionName] = System.Text.Json.JsonSerializer.SerializeToElement("base64"),
                };
            }
        }

        // Response headers
        if (entry.ResponseHeaders is not null)
        {
            foreach (var (name, values) in entry.ResponseHeaders)
            {
                foreach (var value in values)
                {
                    harEntry.Response.Headers.Add(new HarHeader { Name = name, Value = value });
                }
            }
        }

        // Response body
        if (entry.ResponseBody is { Length: > 0 })
        {
            var mimeType = GetContentType(entry.ResponseHeaders);

            harEntry.Response.Content = new HarContent
            {
                Size = entry.ResponseBody.Length,
                MimeType = mimeType,
            };

            if (IsTextMediaType(mimeType) && TryDecodeUtf8(entry.ResponseBody, out var responseText))
            {
                harEntry.Response.Content.Text = responseText;
            }
            else
            {
                harEntry.Response.Content.Encoding = "base64";
                harEntry.Response.Content.Text = Convert.ToBase64String(entry.ResponseBody);
            }
        }

        return harEntry;
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
        var separatorIndex = contentType.IndexOf(';');
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

    private static bool TryGetMeziantouEncoding(HarPostData postData, out string? encoding)
    {
        if (postData.ExtensionData is not null &&
            postData.ExtensionData.TryGetValue(MeziantouEncodingExtensionName, out var value) &&
            value.ValueKind is System.Text.Json.JsonValueKind.String)
        {
            encoding = value.GetString();
            return true;
        }

        encoding = null;
        return false;
    }
}
