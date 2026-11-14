using System.Globalization;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The handlers of the Compression Dictionary Transport tests (<c>fetch/compression-dictionary/resources</c>): the
/// dictionaries with a <c>Use-As-Dictionary</c> header, the data compressed with a dictionary (the <c>dcb</c> and <c>dcz</c>
/// content codings, decoded by the library) and the echo of the request headers. The bodies of the other content codings are
/// decoded by the network layer: the Content-Length is the one of the encoded body.
/// </summary>
internal static partial class WptPythonHandlers
{
    private const string CompressionDictionaryResources = "/fetch/compression-dictionary/resources/";

    private static ReadOnlySpan<byte> CompressedDataDcbData =>
    [
        0xFF, 0x44, 0x43, 0x42, 0x53, 0x96, 0x9B, 0xCF, 0x5E, 0x96, 0x0E, 0x0E, 0xDB, 0xF0, 0xA4, 0xBD,
        0xDE, 0x6B, 0x0B, 0x3E, 0x93, 0x81, 0xE1, 0x56, 0xDE, 0x7F, 0x5B, 0x91, 0xCE, 0x83, 0x91, 0x62,
        0x42, 0x70, 0xF4, 0x16, 0xA1, 0x98, 0x01, 0x80, 0x62, 0xA4, 0x4C, 0x1D, 0xDF, 0x12, 0x84, 0x8C,
        0xAE, 0xC2, 0xCA, 0x60, 0x22, 0x07, 0x6E, 0x81, 0x05, 0x14, 0xC9, 0xB7, 0xC3, 0x44, 0x8E, 0xBC,
        0x16, 0xE0, 0x15, 0x0E, 0xEC, 0xC1, 0xEE, 0x34, 0x33, 0x3E, 0x0D
    ];

    private static ReadOnlySpan<byte> CompressedDataDczData =>
    [
        0x5E, 0x2A, 0x4D, 0x18, 0x20, 0x00, 0x00, 0x00, 0x53, 0x96, 0x9B, 0xCF, 0x5E, 0x96, 0x0E, 0x0E,
        0xDB, 0xF0, 0xA4, 0xBD, 0xDE, 0x6B, 0x0B, 0x3E, 0x93, 0x81, 0xE1, 0x56, 0xDE, 0x7F, 0x5B, 0x91,
        0xCE, 0x83, 0x91, 0x62, 0x42, 0x70, 0xF4, 0x16, 0x28, 0xB5, 0x2F, 0xFD, 0x24, 0x34, 0xF5, 0x00,
        0x00, 0x98, 0x63, 0x6F, 0x6D, 0x70, 0x72, 0x65, 0x73, 0x73, 0x65, 0x64, 0x61, 0x74, 0x61, 0x20,
        0x75, 0x73, 0x69, 0x6E, 0x67, 0x03, 0x00, 0x59, 0xF9, 0x73, 0x54, 0x46, 0x27, 0x26, 0x10, 0x9E,
        0x99, 0xF2, 0xBC
    ];

    private static ReadOnlySpan<byte> CompressedDataLargeDcbData =>
    [
        0xFF, 0x44, 0x43, 0x42, 0x53, 0x96, 0x9B, 0xCF, 0x5E, 0x96, 0x0E, 0x0E, 0xDB, 0xF0, 0xA4, 0xBD,
        0xDE, 0x6B, 0x0B, 0x3E, 0x93, 0x81, 0xE1, 0x56, 0xDE, 0x7F, 0x5B, 0x91, 0xCE, 0x83, 0x91, 0x62,
        0x42, 0x70, 0xF4, 0x16, 0xA1, 0xD8, 0x0A, 0x00, 0x2F, 0xEA, 0xB6, 0x54, 0x17, 0xD2, 0x63, 0x1F,
        0x3A, 0x79, 0x10, 0x85, 0x6F, 0xB0, 0x01, 0x07, 0x6E, 0x41, 0x4A, 0xC3, 0x42, 0xB4, 0xE9, 0xB0,
        0x01, 0x17, 0x71, 0x94, 0x7C, 0xE9, 0xAF, 0x81, 0xDC, 0xDE, 0xC0, 0xC8, 0xC1, 0x66, 0x88, 0x34,
        0x03, 0x5E, 0x7F, 0x8C, 0x8E, 0x46, 0x6F, 0xC9, 0x48, 0xAB, 0xC8, 0x71, 0x2C
    ];

