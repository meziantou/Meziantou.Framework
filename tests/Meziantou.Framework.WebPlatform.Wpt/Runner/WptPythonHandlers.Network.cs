using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The handlers of the <c>fetch</c> and <c>xhr</c> tests that check the network layer: the raw HTTP messages written by the
/// handlers (<see cref="WptHandlerResponse.RawMessage"/>), the bodies sent with a content coding, the chunked bodies, the
/// connections, the ranges and the cookies of the responses.
/// </summary>
internal static partial class WptPythonHandlers
{
    private static readonly Dictionary<string, Handler> NetworkHandlers = new(StringComparer.Ordinal)
    {
        // Each hex-encoded chunk parameter is a chunk of a chunked body, after the delay
        ["/xhr/resources/content-in-chunks.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", request.Get("type") ?? "text/plain");
            response.Streamed = true;
            var delay = Milliseconds(request.Get("ms"), 100);
            foreach (var chunk in request.GetParameters.Where(parameter => parameter.Key == "chunk"))
            {
                response.Chunks.Add((delay, Convert.FromHexString(chunk.Value)));
            }
        },
        ["/fetch/content-length/resources/content-length.py"] = (request, response, _) =>
            response.RawMessage = [.. "HTTP/1.1 200 OK\r\nContent-Type: text/plain;charset=UTF-8\r\nConnection: close\r\n"u8, .. Encoding.Latin1.GetBytes(RequiredParameter(request, "length")), .. "\r\n\r\nFact: this is really forty-two bytes long."u8],
        ["/fetch/h1-parsing/resources/status-code.py"] = (request, response, _) =>
            response.RawMessage = [.. "HTTP/1.1 "u8, .. Encoding.Latin1.GetBytes(RequiredParameter(request, "input")), .. "\nheader-parsing: is sad\n"u8],
        ["/fetch/h1-parsing/resources/message.py"] = (request, response, _) => response.RawMessage = Encoding.Latin1.GetBytes(RequiredParameter(request, "message")),
        ["/fetch/nosniff/resources/nosniff.py"] = (request, response, _) =>
            response.RawMessage = [.. "HTTP/1.1 220 YOU HAVE NO POWER HERE\r\nContent-Length: 22\r\nConnection: close\r\nContent-Type: x/x\r\n"u8, .. Encoding.Latin1.GetBytes(RequiredParameter(request, "nosniff")), .. "\r\n\r\n// nothing to see here"u8],

        // The status line and the headers written by the handler (add_required_headers = False)
        ["/fetch/corb/resources/sniffable-resource.py"] = (request, response, _) =>
        {
            var body = RequiredParameter(request, "body");
            response.RawMessage = Encoding.Latin1.GetBytes("HTTP/1.1 200 OK\r\ncontent-length: " + body.Length.ToString(CultureInfo.InvariantCulture) + "\r\ncontent-type: " + RequiredParameter(request, "type") + "\r\n\r\n" + body);
        },
        ["/fetch/redirect-navigate/302-found-post-handler.py"] = (request, response, _) =>
        {
            if (request.Method == "POST")
            {
                response.RawMessage = Encoding.Latin1.GetBytes("HTTP/1.1 302 Found\r\nLocation: " + request.Url.Href + "\r\n\r\n");
            }
            else
            {
                response.Set("Content-Type", "text/plain");
                response.Content = request.Method == "GET" ? "OK" : "FAIL";
            }
        },

        // A body that is not encoded with its content coding
        ["/fetch/content-encoding/gzip/resources/bad-gzip-body.py"] = (_, response, _) => ContentEncodedResponse(response, "gzip", "not actually gzip"u8.ToArray()),
        ["/fetch/content-encoding/br/resources/bad-br-body.py"] = (_, response, _) => ContentEncodedResponse(response, "br", "not actually br"u8.ToArray()),
        ["/fetch/content-encoding/zstd/resources/bad-zstd-body.py"] = (_, response, _) => ContentEncodedResponse(response, "zstd", "not actually zstd"u8.ToArray()),

        ["/fetch/connection-pool/resources/network-partition-key.py"] = NetworkPartitionKey,
        ["/fetch/range/resources/partial-script.py"] = PartialScript,
        ["/fetch/range/resources/long-wav.py"] = LongWav,
        ["/fetch/range/resources/stash-take.py"] = (request, response, stash) =>
        {
            response.Set("Content-Type", "application/json");
            response.Content = stash.TakeValue(RequiredParameter(request, "key"), "/fetch/range/") is string value ? PythonJsonString(value) : "null";
        },

