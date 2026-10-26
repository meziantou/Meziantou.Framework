using System.Text;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;
using File = Meziantou.Framework.WebPlatform.Dom.FileApi.File;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The parsing of the bodies of the Fetch API (<c>formData()</c>, https://fetch.spec.whatwg.org/#body-mixin).</summary>
internal static class FetchBodyParsing
{
    /// <summary>
    /// Parses a body as a <see cref="FormData"/> (the formData() steps of https://fetch.spec.whatwg.org/#dom-body-formdata):
    /// <c>multipart/form-data</c> or <c>application/x-www-form-urlencoded</c>. Throws a TypeError on failure.
    /// </summary>
    public static FormData ParseFormData(byte[] bytes, string? mimeTypeString)
    {
        var mimeType = mimeTypeString is null ? null : MimeType.Parse(mimeTypeString);
        if (mimeType?.Essence == "multipart/form-data")
            return new FormData(ParseMultipart(bytes, mimeType) ?? throw new TypeErrorException("The body is not valid multipart/form-data"));

        if (mimeType?.Essence == "application/x-www-form-urlencoded")
        {
            var entries = new List<KeyValuePair<string, FormDataEntryValue>>();
            foreach (var (name, value) in FormUrlEncoded.Parse(bytes))
            {
                entries.Add(new(name, value));
            }

            return new FormData(entries);
        }

        throw new TypeErrorException("The MIME type of the body is not multipart/form-data or application/x-www-form-urlencoded");
    }

    /// <summary>https://andreubotella.github.io/multipart-form-data/#multipart-form-data-parser: null on failure.</summary>
    private static List<KeyValuePair<string, FormDataEntryValue>>? ParseMultipart(ReadOnlySpan<byte> input, MimeType mimeType)
    {
        if (mimeType.GetParameter("boundary") is not { } boundaryParameter)
            return null;

        var boundary = FetchHeaders.IsomorphicEncode(boundaryParameter);
        var entries = new List<KeyValuePair<string, FormDataEntryValue>>();
        var position = 0;
        if (!StartsWith(input, position, "--"u8) || !StartsWith(input, position + 2, boundary))
            return null;

        position += 2 + boundary.Length;
        var delimiter = new byte[4 + boundary.Length];
        "\r\n--"u8.CopyTo(delimiter);
        boundary.CopyTo(delimiter, 4);
        while (true)
        {
            // The close delimiter, followed by the transport padding and the end of the body or a CRLF and the epilogue
            // (https://www.rfc-editor.org/rfc/rfc2046#section-5.1.1). A body without part is the serialization of an
            // empty FormData.
            if (StartsWith(input, position, "--"u8))
            {
                position += 2;
                while (position < input.Length && input[position] is (byte)' ' or (byte)'\t')
                {
                    position++;
                }

                return position == input.Length || StartsWith(input, position, "\r\n"u8) ? entries : null;
            }

            // The transport padding (https://www.rfc-editor.org/rfc/rfc2046#section-5.1.1)
            while (position < input.Length && input[position] is (byte)' ' or (byte)'\t')
            {
                position++;
            }

            if (!StartsWith(input, position, "\r\n"u8))
                return null;

            position += 2;
            if (ParseHeaders(input, ref position) is not var (name, filename, contentType))
                return null;

            var end = input[position..].IndexOf(delimiter);
            if (end < 0)
                return null;

            var body = input.Slice(position, end);
            position += end + delimiter.Length;
            FormDataEntryValue value;
            if (filename is not null)
            {
                value = new File([body.ToArray()], filename, new FilePropertyBag { Type = contentType ?? "text/plain" });
            }
            else
            {
                value = DecodeUtf8WithoutBom(body);
            }

            entries.Add(new(name, value));
        }
    }

    /// <summary>https://andreubotella.github.io/multipart-form-data/#parse-multipart-form-data-headers</summary>
    private static (string Name, string? Filename, string? ContentType)? ParseHeaders(ReadOnlySpan<byte> input, ref int position)
    {
        string? name = null;
        string? filename = null;
        string? contentType = null;
        while (true)
        {
            if (StartsWith(input, position, "\r\n"u8))
            {
                position += 2;
                return name is null ? null : (name, filename, contentType);
            }

            var headerNameStart = position;
            while (position < input.Length && input[position] is not ((byte)'\n' or (byte)'\r' or (byte)':'))
            {
                position++;
            }

            var headerName = FetchHeaders.IsomorphicDecode(input[headerNameStart..position]).TrimEnd('\t', ' ');
            if (!FetchHeaders.IsHeaderName(headerName) || position >= input.Length || input[position] != ':')
                return null;

            position++;
            while (position < input.Length && input[position] is (byte)'\t' or (byte)' ')
            {
                position++;
            }

            switch (InfraAscii.ToAsciiLowercase(headerName))
            {
                case "content-disposition":
                    filename = null;
                    if (!StartsWith(input, position, "form-data; name=\""u8))
                        return null;

                    position += "form-data; name="u8.Length;
                    name = ParseQuotedString(input, ref position);
                    if (name is null)
                        return null;

                    if (StartsWith(input, position, "; filename=\""u8))
                    {
                        position += "; filename="u8.Length;
                        filename = ParseQuotedString(input, ref position);
                        if (filename is null)
                            return null;
                    }

                    break;

                case "content-type":
                {
                    var valueStart = position;
                    while (position < input.Length && input[position] is not ((byte)'\n' or (byte)'\r'))
                    {
                        position++;
                    }

                    contentType = FetchHeaders.IsomorphicDecode(input[valueStart..position]).TrimEnd('\t', ' ');
                    break;
                }

                default:
                    while (position < input.Length && input[position] is not ((byte)'\n' or (byte)'\r'))
                    {
                        position++;
                    }

                    break;
            }

            if (!StartsWith(input, position, "\r\n"u8))
                return null;

            position += 2;
        }
    }

    /// <summary>https://andreubotella.github.io/multipart-form-data/#parse-a-multipart-form-data-name: the escapes %0A, %0D and %22 are decoded.</summary>
    private static string? ParseQuotedString(ReadOnlySpan<byte> input, ref int position)
    {
        if (position >= input.Length || input[position] != '"')
            return null;

        position++;
        var start = position;
        while (position < input.Length && input[position] is not ((byte)'"' or (byte)'\n' or (byte)'\r'))
        {
            position++;
        }

        if (position >= input.Length || input[position] != '"')
            return null;

        var value = DecodeUtf8WithoutBom(input[start..position]);
        position++;
        return value.Replace("%0A", "\n", StringComparison.Ordinal).Replace("%0D", "\r", StringComparison.Ordinal).Replace("%22", "\"", StringComparison.Ordinal);
    }

    private static bool StartsWith(ReadOnlySpan<byte> input, int position, ReadOnlySpan<byte> value) => position <= input.Length && input[position..].StartsWith(value);

    private static string DecodeUtf8WithoutBom(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith("\U0000FEFF"u8))
        {
            bytes = bytes[3..];
        }

        return Encodings.WebEncodingRegistry.CreateDecoder(Encodings.WebEncodingId.Utf8).DecodeToString(bytes);
    }
}
