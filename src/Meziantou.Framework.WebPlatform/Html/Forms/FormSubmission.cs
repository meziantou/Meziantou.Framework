using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The body of a POST navigation (https://html.spec.whatwg.org/multipage/browsing-the-web.html#post-resource): the submission of a form.</summary>
internal sealed class PostResource(byte[] body, string contentType)
{
    /// <summary>Gets the request body.</summary>
    public byte[] Body { get; } = body;

    /// <summary>Gets the request content-type.</summary>
    public string ContentType { get; } = contentType;
}

/// <summary>The encodings of the form submission (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#form-submission-algorithm).</summary>
internal static class FormSubmissionEncoding
{
    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#convert-to-a-list-of-name-value-pairs</summary>
    public static List<KeyValuePair<string, string>> ConvertToNameValuePairs(List<KeyValuePair<string, FormDataEntryValue>> entryList)
    {
        var list = new List<KeyValuePair<string, string>>(entryList.Count);
        foreach (var (name, value) in entryList)
        {
            var stringValue = value.File is { } file ? file.Name : value.StringValue ?? "";
            list.Add(new(NormalizeNewlines(name), NormalizeNewlines(stringValue)));
        }

        return list;
    }

    /// <summary>Replaces every CR not followed by LF, and every LF not preceded by CR, by CRLF.</summary>
    public static string NormalizeNewlines(string value)
    {
        if (value.AsSpan().IndexOfAny('\r', '\n') < 0)
            return value;

        var result = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\r')
            {
                result.Append("\r\n");
                if (i + 1 < value.Length && value[i + 1] == '\n')
                {
                    i++;
                }
            }
            else if (c == '\n')
            {
                result.Append("\r\n");
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>https://url.spec.whatwg.org/#concept-urlencoded-serializer</summary>
    public static string SerializeUrlEncoded(List<KeyValuePair<string, string>> pairs, WebEncodingId encoding) => FormUrlEncoded.Serialize(pairs, encoding);

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#text/plain-encoding-algorithm</summary>
    public static string SerializeTextPlain(List<KeyValuePair<string, string>> pairs)
    {
        var result = new StringBuilder();
        foreach (var (name, value) in pairs)
        {
            result.Append(name).Append('=').Append(value).Append("\r\n");
        }

        return result.ToString();
    }

    /// <summary>Encodes a string with the "html" error mode (https://encoding.spec.whatwg.org/#encode): the code points that the encoding cannot represent are numeric character references.</summary>
    public static byte[] Encode(string value, WebEncodingId encoding)
    {
        var output = new ArrayBufferWriter<byte>(Math.Max(1, value.Length));
        WebEncodingRegistry.CreateEncoder(encoding).EncodeHtml(value, output);
        return output.WrittenSpan.ToArray();
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#multipart/form-data-encoding-algorithm
    /// (RFC 7578): returns the body and the boundary.
    /// </summary>
    public static (byte[] Body, string Boundary) SerializeMultipart(List<KeyValuePair<string, FormDataEntryValue>> entryList, WebEncodingId encoding)
    {
        // https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#multipart/form-data-boundary-string: as the
        // browsers, a random string that is unlikely to appear in the body
        var boundary = "----formdata-webplatform-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
        var output = new ArrayBufferWriter<byte>();
        foreach (var (name, value) in entryList)
        {
            Write(output, "--" + boundary + "\r\n");
            Write(output, "Content-Disposition: form-data; name=\"");
            output.Write(EscapeQuotedValue(Encode(NormalizeNewlines(name), encoding)));
            if (value.File is { } file)
            {
                Write(output, "\"; filename=\"");
                output.Write(EscapeQuotedValue(Encode(file.Name, encoding)));
                Write(output, "\"\r\nContent-Type: " + (file.Type.Length > 0 ? file.Type : "application/octet-stream") + "\r\n\r\n");
                output.Write(file.Data.Span);
            }
            else
            {
                Write(output, "\"\r\n\r\n");
                output.Write(Encode(NormalizeNewlines(value.StringValue ?? ""), encoding));
            }

            Write(output, "\r\n");
        }

        Write(output, "--" + boundary + "--\r\n");
        return (output.WrittenSpan.ToArray(), boundary);

        static void Write(ArrayBufferWriter<byte> output, string value) => output.Write(Encoding.ASCII.GetBytes(value));

        // The field names and the filenames escape LF, CR and the quotation mark
        static byte[] EscapeQuotedValue(byte[] value)
        {
            if (value.AsSpan().IndexOfAny((byte)'\n', (byte)'\r', (byte)'"') < 0)
                return value;

            var result = new List<byte>(value.Length + 8);
            foreach (var b in value)
            {
                switch (b)
                {
                    case (byte)'\n':
                        result.AddRange("%0A"u8);
                        break;
                    case (byte)'\r':
                        result.AddRange("%0D"u8);
                        break;
                    case (byte)'"':
                        result.AddRange("%22"u8);
                        break;
                    default:
                        result.Add(b);
                        break;
                }
            }

            return [.. result];
        }
    }
}
