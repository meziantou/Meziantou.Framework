using System.Buffers;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Meziantou.Framework.OpenTelemetryCollector;

internal static class OpenTelemetryHttpPayload
{
    public const string ProtobufContentType = "application/x-protobuf";
    public const string OctetStreamContentType = "application/octet-stream";
    public const string JsonContentType = "application/json";

    private const string GzipContentEncoding = "gzip";
    private const string IdentityContentEncoding = "identity";
    private const int CopyBufferSize = 81920;

    private static readonly JsonParser OtlpJsonParser = new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true));

    public static bool TryGetPayloadFormat(string? contentType, out OpenTelemetryPayloadFormat format)
    {
        format = OpenTelemetryPayloadFormat.Protobuf;
        if (string.IsNullOrEmpty(contentType))
        {
            return true;
        }

        if (!MediaTypeHeaderValue.TryParse(contentType, out var mediaTypeHeaderValue))
        {
            return false;
        }

        var mediaType = mediaTypeHeaderValue.MediaType.Value;
        if (string.Equals(mediaType, ProtobufContentType, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mediaType, OctetStreamContentType, StringComparison.OrdinalIgnoreCase))
        {
            format = OpenTelemetryPayloadFormat.Protobuf;
            return true;
        }

        if (string.Equals(mediaType, JsonContentType, StringComparison.OrdinalIgnoreCase))
        {
            format = OpenTelemetryPayloadFormat.Json;
            return true;
        }

        return false;
    }

    /// <summary>Determines whether the <c>Content-Encoding</c> of the request is supported.</summary>
    public static bool TryGetContentEncoding(HttpRequest request, out bool decompressGzip)
    {
        ArgumentNullException.ThrowIfNull(request);

        decompressGzip = false;
        foreach (var headerValue in request.Headers.ContentEncoding)
        {
            if (string.IsNullOrEmpty(headerValue))
            {
                continue;
            }

            foreach (var range in headerValue.AsSpan().Split(','))
            {
                var encoding = headerValue.AsSpan(range).Trim();
                if (encoding.IsEmpty || encoding.Equals(IdentityContentEncoding, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Only a single gzip layer is supported. Anything else must be reported as an unsupported media type.
                if (!encoding.Equals(GzipContentEncoding, StringComparison.OrdinalIgnoreCase) || decompressGzip)
                {
                    return false;
                }

                decompressGzip = true;
            }
        }

        return true;
    }

    /// <summary>Reads the request body, optionally decompressing it, and enforces <paramref name="maxRequestBodySize"/> on the decompressed payload.</summary>
    /// <returns>The payload, or an HTTP status code describing why the payload could not be read.</returns>
    public static async Task<(byte[]? Payload, int ErrorStatusCode)> ReadPayloadAsync(HttpRequest request, bool decompressGzip, long? maxRequestBodySize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            // The limit is enforced on the decompressed payload so a compressed request cannot expand beyond it.
            if (decompressGzip)
            {
                await using var gzipStream = new GZipStream(request.Body, CompressionMode.Decompress, leaveOpen: true);
                return await ReadPayloadCoreAsync(gzipStream, maxRequestBodySize, cancellationToken);
            }

            return await ReadPayloadCoreAsync(request.Body, maxRequestBodySize, cancellationToken);
        }
        catch (InvalidDataException)
        {
            // The body is not a valid gzip stream
            return (null, StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<(byte[]? Payload, int ErrorStatusCode)> ReadPayloadCoreAsync(Stream stream, long? maxRequestBodySize, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var rentedBuffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            while (true)
            {
                var readCount = await stream.ReadAsync(rentedBuffer, cancellationToken);
                if (readCount is 0)
                {
                    break;
                }

                if (maxRequestBodySize is { } maxSize && buffer.Length + readCount > maxSize)
                {
                    return (null, StatusCodes.Status413PayloadTooLarge);
                }

                buffer.Write(rentedBuffer, 0, readCount);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }

        return (buffer.ToArray(), 0);
    }

    public static TRequest Parse<TRequest>(MessageParser<TRequest> parser, OpenTelemetryPayloadFormat format, byte[] payload)
        where TRequest : class, IMessage<TRequest>, new()
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(payload);

        if (format is OpenTelemetryPayloadFormat.Json)
        {
            var node = JsonNode.Parse(payload) ?? throw new JsonException("The OTLP/JSON payload is null.");
            NormalizeIdentifiers(node);
            return OtlpJsonParser.Parse<TRequest>(node.ToJsonString());
        }

        return parser.ParseFrom(payload);
    }

    /// <summary>Converts the hex-encoded identifiers of an OTLP/JSON payload to the base64 encoding used by the Protobuf JSON mapping.</summary>
    /// <remarks>
    /// OTLP/JSON deviates from the Protobuf JSON mapping: <c>trace_id</c> and <c>span_id</c> are hex-encoded instead of
    /// base64-encoded. Both encodings have distinct lengths for a given identifier size, so they cannot be confused.
    /// </remarks>
    private static void NormalizeIdentifiers(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject jsonObject:
            {
                List<KeyValuePair<string, string>>? updates = null;
                foreach (var property in jsonObject)
                {
                    if (TryGetIdentifierByteCount(property.Key, out var byteCount)
                        && property.Value is JsonValue jsonValue
                        && jsonValue.TryGetValue<string>(out var text)
                        && TryConvertHexToBase64(text, byteCount, out var base64))
                    {
                        updates ??= [];
                        updates.Add(new KeyValuePair<string, string>(property.Key, base64));
                    }
                    else
                    {
                        NormalizeIdentifiers(property.Value);
                    }
                }

                if (updates is not null)
                {
                    foreach (var (propertyName, value) in updates)
                    {
                        jsonObject[propertyName] = value;
                    }
                }

                break;
            }

            case JsonArray jsonArray:
            {
                foreach (var item in jsonArray)
                {
                    NormalizeIdentifiers(item);
                }

                break;
            }
        }
    }

    private static bool TryGetIdentifierByteCount(string propertyName, out int byteCount)
    {
        switch (propertyName)
        {
            case "traceId" or "trace_id":
                byteCount = 16;
                return true;

            case "spanId" or "span_id" or "parentSpanId" or "parent_span_id":
                byteCount = 8;
                return true;

            default:
                byteCount = 0;
                return false;
        }
    }

    private static bool TryConvertHexToBase64(string? value, int byteCount, [NotNullWhen(true)] out string? base64)
    {
        base64 = null;
        if (value is null || value.Length != byteCount * 2)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[16];
        if (Convert.FromHexString(value, bytes, out _, out var written) is not OperationStatus.Done || written != byteCount)
        {
            return false;
        }

        base64 = Convert.ToBase64String(bytes[..byteCount]);
        return true;
    }
}