        // The hexadecimal bytes of the headers of the request
        ["/fetch/api/resources/echo-header-bytes.py"] = (request, response, _) =>
        {
            response.Set("content-type", "text/plain");
            response.Content = string.Join("\n", RequiredParameter(request, "headers").Split('|').Select(name => name + ": " + Convert.ToHexStringLower(Encoding.Latin1.GetBytes(request.Header(name) ?? ""))));
        },
        ["/fetch/content-disposition/resources/multiple-content-disposition.h2.py"] = (request, response, _) =>
        {
            response.Set("content-type", "text/plain");
            foreach (var value in request.GetParameters.Where(parameter => parameter.Key == "value"))
            {
                response.Add("content-disposition", value.Value);
            }

            response.Content = "Test.\n";
        },

        // The Origin headers of the requests of a redirect chain
        ["/fetch/origin/resources/redirect-and-stash.py"] = (request, response, stash) =>
        {
            var key = RequiredParameter(request, "stash");
            var origin = request.Header("origin") ?? "no Origin header";
            var origins = stash.TakeValue(key) as List<string>;
            if (request.Has("dump"))
            {
                response.Set("Content-Type", "application/json");
                response.Content = origins is null ? "null" : "[" + string.Join(", ", origins.Select(PythonJsonString)) + "]";
                return;
            }

            origins ??= [];
            origins.Add(origin);
            stash.Put(key, origins);
            if (request.Get("location") is { } location)
            {
                response.Status = 308;
                response.Set("Location", request.Has("dummyJS") ? location + "&dummyJS" : location);
                return;
            }

            response.Set("Content-Type", "text/html");
            response.Set("Access-Control-Allow-Origin", "*");
            response.Content = request.Has("dummyJS") ? "console.log('dummy JS')" : "<meta charset=utf-8>\n<body><script>parent.postMessage('loaded','*')</script></body>";
        },

        // The request of a ping (POST), then its data (GET)
        ["/fetch/orb/resources/ping.py"] = (request, response, stash) =>
        {
            var key = RequiredParameter(request, "id");
            if (request.Method == "POST")
            {
                stash.Put(key, "{\"method\": \"POST\", \"contentType\": " + PythonJsonString(request.Header("content-type") ?? "") + ", \"body\": " + PythonJsonString(Encoding.Latin1.GetString(request.Body)) + ", \"pingTo\": " + PythonJsonString(request.Header("ping-to") ?? "") + "}");
                var status = int.Parse(RequiredParameter(request, "status"), CultureInfo.InvariantCulture);
                response.Status = status;
                response.Add("Content-Type", RequiredParameter(request, "type"));
                response.Add("X-Content-Type-Options", "nosniff");
                response.Add("Cache-Control", "no-store");
                response.Content = status != 204 ? "{\"ping\": true}" : "";
                return;
            }

            response.Add("Content-Type", "application/json");
            response.Add("Cache-Control", "no-store");
            response.Content = stash.TakeValue(key) as string ?? "null";
        },

        // The cookies of the request, and a session cookie and a secure cookie (or their removal)
        ["/xhr/resources/get-set-cookie.py"] = (request, response, _) =>
        {
            response.Set("Content-type", "text/plain");
            var expires = request.Get("clear") is { Length: > 0 } ? TimeSpan.FromDays(-1) : (TimeSpan?)null;
            SetResponseCookie(request, response, "WK-test", "1", expires);
            SetResponseCookie(request, response, "WK-test-secure", "1", expires, secure: true);
            response.Content = string.Concat(ParseRequestCookies(request).Select(cookie => " " + cookie.Key + "=" + cookie.Value));
        },
        ["/xhr/resources/access-control-cookie.py"] = (request, response, _) =>
        {
            response.Set("Cache-Control", "no-store");
            response.Set("Access-Control-Allow-Origin", request.Header("origin"));
            response.Set("Access-Control-Allow-Credentials", "true");
            foreach (var cookie in ParseRequestCookies(request))
            {
                SetResponseCookie(request, response, cookie.Key, "deleted", TimeSpan.FromDays(-1));
            }

            if (request.Get("cookie_name") is { Length: > 0 } cookieName)
            {
                SetResponseCookie(request, response, cookieName, "COOKIE", TimeSpan.FromDays(1));
            }
        },