    private static ReadOnlySpan<byte> CompressedDataLargeDczData =>
    [
        0x5E, 0x2A, 0x4D, 0x18, 0x20, 0x00, 0x00, 0x00, 0x53, 0x96, 0x9B, 0xCF, 0x5E, 0x96, 0x0E, 0x0E,
        0xDB, 0xF0, 0xA4, 0xBD, 0xDE, 0x6B, 0x0B, 0x3E, 0x93, 0x81, 0xE1, 0x56, 0xDE, 0x7F, 0x5B, 0x91,
        0xCE, 0x83, 0x91, 0x62, 0x42, 0x70, 0xF4, 0x16, 0x28, 0xB5, 0x2F, 0xFD, 0x64, 0x5C, 0x00, 0xFD,
        0x01, 0x00, 0xF4, 0x02, 0x20, 0x64, 0x64, 0x69, 0x74, 0x69, 0x6F, 0x6E, 0x61, 0x6C, 0x61, 0x74,
        0x61, 0x20, 0x74, 0x68, 0x61, 0x74, 0x20, 0x61, 0x6C, 0x73, 0x6F, 0x20, 0x72, 0x65, 0x66, 0x65,
        0x72, 0x65, 0x6E, 0x63, 0x65, 0x73, 0x20, 0x74, 0x68, 0x65, 0x20, 0x63, 0x6F, 0x6E, 0x74, 0x65,
        0x6E, 0x74, 0x2E, 0x04, 0x00, 0x60, 0x2D, 0x72, 0x35, 0x2B, 0xBB, 0x3C, 0xA0, 0xCE, 0xED, 0x19,
        0x04, 0x0C, 0x4B, 0x9E, 0x2F
    ];

    private static ReadOnlySpan<byte> RegisterDictionaryGzipData =>
    [
        0x1F, 0x8B, 0x08, 0x00, 0x10, 0x31, 0x47, 0x68, 0x00, 0x03, 0x0B, 0xC9, 0xC8, 0x2C, 0x56, 0x00,
        0xA2, 0x44, 0x85, 0x92, 0xD4, 0xE2, 0x12, 0x85, 0x94, 0xCC, 0xE4, 0x92, 0xCC, 0xFC, 0xBC, 0xC4,
        0xA2, 0x4A, 0x3D, 0x2E, 0x00, 0x79, 0xF2, 0x36, 0x63, 0x1B, 0x00, 0x00, 0x00
    ];

    private static ReadOnlySpan<byte> RegisterDictionaryBrData =>
    [
        0xA1, 0xD0, 0x00, 0xC0, 0x6F, 0xA4, 0x74, 0xF3, 0x56, 0xB5, 0x02, 0x48, 0x18, 0x9D, 0x2A, 0x9B,
        0xCB, 0x42, 0x14, 0x81, 0xA7, 0x14, 0xDA, 0x89, 0x29, 0x93, 0x7B, 0xC8, 0x09
    ];

    private static ReadOnlySpan<byte> RegisterDictionaryZstdData =>
    [
        0x28, 0xB5, 0x2F, 0xFD, 0x24, 0x1B, 0xD9, 0x00, 0x00, 0x54, 0x68, 0x69, 0x73, 0x20, 0x69, 0x73,
        0x20, 0x61, 0x20, 0x74, 0x65, 0x73, 0x74, 0x20, 0x64, 0x69, 0x63, 0x74, 0x69, 0x6F, 0x6E, 0x61,
        0x72, 0x79, 0x2E, 0x0A, 0x3F, 0x0D, 0x76, 0xA0
    ];

