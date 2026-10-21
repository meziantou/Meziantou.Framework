using System.Net;
using System.Text.Json;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Http.Caching.Fuzz;

public static partial class HttpCachingFuzzTargets
{
    private static readonly string[] SerializerTokens =
    [
        "{", "}", "[", "]", ",", ":", "\"", "null", "true", "false", "0", "-1", "200", "999", "1000", "1e3", "\"\"", "\\n", "\\r", "\\u0000",
        "\"HttpStatusCode\":", "\"ReasonPhrase\":", "\"Headers\":", "\"ContentHeaders\":", "\"TrailingHeaders\":", "\"Content\":", "\"Key\":", "\"Value\":",
        "\"AAAA\"", "\"YWJj\"", "\"Content-Type\"", "\"Content-Length\"", "\"ETag\"", "\"Cache-Control\"",
    ];

    private static readonly string[] SerializerHeaderNames =
    [
        "Cache-Control", "ETag", "Date", "Age", "Vary", "Expires", "Last-Modified", "Content-Type", "Content-Length", "Content-Range", "Content-Location",
        "Location", "Set-Cookie", "Warning", "WWW-Authenticate", "X-Custom", "Content-Disposition", "Allow", "Server", "Transfer-Encoding",
    ];

    /// <summary>
    /// The first byte selects the mode. A corrupted entry: the input is the persisted payload of a response, and when
    /// <c>CacheEntry.FromPersistenceEntry</c> accepts it (a <see cref="JsonException"/> is the cache miss the cache turns
    /// it into), building the response from it never throws. A response: the input describes a response (status, reason
    /// phrase, headers of the three collections, body). Either way, serializing the response and deserializing it gives
    /// the same response, and serializing it again gives the same payload (after a first round for a corrupted entry,
    /// whose JSON escapes can hold unpaired surrogates).
    /// </summary>
    private static void ResponseSerializerRoundTrip(ReadOnlySpan<byte> data)
    {
        var mode = FuzzInput.ReadByte(ref data);
        if ((mode & 1) is 0)
        {
            CacheEntry entry;
            try
            {
                entry = CacheEntry.FromPersistenceEntry(new HttpCachePersistenceEntry { SerializedResponse = data.ToArray() });
            }
            catch (JsonException)
            {
                // A corrupted entry is ignored by the cache lookup
                return;
            }

            // A JSON string escape can decode to an unpaired surrogate, which the serializer writes as U+FFFD: the
            // payload is a fixed point after one round
            using var stored = ResponseSerializer.Deserialize(entry.SerializedResponse);
            using var normalized = ResponseSerializer.Deserialize(Serialize(stored));
            CheckSerializerRoundTrip(normalized);
            return;
        }

        var reader = new InputReader(data);
        var status = ((reader.ReadByte() << 8) | reader.ReadByte()) % 1000;
        var options = reader.ReadByte();
        using var response = new HttpResponseMessage((HttpStatusCode)status);
        if ((options & 1) is not 0)
        {
            // The setter rejects CR, LF and NUL, which a status line cannot hold anyway
            response.ReasonPhrase = ReadNetworkText(reader).Replace("\r", "", StringComparison.Ordinal).Replace("\0", "", StringComparison.Ordinal);
        }

        if ((options & 2) is not 0)
        {
            response.Content = new ByteArrayContent([]);
        }

        var headerCount = reader.ReadByte() % 8;
        for (var i = 0; i < headerCount; i++)
        {
            var collection = reader.ReadByte();
            var name = collection >= 0xF0 ? ReadNetworkText(reader) : FuzzInput.Pick(SerializerHeaderNames, collection);
            var value = ReadNetworkText(reader);
            _ = (collection % 3) switch
            {
                0 => response.Headers.TryAddWithoutValidation(name, value),
                1 => response.Content.Headers.TryAddWithoutValidation(name, value),
                _ => response.TrailingHeaders.TryAddWithoutValidation(name, value),
            };
        }

        if ((options & 2) is not 0)
        {
            var content = new ByteArrayContent(reader.ReadRemainingBytes());
            foreach (var header in response.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            response.Content = content;
        }

        CheckSerializerRoundTrip(response);
    }

    /// <summary>Reads a line, with its unpaired surrogates replaced by U+FFFD: a text decoded from the network has none.</summary>
    private static string ReadNetworkText(InputReader reader)
    {
        var text = reader.ReadLine();
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(text, i, 2);
                i++;
            }
            else
            {
                builder.Append(char.IsSurrogate(text[i]) ? '\uFFFD' : text[i]);
            }
        }

