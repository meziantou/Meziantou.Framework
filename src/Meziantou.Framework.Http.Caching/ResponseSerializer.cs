using System.Net.Http.Headers;
using System.Text.Json;

namespace HttpCaching;

internal static class ResponseSerializer
{
    public static async Task<byte[]> SerializeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = response.Content is null ? null : await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var serialized = new SerializedResponseMessage
        {
            HttpStatusCode = response.StatusCode,
            ReasonPhrase = response.ReasonPhrase,
            Headers = CopyHeaders(response.Headers),
            ContentHeaders = CopyHeaders(response.Content.Headers),
            TrailingHeaders = CopyHeaders(response.TrailingHeaders),
            Content = content,
        };

        return JsonSerializer.SerializeToUtf8Bytes(serialized, SerializationContext.Default.SerializedResponseMessage);
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

        if (serialized.Headers != null)
        {
            foreach (var header in serialized.Headers)
            {
                response.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (response.Content is not null && serialized.ContentHeaders != null)
        {
            foreach (var header in serialized.ContentHeaders)
            {
                response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (serialized.TrailingHeaders != null)
        {
            foreach (var header in serialized.TrailingHeaders)
            {
                response.TrailingHeaders.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return response;
    }

    private static List<KeyValuePair<string, string[]>> CopyHeaders(HttpHeaders headers)
    {
        var result = new List<KeyValuePair<string, string[]>>();
        foreach (var header in headers)
        {
            result.Add(new KeyValuePair<string, string[]>(header.Key, header.Value.ToArray()));
        }
        return result;
    }
}