    private static ReadOnlySpan<byte> RegisterDictionaryDcbData =>
    [
        0xFF, 0x44, 0x43, 0x42, 0x53, 0x96, 0x9B, 0xCF, 0x5E, 0x96, 0x0E, 0x0E, 0xDB, 0xF0, 0xA4, 0xBD,
        0xDE, 0x6B, 0x0B, 0x3E, 0x93, 0x81, 0xE1, 0x56, 0xDE, 0x7F, 0x5B, 0x91, 0xCE, 0x83, 0x91, 0x62,
        0x42, 0x70, 0xF4, 0x16, 0xA1, 0xD0, 0x00, 0xC0, 0x2F, 0x01, 0x10, 0xC4, 0x84, 0x0A, 0x05
    ];

    private static ReadOnlySpan<byte> RegisterDictionaryDczData =>
    [
        0x5E, 0x2A, 0x4D, 0x18, 0x20, 0x00, 0x00, 0x00, 0x53, 0x96, 0x9B, 0xCF, 0x5E, 0x96, 0x0E, 0x0E,
        0xDB, 0xF0, 0xA4, 0xBD, 0xDE, 0x6B, 0x0B, 0x3E, 0x93, 0x81, 0xE1, 0x56, 0xDE, 0x7F, 0x5B, 0x91,
        0xCE, 0x83, 0x91, 0x62, 0x42, 0x70, 0xF4, 0x16, 0x28, 0xB5, 0x2F, 0xFD, 0x24, 0x1B, 0x35, 0x00,
        0x00, 0x00, 0x01, 0x00, 0x1E, 0x4E, 0x20, 0x3F, 0x0D, 0x76, 0xA0
    ];

    private static readonly Dictionary<string, Handler> CompressionDictionaryHandlers = new(StringComparer.Ordinal)
    {
        // clear-site-data.py: a Clear-Site-Data header with the directive of the query
        [CompressionDictionaryResources + "clear-site-data.py"] = (request, response, _) =>
        {
            response.Set("Clear-Site-Data", "\"" + RequiredParameter(request, "directive") + "\"");
            response.Content = "OK";
        },
        [CompressionDictionaryResources + "compressed-data.py"] = CompressedData,
        [CompressionDictionaryResources + "compressed-data.h2.py"] = CompressedData,
        [CompressionDictionaryResources + "echo-headers.py"] = CompressionDictionaryEchoHeaders,
        [CompressionDictionaryResources + "echo-headers2.py"] = CompressionDictionaryEchoHeaders,
        [CompressionDictionaryResources + "register-dictionary.py"] = RegisterDictionary,
        [CompressionDictionaryResources + "register-dictionary.h2.py"] = RegisterDictionary,

        // register-dictionary-network-error.py: a redirect to a URL that is not an HTTP(S) URL (a network error)
        [CompressionDictionaryResources + "register-dictionary-network-error.py"] = (_, response, _) =>
        {
            response.Status = 302;
            response.Set("Location", "about:blank");
        },
    };

    /// <summary>compressed-data.py: data compressed with the default dictionary (<c>This is a test dictionary.\n</c>).</summary>
    private static void CompressedData(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Add("Access-Control-Allow-Origin", "*");
        response.Add("Content-Type", "text/plain");
        response.Add("Vary", "available-dictionary,accept-encoding");
        if (request.Has("cacheable"))
        {
            response.Add("Cache-Control", "max-age=3600");
        }

        var useLarge = request.Has("large");
        byte[] content = [];
        if (request.Get("content_encoding") is { } contentEncoding)
        {
            response.Add("Content-Encoding", contentEncoding);
            content = contentEncoding switch
            {
                "dcb" => useLarge ? CompressedDataLargeDcbData.ToArray() : CompressedDataDcbData.ToArray(),
                "dcz" => useLarge ? CompressedDataLargeDczData.ToArray() : CompressedDataDczData.ToArray(),
                _ => [],
            };

            if (request.Has("hash_mismatch"))
            {
                var offset = contentEncoding == "dcb" ? 4 : 8;
                content = [.. content.AsSpan(0, Math.Min(offset, content.Length)), .. new byte[32], .. content.Length > offset + 32 ? content.AsSpan(offset + 32) : []];
            }
        }

        response.Body = content;
    }

