using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class HttpContentConverter : HumanReadableConverter<HttpContent>
{
    private static readonly HashSet<string> TextMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/ecmascript",
        "application/javascript",
        "application/json",
        "application/x-ecmascript",
        "application/x-javascript",
        "application/x-www-form-urlencoded",
        "application/xml",
    };

    private static readonly HttpHeadersConverter<HttpContentHeaders> DefaultMultipartHeadersConverter = new(excludedHeaderNames: null, [MultipartBoundaryFormatter.Instance]);

    private readonly HttpHeaderSettings? _headers;
    private readonly HttpHeadersConverter<HttpContentHeaders>? _headersConverter;
    private readonly HttpHeadersConverter<HttpContentHeaders> _multipartHeadersConverter;

    public HttpContentConverter()
    {
        _multipartHeadersConverter = DefaultMultipartHeadersConverter;
    }

    // Used for the content of the HTTP messages configured by AddHttpConverters, so the header options also apply to the content headers
    public HttpContentConverter(HttpHeaderSettings headers)
    {
        _headers = headers;
        _headersConverter = new HttpHeadersConverter<HttpContentHeaders>(headers);
        _multipartHeadersConverter = new HttpHeadersConverter<HttpContentHeaders>(headers.ExcludedHeaderNames, [.. headers.Formatters, MultipartBoundaryFormatter.Instance]);
    }

    protected override void WriteValue(HumanReadableTextWriter writer, HttpContent? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);

        // This instance is set on a member, so it would otherwise take precedence over the converters registered by the user
        if (_headers is not null && options.Converters.Any(converter => converter.CanConvert(value.GetType())))
        {
            HumanReadableSerializer.Serialize(writer, value, value.GetType(), options);
            return;
        }

        writer.StartObject();

        var isMultipart = value is IEnumerable<HttpContent>;
        var hasHeaders = _headers is null ? value.Headers.NonValidated.Count > 0 : !_headers.IsEmptyAfterFiltering(value.Headers);
        if (hasHeaders)
        {
            writer.WritePropertyName("Headers");
            if (isMultipart)
            {
                _multipartHeadersConverter.WriteValue(writer, value.Headers, typeof(HttpContentHeaders), options);
            }
            else if (_headersConverter is not null)
            {
                _headersConverter.WriteValue(writer, value.Headers, typeof(HttpContentHeaders), options);
            }
            else
            {
                HumanReadableSerializer.Serialize(writer, value.Headers, options);
            }
        }

        if (hasHeaders || isMultipart)
            writer.WritePropertyName("Value");

        if (value is IEnumerable<HttpContent> collection)
        {
            if (_headers is null)
            {
                options.GetConverter(typeof(IEnumerable<HttpContent>)).WriteValue(writer, collection, typeof(IEnumerable<HttpContent>), options);
            }
            else
            {
                WriteParts(writer, collection, options);
            }
        }
        else if (CanReadAsString(value) && TryReadAsString(value, out var str))
        {
            var mediaType = value.Headers.ContentType?.MediaType;
            if (mediaType is not null)
            {
                writer.WriteFormattedValue(mediaType, str);
            }
            else
            {
                writer.WriteValue(str);
            }
        }
        else
        {
            var bytes = value.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            options.GetConverter(typeof(byte[])).WriteValue(writer, bytes, typeof(byte[]), options);
        }

        writer.EndObject();
    }

    // The parts use the same header options as the multipart content
    private void WriteParts(HumanReadableTextWriter writer, IEnumerable<HttpContent> parts, HumanReadableSerializerOptions options)
    {
        var hasItem = false;
        foreach (var part in parts)
        {
            if (!hasItem)
            {
                writer.StartArray();
                hasItem = true;
            }

            writer.StartArrayItem();
            if (part is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                WriteValue(writer, part, typeof(HttpContent), options);
            }

            writer.EndArrayItem();
        }

        if (hasItem)
        {
            writer.EndArray();
        }
        else
        {
            writer.WriteEmptyArray();
        }
    }

    private static bool TryReadAsString(HttpContent content, [NotNullWhen(true)] out string? value)
    {
        var contentToRead = content;
        ByteArrayContent? decodedContent = null;
        try
        {
            // The body of these types is the text itself, whatever the headers say
            if (!IsTextContent(content) && HasContentEncoding(content))
            {
                // The body is compressed (e.g. when the handler does not decompress the response), so decoding it as text would produce garbage
                var bytes = content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                if (!TryDecompress(bytes, content.Headers.ContentEncoding, out var decompressedBytes))
                {
                    value = null;
                    return false;
                }

                decodedContent = new ByteArrayContent(decompressedBytes);
                decodedContent.Headers.ContentType = content.Headers.ContentType;
                contentToRead = decodedContent;
            }

            value = contentToRead.ReadAsStringAsync().GetAwaiter().GetResult();
            return true;
        }
        catch (InvalidOperationException)
        {
            // The charset is invalid or not supported (e.g. windows-1252 when CodePagesEncodingProvider is not registered)
            value = null;
            return false;
        }
        finally
        {
            decodedContent?.Dispose();
        }
    }

    private static bool IsTextContent(HttpContent content) => content is StringContent or FormUrlEncodedContent or System.Net.Http.Json.JsonContent;

    private static bool HasContentEncoding(HttpContent content)
    {
        foreach (var encoding in content.Headers.ContentEncoding)
        {
            if (!string.Equals(encoding, "identity", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool TryDecompress(byte[] bytes, ICollection<string> contentEncodings, [NotNullWhen(true)] out byte[]? result)
    {
        result = null;
        try
        {
            // The encodings are listed in the order they were applied
            foreach (var encoding in contentEncodings.Reverse())
            {
                if (string.Equals(encoding, "identity", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.Equals(encoding, "gzip", StringComparison.OrdinalIgnoreCase) || string.Equals(encoding, "x-gzip", StringComparison.OrdinalIgnoreCase))
                {
                    bytes = Decompress(bytes, static stream => new GZipStream(stream, CompressionMode.Decompress));
                }
                else if (string.Equals(encoding, "deflate", StringComparison.OrdinalIgnoreCase))
                {
                    bytes = Decompress(bytes, static stream => new ZLibStream(stream, CompressionMode.Decompress));
                }
                else if (string.Equals(encoding, "br", StringComparison.OrdinalIgnoreCase))
                {
                    bytes = Decompress(bytes, static stream => new BrotliStream(stream, CompressionMode.Decompress));
                }
                else
                {
                    return false;
                }
            }

            result = bytes;
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }

        static byte[] Decompress(byte[] bytes, Func<Stream, Stream> createStream)
        {
            using var input = new MemoryStream(bytes);
            using var decompressionStream = createStream(input);
            using var output = new MemoryStream();
            decompressionStream.CopyTo(output);
            return output.ToArray();
        }
    }

    private static bool CanReadAsString(HttpContent content)
    {
        if (IsTextContent(content))
            return true;

        var charSet = content.Headers.ContentType?.CharSet;
        if (!string.IsNullOrEmpty(charSet))
            return true;

        var mimeType = content.Headers.ContentType?.MediaType;
        if (mimeType is not null)
        {
            // https://www.iana.org/assignments/media-types/media-types.xhtml
            if (mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                return true;

            if (mimeType.EndsWith("+json", StringComparison.OrdinalIgnoreCase) || mimeType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase))
                return true;

            if (TextMimeTypes.Contains(mimeType))
                return true;
        }

        return false;
    }
}
