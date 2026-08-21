using System.Net.Http.Headers;
using System.Text.Json;

namespace Meziantou.Framework.Http.Caching;

internal static class ResponseSerializer
{
    public static async Task<byte[]> SerializeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var serialized = await SerializeAsync(response, maximumSize: null, cancellationToken).ConfigureAwait(false);
        return serialized!;
    }

    /// <summary>Serializes the response, or returns <see langword="null"/> when it exceeds <paramref name="maximumSize"/>.</summary>
    /// <remarks>
    /// The body is checked before it is serialized. JSON encoding only grows the payload, since the body is
    /// Base64-encoded and the headers are added, so a body already over the limit can never fit and the
    /// serialization is skipped entirely.
    /// </remarks>
    public static async Task<byte[]?> SerializeAsync(HttpResponseMessage response, long? maximumSize, CancellationToken cancellationToken)
    {
        var content = response.Content is null ? null : await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (maximumSize is not null && content is not null && content.Length > maximumSize.GetValueOrDefault())
            return null;

        var serialized = new SerializedResponseMessage
        {
            HttpStatusCode = response.StatusCode,
            ReasonPhrase = response.ReasonPhrase,
            Headers = CopyHeaders(response.Headers),
            ContentHeaders = CopyHeaders(response.Content?.Headers),
            TrailingHeaders = CopyHeaders(response.TrailingHeaders),
            Content = content,
        };

        var payload = JsonSerializer.SerializeToUtf8Bytes(serialized, SerializationContext.Default.SerializedResponseMessage);
        if (maximumSize is not null && payload.Length > maximumSize.GetValueOrDefault())
            return null;

        return payload;
    }

    /// <summary>Throws when the payload is not well-formed JSON.</summary>
    /// <remarks>
    /// Entries come from an external store and can be truncated or corrupted. Validating on read keeps the
    /// failure inside the cache lookup, where it is turned into a cache miss, instead of letting it escape
    /// from the message handler once the response is being built.
    /// </remarks>
    public static void EnsureWellFormed(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            throw new JsonException("The serialized response is empty");

        var reader = new Utf8JsonReader(data);
        while (reader.Read())
        {
        }
    }

    public static HttpResponseMessage Deserialize(byte[] data)
    {
        var serialized = JsonSerializer.Deserialize(data, SerializationContext.Default.SerializedResponseMessage);
        if (serialized is null)
            throw new ArgumentException("Invalid serialized response data", nameof(data));

        var response = new HttpResponseMessage(serialized.HttpStatusCode)
        {
            ReasonPhrase = serialized.ReasonPhrase,
        };

        if (serialized.Content is not null)
        {
            response.Content = new ByteArrayContent(serialized.Content);
        }

        if (serialized.Headers is not null)
        {
            foreach (var header in serialized.Headers)
            {
                response.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (response.Content is not null && serialized.ContentHeaders is not null)
        {
            foreach (var header in serialized.ContentHeaders)
            {
                response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (serialized.TrailingHeaders is not null)
        {
            foreach (var header in serialized.TrailingHeaders)
            {
                response.TrailingHeaders.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return response;
    }

    [return: NotNullIfNotNull(nameof(headers))]
    private static List<KeyValuePair<string, string[]>>? CopyHeaders(HttpHeaders? headers)
    {
        if (headers is null)
            return null;

        var result = new List<KeyValuePair<string, string[]>>();
        foreach (var header in headers)
        {
            result.Add(new KeyValuePair<string, string[]>(header.Key, header.Value.ToArray()));
        }

        return result;
    }
}