    /// <summary>echo-headers.py and echo-headers2.py (handler_utils.create_echo_response): the request headers as JSON, or JSONP with a callback.</summary>
    private static void CompressionDictionaryEchoHeaders(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Access-Control-Allow-Origin", "*");
        var result = PythonRequestHeadersJsonUtf8(request);
        if (request.Get("callback") is { } callback)
        {
            result = DecodeUtf8(callback) + "(" + result + ");";
        }

        response.Body = Encoding.UTF8.GetBytes(result);
    }

    /// <summary><c>json.dumps()</c> of the request headers decoded as UTF-8 (<c>{name: value.decode("utf-8")}</c>), lowercase names.</summary>
    private static string PythonRequestHeadersJsonUtf8(WptHandlerRequest request)
        => "{" + string.Join(", ", request.Headers.Select(header => header.Key.ToLowerInvariant()).Distinct(StringComparer.Ordinal).Select(name => PythonJsonString(name) + ": " + PythonJsonString(DecodeUtf8(request.Header(name) ?? "")))) + "}";

    /// <summary>register-dictionary.py: a dictionary with a Use-As-Dictionary header, whose request headers can be saved in the stash.</summary>
    private static void RegisterDictionary(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Access-Control-Allow-Origin", "*");
        var match = request.Get("match") ?? "/fetch/compression-dictionary/resources/*";
        var content = request.Get("content") ?? "This is a test dictionary.\n";
        var maxAge = request.Get("max-age") ?? "3600";

        if (request.Get("save_header") is { } token)
        {
            stash.Put(token, PythonRequestHeadersJsonUtf8(request));
        }

        if (request.Get("get_previous_header") is { } previousToken)
        {
            response.Body = Encoding.UTF8.GetBytes(stash.Take(previousToken) is { } store ? "{\"headers\": " + store + "}" : "{}");
            return;
        }

        if (request.Get("use-as-dictionary") is { } useAsDictionary)
        {
            response.Set("Use-As-Dictionary", useAsDictionary);
        }
        else
        {
            var options = "match=\"" + match + "\"";
            if (request.Get("id") is { } id)
            {
                options += ", id=\"" + id + "\"";
            }

            if (request.Get("type") is { } type)
            {
                options += ", type=" + type;
            }

            if (request.Get("match-dest") is { } matchDest)
            {
                options += ", match-dest=" + matchDest;
            }

            if (request.Get("extra-header-option") is { } extraOption)
            {
                options += ", " + extraOption;
            }

            response.Set("Use-As-Dictionary", options);
        }

        response.Set("Cache-Control", request.Get("cache-control") ?? "max-age=" + maxAge);
        if (request.Get("age") is { } age)
        {
            response.Set("Age", age);
        }

        response.Set("Vary", "available-dictionary,accept-encoding");

        // The bodies of the content codings that are not the ones of the dictionaries are decoded by the network layer
        response.Body = Encoding.Latin1.GetBytes(content);
        if (request.Get("content_encoding") is { } contentEncoding)
        {
            response.Set("Content-Encoding", contentEncoding);
            byte[]? encoded = contentEncoding switch
            {
                "gzip" => RegisterDictionaryGzipData.ToArray(),
                "br" => RegisterDictionaryBrData.ToArray(),
                "zstd" => RegisterDictionaryZstdData.ToArray(),
                "dcb" => RegisterDictionaryDcbData.ToArray(),
                "dcz" => RegisterDictionaryDczData.ToArray(),
                _ => null,
            };

            if (encoded is not null)
            {
                response.Body = contentEncoding is "dcb" or "dcz" ? encoded : WptPipes.Decode(encoded, contentEncoding) ?? encoded;
                response.Set("Content-Length", encoded.Length.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