        // The methods of the requests of an id (a preflight and the request), then the list (read)
        ["/xhr/resources/cors-upload-listener-timing.py"] = (request, response, stash) =>
        {
            response.Set("Cache-Control", "no-store");
            var key = RequiredParameter(request, "id");
            if (request.Has("read"))
            {
                response.Set("Access-Control-Allow-Origin", "*");
                response.Set("Content-Type", "application/json");
                response.Content = "[" + string.Join(", ", (stash.TakeValue(key) as List<string> ?? []).Select(PythonJsonString)) + "]";
                return;
            }

            var methods = stash.TakeValue(key) as List<string> ?? [];
            methods.Add(request.Method);
            stash.Put(key, methods);
            if (RequiredParameter(request, "cors") == "allow")
            {
                response.Set("Access-Control-Allow-Origin", "*");
                response.Set("Access-Control-Allow-Methods", "POST");
                response.Set("Access-Control-Max-Age", "0");
            }

            response.Set("Content-Type", "text/plain");
            response.Body = request.Method == "OPTIONS" ? [] : request.Body;
        },
    };

    /// <summary>A response whose body is sent encoded with a content coding (see <see cref="WptHandlerResponse.BodyIsContentEncoded"/>).</summary>
    private static void ContentEncodedResponse(WptHandlerResponse response, string contentEncoding, byte[] body)
    {
        response.Add("Content-Encoding", contentEncoding);
        response.Body = body;
        response.BodyIsContentEncoded = true;
    }

    /// <summary>
    /// <c>response.set_cookie(name, value, expires=timedelta, secure=...)</c> of wptserve: a cookie of the path <c>/</c>, as
    /// <c>http.cookies.Morsel.OutputString()</c> writes it.
    /// </summary>
    private static void SetResponseCookie(WptHandlerRequest request, WptHandlerResponse response, string name, string value, TimeSpan? expires, bool secure = false)
    {
        var cookie = new StringBuilder(name).Append('=').Append(value);
        if (expires is { } delta)
        {
            cookie.Append("; expires=").Append((request.Now + delta).ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture));
        }

        cookie.Append("; Path=/");
        if (secure)
        {
            cookie.Append("; Secure");
        }

        response.Add("Set-Cookie", cookie.ToString());
    }

    /// <summary>The state of fetch/connection-pool/resources/network-partition-key.py for a uuid.</summary>
    private sealed class NetworkPartitionState
    {
        public bool TestFailed { get; set; }

        public int RequestCount { get; set; }

        public int ConnectionCount { get; set; }

        /// <summary>The last partition id of each connection.</summary>
        public Dictionary<string, string> Connections { get; } = new(StringComparer.Ordinal);
    }

    // fetch/connection-pool/resources/network-partition-key.py: checks that the requests of a connection have the same
    // partition id, and counts the requests and the connections. The runner has no sockets and no connection pool: each
    // request is sent on a new connection (the client address of the request is unique)
    private static void NetworkPartitionKey(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Cache-Control", "no-store");
        var dispatch = request.Get("dispatch");
        var uuid = request.Get("uuid");
        var partitionId = request.Get("partition_id");
        if (string.IsNullOrEmpty(uuid) || string.IsNullOrEmpty(dispatch) || string.IsNullOrEmpty(partitionId))
        {
            SimpleResponse(404, "Not found", "Invalid query parameters");
            return;
        }

        var testFailed = false;
        var requestCount = 0;
        var connectionCount = 0;
        if (request.Get("nocheck_partition") != "True")
        {
            var state = stash.TakeValue(uuid) as NetworkPartitionState ?? new NetworkPartitionState();
            requestCount = ++state.RequestCount;
            var addressKey = Guid.NewGuid().ToString("N");
            if (state.Connections.TryGetValue(addressKey, out var previous))
            {
                if (previous != partitionId)
                {
                    state.TestFailed = true;
                }
            }
            else
            {
                connectionCount = ++state.ConnectionCount;
            }

            state.Connections[addressKey] = partitionId;
            testFailed = state.TestFailed;
            stash.Put(uuid, state);
        }

        if (request.Header("Origin") is { Length: > 0 } origin)
        {
            response.Set("Access-Control-Allow-Origin", origin);
            response.Set("Access-Control-Allow-Credentials", "true");
        }

        if (request.Method == "OPTIONS")
        {
            response.Status = 200;
            response.StatusText = "OK";
            response.Set("Access-Control-Allow-Methods", "GET");
            response.Set("Access-Control-Allow-Headers", "header-to-force-cors");
            response.Set("Access-Control-Max-Age", "86400");
            response.Content = "Preflight request";
            return;
        }

        switch (dispatch)
        {
            case "fetch_file":
                FetchFile();
                return;

            case "check_partition":
                var status = ParseInt(request.Get("status"), 200);
                if (testFailed)
                {
                    SimpleResponse(status, "OK", "Multiple partition IDs used on a socket");
                    return;
                }

                var body = "ok";
                if (request.Has("addcounter"))
                {
                    body += ". Request was sent " + requestCount.ToString(CultureInfo.InvariantCulture) + " times. " + connectionCount.ToString(CultureInfo.InvariantCulture) + " connections were created.";
                }

                SimpleResponse(status, "OK", body);
                return;

            case "clean_up":
                stash.TakeValue(uuid);
                SimpleResponse(200, "OK", testFailed ? "Test failed, but cleanup completed." : "cleanup complete");
                return;

            default:
                SimpleResponse(404, "Not Found", "Unrecognized dispatch parameter: " + dispatch);
                return;
        }

        void SimpleResponse(int status, string statusMessage, string body, string contentType = "text/plain")
        {
            response.Status = status;
            response.StatusText = statusMessage;
            response.Set("Content-Type", contentType);
            response.Content = body;
        }

        void FetchFile()
        {
            var subresourceOrigin = request.Get("subresource_origin");
            var relativePath = request.Get("path");
            var includeCredentials = request.Get("include_credentials");
            if (string.IsNullOrEmpty(subresourceOrigin) || string.IsNullOrEmpty(relativePath) || string.IsNullOrEmpty(includeCredentials))
            {
                SimpleResponse(404, "Not found", "Invalid query parameters");
                return;
            }

            var basePath = Path.GetFullPath(WptPaths.Wpt);
            var path = Path.GetFullPath(Path.Combine(basePath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(basePath, StringComparison.Ordinal))
            {
                SimpleResponse(404, "Not found", "Invalid path");
                return;
            }

            if (request.Get("sandbox") == "true")
            {
                response.Set("Content-Security-Policy", "sandbox allow-scripts");
            }

            var subresourceParameters = "?partition_id=" + partitionId + "&uuid=" + uuid + "&subresource_origin=" + subresourceOrigin + "&include_credentials=" + includeCredentials;
            var content = Encoding.Latin1.GetString(File.ReadAllBytes(path))
                .Replace("SUBRESOURCE_PREFIX:", subresourceOrigin + request.Path + subresourceParameters, StringComparison.Ordinal);
            if (request.Get("other_origin") is { Length: > 0 } otherOrigin)
            {
                content = content.Replace("OTHER_PREFIX:", otherOrigin + request.Path + subresourceParameters, StringComparison.Ordinal);
            }

            // mimetypes.guess_type()
            var mimeType = Path.GetExtension(path) switch
            {
                ".html" or ".htm" => "text/html",
                ".js" => "text/javascript",
                ".css" => "text/css",
                ".png" => "image/png",
                ".json" => "application/json",
                ".txt" => "text/plain",
                _ => null,
            };
            if (mimeType is null)
            {
                SimpleResponse(500, "Server Error", "Unknown MIME type");
                return;
            }

            SimpleResponse(200, "OK", content, mimeType);
        }
    }

    // fetch/range/resources/partial-script.py: a partial response (206 or 416) of a script or of a PNG image
    private static void PartialScript(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var pretendOffset = ParseInt(request.Get("pretend-offset"), 0);
        var contentType = request.Get("type") ?? "text/plain";
        if (request.Get("require-range") is { Length: > 0 } && request.Header("Range") is null or "")
        {
            response.Status = 412;
            response.StatusText = "Range header required";
            return;
        }

        // 1x1 red PNG image
        byte[] data = contentType == "image/png"
            ? Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAADAAEAGN2NtAAAAABJRU5ErkJggg==")
            : "self.scriptExecuted = true;"u8.ToArray();
        var length = data.Length;
        response.Set("Content-Type", contentType);
        response.Set("Accept-Ranges", "bytes");
        response.Set("Cache-Control", "no-cache");
        response.Set("Content-Length", length.ToString(CultureInfo.InvariantCulture));
        if (request.Get("range-not-satisfiable") is { Length: > 0 })
        {
            response.Status = 416;
            response.Set("Content-Range", "bytes */" + (pretendOffset + length).ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            response.Status = 206;
            response.Set("Content-Range", string.Create(CultureInfo.InvariantCulture, $"bytes {pretendOffset}-{pretendOffset + length - 1}/{pretendOffset + length}"));
        }

        response.Body = data;
    }

    // fetch/range/resources/long-wav.py: a silent wav of 5 minutes (2,400,000 bytes), which answers the range requests. The
    // handler sends a chunk of a second every 500 ms, until the client closes the connection: the chunks are sent over the
    // virtual clock, and the response never ends for the clients that do not read it until its end
    private static void LongWav(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        if (request.Method == "OPTIONS")
        {
            response.Status = 404;
            response.StatusText = "Not Found";
            response.Set("Content-Type", "text/plain");
            response.Content = "Preflight not accepted";
            return;
        }

        response.Set("Content-Type", "audio/wav");
        response.Set("Accept-Ranges", "bytes");
        response.Set("Cache-Control", "no-cache");
        response.Set("Access-Control-Allow-Origin", request.Header("Origin") ?? "");
        var rangeHeader = request.Header("Range") ?? "";
        var rangeMatch = rangeHeader.Length > 0 ? RangeHeaderRegex().Match(rangeHeader) : Match.Empty;
        if (request.Get("range-received-key") is { Length: > 0 } rangeReceivedKey && rangeHeader.Length > 0)
        {
            stash.TakeValue(rangeReceivedKey, "/fetch/range/");
            stash.Put(rangeReceivedKey, "range-header-received", "/fetch/range/");
        }

        if (request.Get("accept-encoding-key") is { Length: > 0 } acceptEncodingKey)
        {
            stash.TakeValue(acceptEncodingKey, "/fetch/range/");
            stash.Put(acceptEncodingKey, request.Header("Accept-Encoding") ?? "", "/fetch/range/");
        }

        const int SampleRate = 8000;
        const int Duration = 60 * 5;
        const int TotalLength = SampleRate * Duration;
        var header = CreateWavHeader(SampleRate, Duration);
        var remaining = TotalLength;
        byte[] initialWrite;
        if (rangeMatch.Success)
        {
            response.Status = 206;
            var start = int.Parse(rangeMatch.Groups["start"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture);
            var end = rangeMatch.Groups["end"].Length > 0 ? int.Parse(rangeMatch.Groups["end"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture) : 0;
            remaining = end != 0 ? end + 1 - start : TotalLength - start;
            initialWrite = start < header.Length ? header[start..] : [];
            if (remaining < initialWrite.Length)
            {
                initialWrite = initialWrite[..remaining];
            }

            response.Set("Content-Range", string.Create(CultureInfo.InvariantCulture, $"bytes {start}-{(end != 0 ? end : TotalLength - 1)}/{TotalLength}"));
        }
        else
        {
            initialWrite = header;
        }

        response.Set("Content-Length", remaining.ToString(CultureInfo.InvariantCulture));
        response.Streamed = true;
        response.Body = initialWrite;
        remaining -= initialWrite.Length;
        var delay = TimeSpan.Zero;
        while (remaining > 0)
        {
            var size = Math.Min(remaining, SampleRate);
            remaining -= size;
            response.Chunks.Add((delay, new byte[size]));
            delay = TimeSpan.FromMilliseconds(500);
        }

        static byte[] CreateWavHeader(int sampleRate, int duration)
        {
            // 8 bits, 1 channel
            var dataSize = duration * sampleRate;
            var data = new byte[44];
            "RIFF"u8.CopyTo(data);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)(36 + dataSize));
            "WAVEfmt "u8.CopyTo(data.AsSpan(8));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 16);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(20), 1);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(22), 1);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24), (uint)sampleRate);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28), (uint)sampleRate);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(32), 1);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(34), 8);
            "data"u8.CopyTo(data.AsSpan(36));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(40), (uint)dataSize);
            return data;
        }
    }

    [GeneratedRegex(@"^bytes=(?<start>\d*)-(?<end>\d*)$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RangeHeaderRegex();
}