        return builder.ToString();
    }

    private static void CheckSerializerRoundTrip(HttpResponseMessage response)
    {
        var expected = DescribeResponse(response);
        var payload = Serialize(response);
        ResponseSerializer.EnsureValid(payload);
        using var roundTripped = ResponseSerializer.Deserialize(payload);
        FuzzAssert.Equal(expected, DescribeResponse(roundTripped), "The deserialized response differs from the serialized one");

        var secondPayload = Serialize(roundTripped);
        FuzzAssert.Equal(Encoding.UTF8.GetString(payload), Encoding.UTF8.GetString(secondPayload), "Serializing the deserialized response gives a different payload");
    }

    private static byte[] Serialize(HttpResponseMessage response) => ResponseSerializer.SerializeAsync(response, CancellationToken.None).GetAwaiter().GetResult();

    private static string DescribeResponse(HttpResponseMessage response)
    {
        var body = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();

        // HttpContent computes Content-Length from the body once the body is read, so only a different value tells something
        var computedContentLength = body.Length.ToString(CultureInfo.InvariantCulture);
        var builder = new StringBuilder();
        builder.Append((int)response.StatusCode).Append(' ').Append(response.ReasonPhrase).Append('\n');
        AppendHeaders(builder, "header", response.Headers, contentLength: null);
        AppendHeaders(builder, "content", response.Content.Headers, computedContentLength);
        AppendHeaders(builder, "trailer", response.TrailingHeaders, contentLength: null);
        builder.Append(Convert.ToHexString(body));
        return builder.ToString();

        static void AppendHeaders(StringBuilder builder, string kind, System.Net.Http.Headers.HttpHeaders headers, string? contentLength)
        {
            foreach (var header in headers)
            {
                if (contentLength is not null && string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase) && header.Value.SequenceEqual([contentLength], StringComparer.Ordinal))
                    continue;

                builder.Append(kind).Append(' ').Append(header.Key).Append(": ").AppendJoin(" | ", header.Value).Append('\n');
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetResponseSerializerSeeds()
    {
        using (var response = new HttpResponseMessage(HttpStatusCode.OK) { ReasonPhrase = "OK", Content = new ByteArrayContent("hello"u8.ToArray()) })
        {
            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=60");
            response.Headers.TryAddWithoutValidation("ETag", "\"v1\"");
            response.Headers.TryAddWithoutValidation("Vary", "Accept-Language");
            response.Content.Headers.TryAddWithoutValidation("Content-Type", "text/plain; charset=utf-8");
            response.TrailingHeaders.TryAddWithoutValidation("X-Checksum", "abc");
            yield return new FuzzSeed("payload", (byte[])[0, .. Serialize(response)]);
        }

        yield return FuzzSeed.FromText("payload-minimal", [0], "{\"HttpStatusCode\":404}");
        yield return FuzzSeed.FromText("payload-not-json", [0], "{ this is not valid json");
        yield return FuzzSeed.FromText("payload-empty", [0], "");
        // Status (2 bytes), options, reason phrase, header count, then a collection byte (which also picks the name), a name
        // when the byte is 0xF0 or more, and a value per header; the body comes last.
        yield return new FuzzSeed("response", (byte[])[1, .. InputReader.Create(0, 200, 3, "OK", 5, 0, "max-age=60", 3, "\"v1\"", 7, "text/plain; charset=utf-8", 2, "abc", 0xF0, "X-Custom", "value"), .. "body"u8]);
        yield return new FuzzSeed("response-no-content", (byte[])[1, .. InputReader.Create(1, 48, 0, 2, 4, "Accept, Accept-Language", 6, "\"etag\"")]);
        yield return new FuzzSeed("response-reason", (byte[])[1, .. InputReader.Create(1, 244, 1, "Not Found", 1, 7, "text/html")]);
    }
}
