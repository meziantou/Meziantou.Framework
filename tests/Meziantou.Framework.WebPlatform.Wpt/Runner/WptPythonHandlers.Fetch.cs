using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The handlers of the <c>xhr</c> and <c>fetch/api</c> tests.</summary>
internal static partial class WptPythonHandlers
{
    private static readonly Dictionary<string, Handler> FetchHandlers = new(StringComparer.Ordinal)
    {
        // fetch-later/resources/set_beacon.py: records the payload of a deferred request (the "payload" field of a
        // multipart/form-data body, else the body, without its "payload=" prefix) in the list of the uuid, and checks the
        // Origin header, the CORS-preflight request and the credentials that the test expects
        ["/fetch/fetch-later/resources/set_beacon.py"] = (request, response, stash) =>
        {
            if (request.Get("uuid") is not { } uuid)
            {
                response.Status = 400;
                response.Content = "Must provide a UUID to store beacon data";
                return;
            }

            var expectedOrigin = request.Get("expectOrigin");
            if (request.Header("Origin") is { } origin)
            {
                if (!string.IsNullOrEmpty(expectedOrigin) && origin != expectedOrigin)
                    throw new InvalidOperationException($"expected {expectedOrigin}, got {origin}");

                response.Set("Access-Control-Allow-Origin", origin);
            }
            else if (expectedOrigin is not null)
            {
                throw new InvalidOperationException($"expected None, got {expectedOrigin}");
            }

            if (request.Method == "OPTIONS")
            {
                if (request.Get("expectPreflight") != "true")
                    throw new InvalidOperationException("Preflight not expected.");

                if (request.HasHeader("Cookie"))
                    throw new InvalidOperationException("The preflight request has cookies");

                var requestedHeaders = request.Header("Access-Control-Request-Headers") ?? "";
                if (!requestedHeaders.Contains("content-type", StringComparison.Ordinal))
                    throw new InvalidOperationException($"expected content-type, got {requestedHeaders}");

                response.Set("Access-Control-Allow-Headers", "content-type");
                if (request.Header("Access-Control-Request-Method") is var requestedMethod && requestedMethod != "POST")
                    throw new InvalidOperationException($"expected POST, got {requestedMethod}");

                response.Set("Access-Control-Allow-Methods", "POST");
                return;
            }

            if ((request.Get("expectCredentials") == "true") != request.HasHeader("Cookie"))
                throw new InvalidOperationException("The credentials of the request are not the expected ones");

            string? data = null;
            if (request.Method == "POST")
            {
                if ((request.Header("Content-Type") ?? "").Contains("multipart/form-data", StringComparison.Ordinal))
                {
                    data = GetPost(request, "payload");
                }
                else if (request.Body.Length > 0)
                {
                    data = Encoding.UTF8.GetString(request.Body);
                    if (data.StartsWith("payload=", StringComparison.Ordinal))
                    {
                        data = data.Split("payload=")[1];
                    }
                }
            }

            lock (stash)
            {
                var saved = stash.TakeValue(uuid, "beacon_data") as List<string?> ?? [];
                saved.Add(data);
                stash.Put(uuid, saved, "beacon_data");
            }

            response.Status = 200;
        },

        // fetch-later/resources/get_beacon.py: the payloads recorded for the uuid, as {"data": [...]}
        ["/fetch/fetch-later/resources/get_beacon.py"] = (request, response, stash) =>
        {
            if (request.Get("uuid") is not { } uuid)
            {
                response.Status = 400;
                response.Content = "Must provide a UUID to store beacon data";
                return;
            }

            string json;
            lock (stash)
            {
                var saved = stash.TakeValue(uuid, "beacon_data") as List<string?>;
                if (saved is not null)
                {
                    stash.Put(uuid, saved, "beacon_data");
                }

                json = "{\"data\": [" + string.Join(", ", (saved ?? []).Select(item => item is null ? "null" : PythonJsonString(item))) + "]}";
            }

            TextResponse(response, json);
        },

        // xhr/resources
        ["/xhr/resources/content.py"] = Content,
        ["/xhr/resources/headers.py"] = XhrHeaders,
        ["/xhr/resources/redirect.py"] = XhrRedirect,
        ["/xhr/resources/inspect-headers.py"] = XhrInspectHeaders,
        ["/xhr/resources/status.py"] = Status,
        ["/xhr/resources/trickle.py"] = XhrTrickle,
        ["/xhr/resources/delay.py"] = Delay,
        ["/xhr/resources/corsenabled.py"] = CorsEnabled,
        ["/xhr/resources/reset-token.py"] = ResetToken,
        ["/xhr/resources/authentication.py"] = XhrAuthentication,
        ["/xhr/resources/auth1/auth.py"] = XhrAuthentication,
        ["/xhr/resources/auth3/auth.py"] = XhrAuthentication,
        ["/xhr/resources/auth4/auth.py"] = XhrAuthentication,
        ["/xhr/resources/auth5/auth.py"] = XhrAuthentication,
        ["/xhr/resources/auth6/auth.py"] = XhrAuthentication,
        ["/xhr/resources/auth9/auth.py"] = XhrAuthentication,
        ["/xhr/resources/auth10/auth.py"] = XhrAuthentication,
        ["/xhr/resources/auth11/auth.py"] = XhrAuthentication,
        ["/xhr/resources/auth2/auth.py"] = XhrAuthentication,
        ["/xhr/resources/auth2/corsenabled.py"] = (request, response, stash) => CorsEnabledAuthentication(request, response, stash, "authorization, x-user, x-pass"),
        ["/xhr/resources/auth7/corsenabled.py"] = (request, response, stash) => CorsEnabledAuthentication(request, response, stash, "authorization, x-user, x-pass"),
        ["/xhr/resources/auth8/corsenabled-no-authorize.py"] = (request, response, stash) => CorsEnabledAuthentication(request, response, stash, "x-user, x-pass"),
        ["/xhr/resources/access-control-allow-lists.py"] = AccessControlAllowLists,
        ["/xhr/resources/access-control-allow-with-body.py"] = AccessControlAllowWithBody,
        ["/xhr/resources/access-control-auth-basic.py"] = AccessControlAuthBasic,
        ["/xhr/resources/access-control-basic-allow-no-credentials.py"] = (request, response, _) => AllowOrigin(response, request.Header("origin"), credentials: false),
        ["/xhr/resources/access-control-basic-allow-star.py"] = (request, response, _) => AllowOrigin(response, "*", credentials: false),
        ["/xhr/resources/access-control-basic-allow.py"] = (request, response, _) => AllowOrigin(response, request.Header("origin"), credentials: true),
        ["/xhr/resources/access-control-basic-cors-safelisted-request-headers.py"] = AccessControlSafelistedRequestHeaders,
        ["/xhr/resources/access-control-basic-cors-safelisted-response-headers.py"] = AccessControlSafelistedResponseHeaders,
        ["/xhr/resources/access-control-basic-denied.py"] = AccessControlDenied,
        ["/xhr/resources/access-control-basic-options-not-supported.py"] = AccessControlOptionsNotSupported,
        ["/xhr/resources/access-control-basic-preflight-cache-invalidation.py"] = AccessControlPreflightCacheInvalidation,
        ["/xhr/resources/access-control-basic-preflight-cache-timeout.py"] = AccessControlPreflightCacheTimeout,
        ["/xhr/resources/access-control-basic-preflight-cache.py"] = AccessControlPreflightCache,
        ["/xhr/resources/access-control-basic-put-allow.py"] = AccessControlPutAllow,
        ["/xhr/resources/access-control-origin-header.py"] = AccessControlOriginHeader,
        ["/xhr/resources/access-control-preflight-denied.py"] = AccessControlPreflightDenied,
        ["/xhr/resources/access-control-preflight-request-allow-headers-returns-star.py"] = AccessControlPreflightAllowHeadersStar,
        ["/xhr/resources/access-control-preflight-request-header-lowercase.py"] = AccessControlPreflightHeaderLowercase,
        ["/xhr/resources/access-control-preflight-request-header-returns-origin.py"] = AccessControlPreflightHeaderReturnsOrigin,
        ["/xhr/resources/access-control-preflight-request-header-sorted.py"] = AccessControlPreflightHeaderSorted,
        ["/xhr/resources/access-control-preflight-request-headers-origin.py"] = AccessControlPreflightHeadersOrigin,
        ["/xhr/resources/access-control-preflight-request-invalid-status.py"] = AccessControlPreflightInvalidStatus,
        ["/xhr/resources/access-control-preflight-request-must-not-contain-cookie.py"] = AccessControlPreflightNoCookie,
        ["/xhr/resources/accept-language.py"] = (request, response, _) => TextResponse(response, request.Header("Accept-Language") ?? "NO"),
        ["/xhr/resources/accept.py"] = (request, response, _) => TextResponse(response, request.Header("Accept") ?? "NO"),
        ["/xhr/resources/chunked.py"] = (_, response, _) =>
        {
            TextResponse(response, "First chunk\r\nSecond chunk\r\nYet another (third) chunk\r\nYet another (fourth) chunk\r\n");
            response.Set("Trailer", "X-Test-Me");
            response.Streamed = true;
        },
        ["/xhr/resources/echo-headers.py"] = (request, response, _) =>
        {
            // str(request.raw_headers): the header lines of the request
            TextResponse(response, string.Concat(request.Headers.Select(header => header.Key + ": " + header.Value + "\r\n")) + "\r\n");
            response.Set("Connection", "close");
        },
        ["/xhr/resources/conditional.py"] = Conditional,
        ["/xhr/resources/echo-content-cors.py"] = EchoContentCors,
        ["/xhr/resources/echo-content-type.py"] = (request, response, _) => TextResponse(response, request.Header("Content-Type") ?? ""),
        ["/xhr/resources/echo-method.py"] = (request, response, _) => TextResponse(response, request.Method),
        ["/xhr/resources/empty-div-utf8-html.py"] = (_, response, _) => Respond(response, "text/html;charset=utf-8", "<!DOCTYPE html><div></div>"u8.ToArray()),
        ["/xhr/resources/form.py"] = (request, response, _) => response.Body = Encoding.UTF8.GetBytes("id:" + GetPost(request, "id") + ";value:" + GetPost(request, "value") + ";"),
        ["/xhr/resources/gzip.py"] = (request, response, _) => Compressed(request, response, "gzip"),
        ["/xhr/resources/zlib.py"] = (request, response, _) => Compressed(request, response, "deflate"),
        ["/xhr/resources/header-user-agent.py"] = HeaderUserAgent,
        ["/xhr/resources/img-utf8-html.py"] = (_, response, _) => Respond(response, "text/html;charset=utf-8", "<img>foo"u8.ToArray()),
        ["/xhr/resources/infinite-redirects.py"] = InfiniteRedirects,
        ["/xhr/resources/invalid-utf8-html.py"] = (_, response, _) => Respond(response, "text/html;charset=utf-8", [0xFF]),
        ["/xhr/resources/invalid-utf8.py"] = (_, response, _) => Respond(response, "application/json", [.. "{\"key\":\""u8, 0xFF, .. "\"}"u8]),
        ["/xhr/resources/json-with-bom.py"] = (_, response, _) => Respond(response, "application/json", [0xEF, 0xBB, 0xBF, .. "{\"key\":\"value\"}"u8]),
        ["/xhr/resources/last-modified.py"] = LastModified,
        ["/xhr/resources/no-custom-header-on-preflight.py"] = NoCustomHeaderOnPreflight,
        ["/xhr/resources/parse-headers.py"] = (request, response, _) => response.Set("My-Custom-Header", request.Get("my-custom-header")),
        ["/xhr/resources/redirect-cors.py"] = RedirectCors,
        ["/xhr/resources/requri.py"] = (request, response, _) => response.Content = request.Has("full") ? request.Url.Href : request.Url.Pathname + request.Url.Search,
        ["/xhr/resources/shift-jis-html.py"] = (_, response, _) => Respond(response, "text/html;charset=shift-jis", [0x83, 0x65, 0x83, 0x58, 0x83, 0x67]),
        ["/xhr/resources/upload.py"] = Upload,
        ["/xhr/resources/win-1252-html.py"] = (_, response, _) => Respond(response, "text/html;charset=windows-1252", "\U000000FF"u8.ToArray()),
        ["/xhr/resources/win-1252-xml.py"] = (_, response, _) => Respond(response, "application/xml;charset=windows-1252", [(byte)'<', 0xFF, (byte)'/', (byte)'>']),
        ["/xhr/resources/bad-chunk-encoding.py"] = (_, response, _) => response.NetworkError = true,

        // fetch/api/resources
        ["/fetch/api/resources/redirect.py"] = FetchRedirect,
        ["/fetch/nosniff/resources/js.py"] = NosniffJs,
        ["/fetch/nosniff/resources/worker.py"] = NosniffWorker,
        ["/fetch/api/resources/inspect-headers.py"] = FetchInspectHeaders,
        ["/fetch/api/resources/status.py"] = Status,
        ["/fetch/api/resources/preflight.py"] = Preflight,
        ["/fetch/api/resources/trickle.py"] = FetchTrickle,
        ["/fetch/api/resources/clean-stash.py"] = (request, response, stash) => response.Content = stash.TakeValue(request.Get("token") ?? "") is not null ? "1" : "0",
        ["/fetch/api/resources/stash-put.py"] = StashPut,
        ["/fetch/api/resources/stash-take.py"] = StashTake,
        ["/fetch/api/resources/authentication.py"] = FetchAuthentication,
        ["/fetch/api/resources/cache.py"] = FetchCache,
        ["/fetch/api/request/resources/cache.py"] = RequestCache,
        ["/fetch/http-cache/resources/http-cache.py"] = FetchHttpCache,
        ["/fetch/http-cache/resources/cached_pragma_rand.py"] = (_, response, _) => CachedPragmaRand(response, "max-age=2592000, public"),
        ["/fetch/http-cache/resources/cached_pragma_immutable_rand.py"] = (_, response, _) => CachedPragmaRand(response, "max-age=2592000, immutable"),
        ["/fetch/api/resources/dump-authorization-header.py"] = DumpAuthorizationHeader,
        ["/fetch/api/resources/echo-content.py"] = EchoContent,
        ["/fetch/api/resources/method.py"] = Method,
        ["/fetch/api/resources/redirect.h2.py"] = (request, response, _) =>
        {
            response.Status = ParseInt(request.Get("redirect_status"), 302);
            response.Set("Location", request.Get("location"));
            response.Set("Content-Type", "text/plain");
        },
        ["/fetch/api/resources/redirect-empty-location.py"] = (_, response, _) =>
        {
            response.Status = 302;
            response.Add("Location", "");
        },
        ["/fetch/api/resources/script-with-header.py"] = (request, response, _) => Respond(response, request.Get("mime") ?? "", request.Get("content") == "empty" ? [] : "console.log('Script loaded')"u8.ToArray()),
        ["/fetch/api/resources/infinite-slow-response.py"] = InfiniteSlowResponse,
        ["/fetch/api/resources/stashed-chunk.py"] = (_, response, _) =>
        {
            response.Set("Content-Type", "application/octet-stream");
            response.Set("Cache-Control", "no-store");
            response.Body = [.. Enumerable.Repeat((byte)'A', 4096), .. Enumerable.Repeat((byte)'B', 4096), .. Enumerable.Repeat((byte)'C', 4096)];
            response.Delay = TimeSpan.FromSeconds(5.2);
        },
        ["/fetch/api/resources/preflight-unterminated-body.py"] = PreflightUnterminatedBody,
        ["/fetch/api/resources/bad-chunk-encoding.py"] = FetchBadChunkEncoding,
    };

    // xhr/resources/content.py
    private static void Content(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var charset = request.Has("response_charset_label") ? ";charset=" + request.Get("response_charset_label") : "";
        response.Add("Content-type", "text/plain" + charset);
        AddRequestInfo(request, response);
        response.Body = request.Has("content") ? Encoding.Latin1.GetBytes(request.Get("content")!) : request.Body;
    }

    private static void AddRequestInfo(WptHandlerRequest request, WptHandlerResponse response)
    {
        response.Add("X-Request-Method", request.Method);
        response.Add("X-Request-Query", request.Query.Length > 0 ? request.Query : "NO");
        response.Add("X-Request-Content-Length", request.Header("Content-Length") ?? "NO");
        response.Add("X-Request-Content-Type", request.Header("Content-Type") ?? "NO");
    }

    // xhr/resources/headers.py
    private static void XhrHeaders(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Content-Type", "text/plain");
        response.Set("X-Custom-Header", "test");
        response.Set("Set-Cookie", "test");
        response.Set("Set-Cookie2", "test");
        response.Set("X-Custom-Header-Empty", "");
        response.Set("X-Custom-Header-Comma", "1");
        response.Add("X-Custom-Header-Comma", "2");
        response.Set("X-Custom-Header-Bytes", Encoding.Latin1.GetString("\U00002026"u8));
        response.Content = "TEST";
    }

    // xhr/resources/redirect.py
    private static void XhrRedirect(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var code = ParseInt(request.Get("code"), 302);
        var location = request.Get("location", request.Path + "?followed");
        if (!string.IsNullOrEmpty(location))
        {
            // parse_qs decodes the location once more
            location = Encoding.UTF8.GetString(PercentEncoding.PercentDecode(Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(location)).Replace('+', ' ')));
            if (location.StartsWith("redirect.py", StringComparison.Ordinal))
            {
                location += "&code=" + code.ToString(CultureInfo.InvariantCulture);
            }
        }

        if (request.Has("delay"))
        {
            response.Delay = Milliseconds(request.Get("delay"), 0);
        }

        if (request.Has("followed"))
        {
            // The header "Content:Type: text/plain" is parsed as the header "Content"
            response.Add("Content", "Type: text/plain");
            response.Content = "MAGIC HAPPENED";
            return;
        }

        response.Status = code;
        response.StatusText = "WEBSRT MARKETING";
        response.Add("Location", location is null ? null : Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(location)));
        response.Content = "TEST";
    }

    // xhr/resources/inspect-headers.py
    private static void XhrInspectHeaders(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        if (request.Has("cors"))
        {
            response.Add("Access-Control-Allow-Origin", "*");
            response.Add("Access-Control-Allow-Credentials", "true");
            response.Add("Access-Control-Allow-Methods", "GET, POST, PUT, FOO");
            response.Add("Access-Control-Allow-Headers", "x-test, x-foo");
            response.Add("Access-Control-Expose-Headers", "x-request-method, x-request-content-type, x-request-query, x-request-content-length");
        }

        response.Add("content-type", "text/plain");
        var filterValue = request.Get("filter_value", "");
        var filterName = (request.Get("filter_name", "") ?? "").ToLowerInvariant();
        var result = new StringBuilder();
        foreach (var (name, value) in request.Headers)
        {
            if (!string.IsNullOrEmpty(filterValue))
            {
                if (value == filterValue)
                {
                    result.Append(name).Append(',');
                }
            }
            else if (name.ToLowerInvariant() == filterName)
            {
                result.Append(name).Append(": ").Append(value).Append('\n');
            }
        }

        response.Content = result.ToString();
    }

    // xhr/resources/status.py, fetch/api/resources/status.py
    private static void Status(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Status = ParseInt(request.Get("code"), 200);
        response.StatusText = request.Get("text", "OMG");
        response.Add("Content-Type", request.Get("type", ""));
        response.Add("X-Request-Method", request.Method);
        response.Content = request.Get("content", "")!;
    }

    // xhr/resources/trickle.py: the chunks are sent at once, after the delays
    private static void XhrTrickle(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var chunk = "TEST_TRICKLE\n";
        var delay = Milliseconds(request.Get("ms"), 500);
        var count = ParseInt(request.Get("count"), 50);
        if (request.Has("specifylength"))
        {
            response.Set("Content-Length", (count * chunk.Length).ToString(CultureInfo.InvariantCulture));
        }

        response.Set("Content-type", "text/plain");
        response.Content = string.Concat(Enumerable.Repeat(chunk, count));
        response.Streamed = true;
        response.Delay = delay * (count + 2);
    }

    // fetch/api/resources/trickle.py
    private static void FetchTrickle(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var delay = Milliseconds(request.Get("ms"), 500);
        var count = ParseInt(request.Get("count"), 50);
        if (!request.Has("notype"))
        {
            response.Set("Content-type", "text/plain");
        }

        // The headers after the delay, then each chunk after the delay, and the end of the body after the last delay
        response.Streamed = true;
        response.Delay = delay;
        for (var i = 0; i < count; i++)
        {
            response.Chunks.Add((delay, "TEST_TRICKLE\n"u8.ToArray()));
        }

        response.Chunks.Add((delay, []));
    }

    // fetch/api/resources/bad-chunk-encoding.py: the headers after the delay, then count valid chunks (each after the delay),
    // then an invalid chunk
    private static void FetchBadChunkEncoding(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var delay = Milliseconds(request.Get("ms"), 1000);
        var count = ParseInt(request.Get("count"), 50);
        response.Streamed = true;
        response.Delay = delay;
        for (var i = 0; i < count; i++)
        {
            response.Chunks.Add((delay, "TEST_CHUNK"u8.ToArray()));
        }

        response.Chunks.Add((delay, []));
        response.ChunksError = true;
    }

    // fetch/nosniff/resources/js.py: a script with X-Content-Type-Options: nosniff and the type parameter as its Content-Type
    private static void NosniffJs(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var type = request.Get("type");
        response.Content = "// nothing to see here\n" + (request.Get("outcome", "f") == "f" ? "log('FAIL: " + (type ?? "Content-Type missing") + "')" : "p()");
        response.Set("x-content-type-options", "nosniff");
        if (type is not null)
        {
            response.Set("content-type", type);
        }
    }

    // fetch/nosniff/resources/worker.py: a worker script with X-Content-Type-Options: nosniff and the type parameter as its Content-Type
    private static void NosniffWorker(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Content = "// nothing to see here\nthis.postMessage('hi')";
        response.Set("x-content-type-options", "nosniff");
        if (request.Get("type") is { } type)
        {
            response.Set("content-type", type);
        }
    }

    // xhr/resources/delay.py
    private static void Delay(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Delay = Milliseconds(request.Get("ms"), 500);
        response.Add("Access-Control-Allow-Origin", "*");
        response.Add("Access-Control-Allow-Methods", "YO");
        response.Add("Content-type", "text/plain");
        response.Content = "TEST_DELAY";
    }

    // xhr/resources/corsenabled.py
    private static void CorsEnabled(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Add("Access-Control-Allow-Origin", "*");
        response.Add("Access-Control-Allow-Credentials", "true");
        response.Add("Access-Control-Allow-Methods", "GET, POST, PUT, FOO");
        response.Add("Access-Control-Allow-Headers", "x-test, x-foo");
        response.Add("Access-Control-Expose-Headers", "x-request-method, x-request-content-type, x-request-query, x-request-content-length, x-request-data");
        if (request.Has("delay"))
        {
            response.Delay = TimeSpan.FromSeconds(ParseInt(request.Get("delay"), 0));
        }

        if (request.Has("safelist_content_type"))
        {
            response.Add("Access-Control-Allow-Headers", "content-type");
        }

        AddRequestInfo(request, response);
        response.Add("X-Request-Data", Encoding.Latin1.GetString(request.Body));
        response.Content = "Test";
    }

    // xhr/resources/reset-token.py
    private static void ResetToken(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Access-Control-Allow-Origin", request.Header("origin"));
        stash.Put(request.Get("token") ?? "", "");
        response.Content = "PASS";
    }

    // xhr/resources/authentication.py (and the auth*/auth.py handlers that load it)
    private static void XhrAuthentication(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var (sessionUser, sessionPassword) = request.Auth;
        var token = request.Header("X-User");
        if (sessionUser is null && sessionPassword is null)
        {
            if (token is not null && stash.TakeValue(token) is not null)
            {
                response.Content = "FAIL (did not authorize)";
                return;
            }

            if (token is not null)
            {
                stash.Put(token, "1");
            }

            response.Status = 401;
            response.StatusText = "Unauthorized";
            response.Add("WWW-Authenticate", "Basic realm=\"test\"");
            response.Content = "FAIL (should be transparent)";
            return;
        }

        var challenge = token is not null && stash.Take(token) == "1" ? "DID" : "DID-NOT";
        response.Add("XHR-USER", token);
        response.Add("SES-USER", sessionUser);
        response.Add("X-challenge", challenge);
        response.Content = sessionUser + "\n" + sessionPassword;
    }

    // xhr/resources/auth2/corsenabled.py, auth7/corsenabled.py, auth8/corsenabled-no-authorize.py
    private static void CorsEnabledAuthentication(WptHandlerRequest request, WptHandlerResponse response, WptStash stash, string allowHeaders)
    {
        response.Set("Access-Control-Allow-Origin", request.Header("origin"));
        response.Set("Access-Control-Allow-Credentials", "true");
        response.Set("Access-Control-Allow-Methods", "GET");
        response.Set("Access-Control-Allow-Headers", allowHeaders);
        response.Set("Access-Control-Expose-Headers", "x-challenge, xhr-user, ses-user");
        if (request.Method != "OPTIONS")
        {
            XhrAuthentication(request, response, stash);
        }
    }

    private static void AccessControlAllowLists(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        if (request.Has("origin"))
        {
            response.Set("Access-Control-Allow-Origin", request.Get("origin"));
        }

        if (request.Has("headers"))
        {
            response.Set("Access-Control-Allow-Headers", request.Get("headers"));
        }

        if (request.Has("methods"))
        {
            response.Set("Access-Control-Allow-Methods", request.Get("methods"));
        }

        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in request.Headers)
        {
            headers.TryAdd(name.ToLowerInvariant(), value);
        }

        response.Content = JsonSerializer.Serialize(headers);
    }

    private static void AccessControlAllowWithBody(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Cache-Control", "no-store");
        response.Set("Access-Control-Allow-Headers", "X-Requested-With");
        response.Set("Access-Control-Max-Age", "0");
        response.Set("Access-Control-Allow-Origin", "*");
        response.Set("Access-Control-Allow-Methods", "*");
        response.Set("Vary", "Accept-Encoding");
        response.Set("Content-Type", "text/plain");
        response.Content = "PASS";
    }

    private static void AccessControlAuthBasic(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Cache-Control", "no-store");
        response.Set("Access-Control-Allow-Origin", request.Header("origin"));
        response.Set("Access-Control-Allow-Credentials", "true");
        var uid = request.Get("uid");
        if (request.Method == "OPTIONS")
        {
            response.Set("Access-Control-Allow-Methods", "PUT");
            return;
        }

        var (username, password) = request.Auth;
        if (string.IsNullOrEmpty(username) || username != uid)
        {
            response.Set("WWW-Authenticate", "Basic realm='Test Realm/Cross Origin'");
            response.Status = 401;
            response.Content = "Authentication cancelled";
        }
        else
        {
            response.Content = "User: " + username + ", Password: " + password;
        }
    }

    private static void AllowOrigin(WptHandlerResponse response, string? origin, bool credentials)
    {
        response.Set("Content-Type", "text/plain");
        if (credentials)
        {
            response.Set("Access-Control-Allow-Credentials", "true");
        }

        response.Set("Access-Control-Allow-Origin", origin);
        response.Content = "PASS: Cross-domain access allowed.";
    }

    private static void AccessControlSafelistedRequestHeaders(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Cache-Control", "no-store");
        if (request.Method != "POST")
        {
            response.Status = 400;
            return;
        }

        response.Set("Access-Control-Allow-Credentials", "true");
        response.Set("Access-Control-Allow-Origin", request.Header("origin"));
        var content = new StringBuilder();
        foreach (var header in (ReadOnlySpan<string>)["Accept", "Accept-Language", "Content-Language", "Content-Type"])
        {
            content.Append(header).Append(": ").Append(request.Header(header) is { Length: > 0 } value ? value : "<None>").Append('\n');
        }

        response.Content = content.ToString();
    }

    private static void AccessControlSafelistedResponseHeaders(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("content-type", "text/plain");
        response.Set("cache-control", "no cache");
        response.Set("content-language", "en");
        response.Set("expires", "Fri, 30 Oct 1998 14:19:41 GMT");
        response.Set("last-modified", "Tue, 15 Nov 1994 12:45:26 GMT");
        response.Set("pragma", "no-cache");
        response.Set("x-test", "foobar");
        response.Set("Access-Control-Allow-Origin", "*");
        response.Content = "PASS: Cross-domain access allowed.";
    }

    private static void AccessControlDenied(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Cache-Control", "no-store");
        response.Set("Content-Type", "text/plain");
        response.Content = "FAIL: Cross-domain access allowed.";
    }

    private static void AccessControlOptionsNotSupported(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Cache-Control", "no-store");
        if (request.Method == "OPTIONS")
        {
            response.Status = 400;
        }
        else if (request.HasHeader("origin"))
        {
            response.Set("Access-Control-Allow-Credentials", "true");
            response.Set("Access-Control-Allow-Origin", request.Header("origin"));
        }
        else
        {
            response.Status = 500;
        }
    }

    /// <summary>The state machine of the preflight cache handlers (access-control-basic-preflight-cache*.py).</summary>
    private static string GetState(WptStash stash, string? token) => token is not null && stash.Take(token) is { Length: > 0 } state ? state : "Uninitialized";

    private static void Fail(WptHandlerRequest request, WptHandlerResponse response, string message, int? status = null)
    {
        response.Content = "FAIL " + request.Method + ": " + message;
        if (status is { } code)
        {
            response.Status = code;
        }
    }

    private static void AccessControlPreflightCacheInvalidation(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Access-Control-Allow-Origin", request.Header("origin"));
        response.Set("Access-Control-Allow-Credentials", "true");
        var token = request.Get("token") ?? "";
        var state = GetState(stash, token);
        switch (state)
        {
            case "Uninitialized" when request.Method == "OPTIONS":
                response.Set("Access-Control-Allow-Methods", "PUT");
                response.Set("Access-Control-Max-Age", "10");
                stash.Put(token, "OPTIONSSent");
                break;
            case "OPTIONSSent" when request.Method == "PUT":
                response.Content = "PASS: First PUT request.";
                stash.Put(token, "FirstPUTSent");
                break;
            case "FirstPUTSent" when request.Method == "OPTIONS":
                response.Set("Access-Control-Allow-Methods", "PUT, XMETHOD");
                response.Set("Access-Control-Allow-Headers", "x-test");
                stash.Put(token, "SecondOPTIONSSent");
                break;
            case "FirstPUTSent" when request.Method == "PUT":
                Fail(request, response, "Second PUT request sent without preflight");
                break;
            case "SecondOPTIONSSent" when request.Method is "PUT" or "XMETHOD":
                response.Content = "PASS: Second OPTIONS request was sent.";
                break;
            default:
                Fail(request, response, state);
                break;
        }
    }

    private static void AccessControlPreflightCacheTimeout(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Access-Control-Allow-Origin", request.Header("origin"));
        response.Set("Access-Control-Allow-Credentials", "true");
        var token = request.Get("token") ?? "";
        var state = GetState(stash, token);
        switch (state)
        {
            case "Uninitialized" when request.Method == "OPTIONS":
                response.Set("Access-Control-Allow-Methods", "PUT");
                response.Set("Access-Control-Allow-Headers", "x-test");
                response.Set("Access-Control-Max-Age", "1");
                stash.Put(token, "OPTIONSSent");
                break;
            case "OPTIONSSent" when request.Method == "PUT":
                response.Content = "PASS: First PUT request.";
                stash.Put(token, "FirstPUTSent");
                break;
            case "FirstPUTSent" when request.Method == "OPTIONS":
                response.Set("Access-Control-Allow-Methods", "PUT");
                response.Set("Access-Control-Allow-Headers", "x-test");
                stash.Put(token, "SecondOPTIONSSent");
                break;
            case "FirstPUTSent" when request.Method == "PUT":
                Fail(request, response, "Second PUT request sent without preflight");
                break;
            case "SecondOPTIONSSent" when request.Method == "PUT":
                response.Content = "PASS: Second OPTIONS request was sent.";
                break;
            default:
                Fail(request, response, state);
                break;
        }
    }

    private static void AccessControlPreflightCache(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Access-Control-Allow-Origin", request.Header("origin"));
        response.Set("Access-Control-Allow-Credentials", "true");
        var token = request.Get("token") ?? "";
        var state = GetState(stash, token);
        switch (state)
        {
            case "Uninitialized" when request.Method == "OPTIONS":
                response.Set("Access-Control-Allow-Methods", "PUT");
                response.Set("Access-Control-Max-Age", "10");
                stash.Put(token, "OPTIONSSent");
                break;
            case "OPTIONSSent" when request.Method == "PUT":
                response.Content = "PASS: First PUT request.";
                stash.Put(token, "FirstPUTSent");
                break;
            case "FirstPUTSent" when request.Method == "PUT":
                response.Content = "PASS: Second PUT request. Preflight worked.";
                break;
            case "FirstPUTSent" when request.Method == "OPTIONS":
                response.Set("Access-Control-Allow-Methods", "PUT");
                stash.Put(token, "FAILSecondOPTIONSSent");
                break;
            case "FAILSecondOPTIONSSent" when request.Method == "PUT":
                Fail(request, response, "Second OPTIONS request was sent. Preflight failed.", 400);
                break;
            default:
                Fail(request, response, state, 400);
                break;
        }
    }

    private static void AccessControlPutAllow(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Content-Type", "text/plain");
        if (request.Method == "OPTIONS")
        {
            response.Set("Access-Control-Allow-Credentials", "true");
            response.Set("Access-Control-Allow-Methods", "PUT");
            response.Set("Access-Control-Allow-Origin", request.Header("origin"));
        }
        else if (request.Method == "PUT")
        {
            response.Set("Access-Control-Allow-Credentials", "true");
            response.Set("Access-Control-Allow-Origin", request.Header("origin"));
            response.Body = [.. "PASS: Cross-domain access allowed.\n"u8, .. request.Body];
        }
        else
        {
            response.Content = "Wrong method: " + request.Method;
        }
    }

    private static void AccessControlOriginHeader(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Content-Type", "text/plain");
        response.Set("Cache-Control", "no-cache, no-store");
        response.Set("Access-Control-Allow-External", "true");
        response.Set("Access-Control-Allow-Origin", "*");
        response.Content = "PASS: Cross-domain access allowed.\nHTTP_ORIGIN: " + request.Header("origin");
    }

    private static void AccessControlPreflightDenied(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        void Failed(string message)
        {
            response.Content = "FAIL: " + message;
            response.Status = 400;
        }

        response.Set("Cache-Control", "no-store");
        response.Set("Access-Control-Allow-Origin", request.Header("origin"));
        response.Set("Access-Control-Max-Age", "1");
        var token = request.Get("token") ?? "";
        var state = GetState(stash, token);
        var command = request.Get("command");
        if (command == "reset")
        {
            if (request.Method == "GET")
            {
                stash.Put(token, "");
                response.Content = "Server state reset";
            }
            else
            {
                Failed("Invalid Method.");
            }
        }
        else if (state == "Uninitialized")
        {
            if (request.Method == "OPTIONS")
            {
                response.Content = "This request should not be displayed.";
                stash.Put(token, "Denied");
            }
            else
            {
                Failed(state);
            }
        }
        else if (state == "Denied")
        {
            if (request.Method == "GET" && command == "complete")
            {
                stash.Put(token, "");
                response.Content = "Request successfully blocked.";
            }
            else
            {
                stash.Put(token, "Deny Ignored");
                Failed("The request was not denied.");
            }
        }
        else if (state == "Deny Ignored")
        {
            stash.Put(token, "");
            Failed(state);
        }
        else
        {
            stash.Put(token, "");
            Failed("Unknown Error.");
        }
    }

    private static void AccessControlPreflightAllowHeadersStar(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Access-Control-Allow-Origin", "*");
        if (request.Method == "OPTIONS")
        {
            response.Set("Access-Control-Allow-Headers", "*");
        }
        else if (request.Method == "GET")
        {
            if (!string.IsNullOrEmpty(request.Header("X-Test")))
            {
                TextResponse(response, "PASS");
            }
            else
            {
                response.Status = 400;
            }
        }
    }

    private static void AccessControlPreflightHeaderLowercase(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Cache-Control", "no-store");
        response.Set("Access-Control-Allow-Origin", "*");
        response.Set("Access-Control-Max-Age", "0");
        if (request.Method == "OPTIONS")
        {
            if ((request.Header("Access-Control-Request-Headers") ?? "").Split(',').Any(header => header.Trim(' ') == "x-test"))
            {
                response.Set("Access-Control-Allow-Headers", "X-Test");
            }
            else
            {
                response.Status = 400;
            }
        }
        else if (request.Method == "GET")
        {
            if (!string.IsNullOrEmpty(request.Header("X-Test")))
            {
                response.Content = "PASS";
            }
            else
            {
                response.Status = 400;
            }
        }
    }

    private static void AccessControlPreflightHeaderReturnsOrigin(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        if (request.Method == "OPTIONS")
        {
            response.Set("Access-Control-Allow-Origin", request.Header("origin"));
            response.Set("Access-Control-Allow-Headers", "X-Test");
        }
        else if (request.Method == "GET")
        {
            response.Set("Access-Control-Allow-Origin", "*");
            if (!string.IsNullOrEmpty(request.Header("X-Test")))
            {
                TextResponse(response, "PASS");
            }
            else
            {
                response.Status = 400;
            }
        }
    }

    private static void AccessControlPreflightHeaderSorted(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        const string Headers = "x-custom-s,x-custom-test,x-custom-u,x-custom-ua,x-custom-v";
        response.Set("Cache-Control", "no-store");
        response.Set("Access-Control-Allow-Origin", request.Header("origin"));
        if (request.Method == "OPTIONS")
        {
            response.Set("Access-Control-Max-Age", "0");
            response.Set("Access-Control-Allow-Headers", Headers);
            if (request.Header("Access-Control-Request-Headers") != Headers)
            {
                response.Status = 400;
            }
        }
        else if (!string.IsNullOrEmpty(request.Header("x-custom-s")))
        {
            response.Content = "PASS";
        }
        else
        {
            response.Status = 400;
            response.Content = "FAIL";
        }
    }

    private static void AccessControlPreflightHeadersOrigin(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Cache-Control", "no-store");
        response.Set("Access-Control-Allow-Origin", "*");
        if (request.Method == "OPTIONS")
        {
            if ((request.Header("Access-Control-Request-Headers") ?? "").Contains("origin", StringComparison.OrdinalIgnoreCase))
            {
                response.Status = 400;
                response.Content = "Error: 'origin' included in Access-Control-Request-Headers";
            }
            else
            {
                response.Set("Access-Control-Allow-Headers", "x-pass");
            }
        }
        else
        {
            response.Content = request.Header("x-pass") ?? "";
        }
    }

    private static void AccessControlPreflightInvalidStatus(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        if (request.Method == "OPTIONS")
        {
            if (ParseInt(request.Get("code"), 0) is > 0 and var code)
            {
                response.Status = code;
            }

            response.Set("Access-Control-Max-Age", "1");
            response.Set("Access-Control-Allow-Headers", "x-pass");
        }

        response.Set("Cache-Control", "no-store");
        response.Set("Access-Control-Allow-Origin", request.Header("origin"));
    }

    private static void AccessControlPreflightNoCookie(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Cache-Control", "no-store");
        response.Set("Access-Control-Allow-Origin", request.Header("origin"));
        response.Set("Access-Control-Allow-Credentials", "true");
        response.Set("Access-Control-Allow-Headers", "X-Proprietary-Header");
    }

    private static void Conditional(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var tag = request.Get("tag");
        var match = request.Header("If-None-Match");
        var date = request.Get("date", "");
        var modified = request.Header("If-Modified-Since");
        var cors = request.Get("cors");
        if (request.Method == "OPTIONS")
        {
            response.Set("Access-Control-Allow-Origin", "*");
            response.Set("Access-Control-Allow-Headers", "IF-NONE-MATCH");
            return;
        }

        if (!string.IsNullOrEmpty(tag))
        {
            response.Set("ETag", "\"" + tag + "\"");
        }
        else if (!string.IsNullOrEmpty(date))
        {
            response.Set("Last-Modified", date);
        }

        if (cors is not null)
        {
            response.Set("Access-Control-Allow-Origin", "*");
        }

        if ((match is not null && match == tag) || (modified is not null && modified == date))
        {
            response.Status = 304;
            response.StatusText = "SUPERCOOL";
            return;
        }

        if (cors is null)
        {
            response.Set("Access-Control-Allow-Origin", "*");
        }

        response.Set("Content-Type", "text/plain");
        response.Content = "MAYBE NOT";
    }

    private static void EchoContentCors(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Add("X-Request-Method", request.Method);
        response.Add("X-Request-Content-Length", request.Header("Content-Length") ?? "NO");
        response.Add("X-Request-Content-Type", request.Header("Content-Type") ?? "NO");
        response.Add("Access-Control-Allow-Credentials", "true");
        response.Add("Content-Type", "text/plain");
        response.Add("Access-Control-Allow-Origin", request.Get("origin", request.Header("origin")));
        response.Add("Access-Control-Allow-Headers", request.Get("origin", request.Header("access-control-request-headers")));
        if (request.Get("origin", request.Header("access-control-request-method")) is { } method)
        {
            response.Add("Access-Control-Allow-Methods", "OPTIONS, " + method);
        }

        response.Body = request.Body;
    }

    // xhr/resources/gzip.py and zlib.py: the body is decoded by the network layer, the Content-Length is the one of the encoded body
    private static void Compressed(WptHandlerRequest request, WptHandlerResponse response, string encoding)
    {
        var output = request.Has("content") ? Encoding.Latin1.GetBytes(request.Get("content")!) : request.Body;
        using var compressed = new MemoryStream();
        using (Stream stream = encoding == "gzip" ? new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true) : new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            stream.Write(output);
        }

        response.Add("Content-type", "text/plain");
        response.Add("Content-Encoding", encoding);
        AddRequestInfo(request, response);
        response.Add("Content-Length", compressed.Length.ToString(CultureInfo.InvariantCulture));
        response.Body = output;
    }

    private static void HeaderUserAgent(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Access-Control-Allow-Origin", "*");
        response.Set("Access-Control-Max-Age", "0");
        response.Set("Access-Control-Allow-Headers", "x-test");
        if (request.Method == "OPTIONS")
        {
            if (string.IsNullOrEmpty(request.Header("User-Agent")))
            {
                response.Content = "FAIL: User-Agent header missing in preflight request.";
                response.Status = 400;
            }
        }
        else if (!string.IsNullOrEmpty(request.Header("User-Agent")))
        {
            response.Content = "PASS";
        }
        else
        {
            response.Content = "FAIL: User-Agent header missing in request";
            response.Status = 400;
        }
    }

    private static void InfiniteRedirects(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var location = request.Url.Protocol + "//" + request.Url.Host + request.Url.Pathname;
        var page = request.Get("page") == "alternate" ? "default" : "alternate";
        var type = request.Get("type") == "301" ? 301 : 302;
        var mix = 0;
        if (request.Get("mix") == "1")
        {
            mix = 1;
            type = type == 301 ? 302 : 301;
        }

        var newLocation = location + "?page=" + page + "&type=" + type.ToString(CultureInfo.InvariantCulture) + "&mix=" + mix.ToString(CultureInfo.InvariantCulture);
        response.Status = 301;
        response.Add("Cache-Control", "no-cache");
        response.Add("Pragma", "no-cache");
        response.Add("Location", newLocation);
        response.Content = "Hello guest. You have been redirected to " + newLocation;
    }

    private static void LastModified(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var file = Path.Combine(WptPaths.Wpt, "xhr", "resources", "well-formed.xml");
        response.Set("Last-Modified", File.GetLastWriteTimeUtc(file).ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture));
        response.Set("Content-Type", "application/xml");
        response.Body = File.ReadAllBytes(file);
    }

    private static void NoCustomHeaderOnPreflight(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Set("Access-Control-Allow-Origin", "*");
        response.Set("Access-Control-Allow-Headers", "x-test");
        response.Set("Access-Control-Max-Age", "0");
        var token = request.Get("token") ?? "";
        if (request.Method == "OPTIONS")
        {
            if (!string.IsNullOrEmpty(request.Header("x-test")))
            {
                response.Content = "FAIL: Invalid header in preflight request.";
                response.Status = 400;
            }
            else
            {
                stash.Put(token, "PASS");
            }
        }
        else if (!string.IsNullOrEmpty(request.Header("x-test")))
        {
            response.Content = GetState(stash, token);
        }
        else
        {
            response.Content = "FAIL: X-Test header missing in request";
            response.Status = 400;
        }
    }

    private static void RedirectCors(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var location = request.Get("location");
        if (request.Method == "OPTIONS")
        {
            if (request.Has("redirect_preflight"))
            {
                response.Status = 302;
                response.Set("Location", location);
            }

            response.Set("Access-Control-Allow-Methods", "GET");
            response.Set("Access-Control-Max-Age", "1");
        }
        else if (request.Method == "GET")
        {
            response.Status = 302;
            response.Set("Location", location);
        }

        if (request.Has("allow_origin"))
        {
            response.Set("Access-Control-Allow-Origin", request.Header("origin"));
        }

        if (request.Has("allow_header"))
        {
            response.Set("Access-Control-Allow-Headers", request.Get("allow_header"));
        }
    }

    private static void Upload(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var fields = request.Post;
        var content = new StringBuilder();
        foreach (var (key, value) in fields.Where(field => field.Value.File is null).OrderBy(field => field.Key, StringComparer.Ordinal))
        {
            content.Append(key).Append('=').Append(value.StringValue).Append(',');
        }

        content.Append('\n');
        foreach (var (key, value) in fields.Where(field => field.Value.File is not null).OrderBy(field => field.Key, StringComparer.Ordinal))
        {
            var file = value.File!;
            content.Append(key).Append('=').Append(file.Name).Append(':').Append(file.Type.Length > 0 ? file.Type : "None").Append(':').Append(file.Size.ToString(CultureInfo.InvariantCulture)).Append(',');
        }

        response.Body = Encoding.UTF8.GetBytes(content.ToString());
    }

    // fetch/api/resources/redirect.py
    private static void FetchRedirect(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var stashedData = new Dictionary<string, object>(StringComparer.Ordinal) { ["count"] = 0, ["preflight"] = "0" };
        var status = 302;
        response.Add("Content-Type", "text/plain");
        response.Add("Cache-Control", "no-cache");
        response.Add("Pragma", "no-cache");
        if (request.HasHeader("Origin"))
        {
            response.Add("Access-Control-Allow-Origin", request.Header("Origin"));
            response.Add("Access-Control-Allow-Credentials", "true");
        }
        else
        {
            response.Add("Access-Control-Allow-Origin", "*");
        }

        string? token = null;
        if (request.Has("token"))
        {
            token = request.Get("token")!;
            if (stash.TakeValue(token) is Dictionary<string, object> data)
            {
                stashedData = data;
            }
        }

        if (request.Method == "OPTIONS")
        {
            if (request.Has("allow_headers"))
            {
                response.Add("Access-Control-Allow-Headers", request.Get("allow_headers"));
            }

            stashedData["preflight"] = "1";
            if (!request.Has("redirect_preflight"))
            {
                if (token is not null)
                {
                    stash.Put(token, stashedData);
                }

                response.Status = 200;
                return;
            }
        }

        if (request.Has("redirect_status"))
        {
            status = ParseInt(request.Get("redirect_status"), 302);
        }
        else if (GetPost(request, "redirect_status") is { } postStatus)
        {
            status = ParseInt(postStatus, 302);
        }

        var count = (int)stashedData["count"] + 1;
        stashedData["count"] = count;
        if (request.Has("location"))
        {
            var url = request.Get("location")!;
            if (!request.Has("simple"))
            {
                // urlparse(url).scheme
                var colon = url.IndexOf(':', StringComparison.Ordinal);
                var scheme = colon > 0 && char.IsAsciiLetter(url[0]) && url[..colon].All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.') ? url[..colon].ToLowerInvariant() : "";
                if (scheme is "" or "http" or "https")
                {
                    url += url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
                    url += string.Join('&', request.GetParameters.GroupBy(parameter => parameter.Key, StringComparer.Ordinal).Select(group => PythonUrlEncode(group.Key) + "=" + PythonUrlEncode(group.First().Value)));
                    url += "&count=" + count.ToString(CultureInfo.InvariantCulture);
                }
            }

            response.Add("Location", url);
        }

        if (request.Has("redirect_referrerpolicy"))
        {
            response.Add("Referrer-Policy", request.Get("redirect_referrerpolicy"));
        }

        if (request.Has("delay"))
        {
            response.Delay = Milliseconds(request.Get("delay"), 0);
        }

        if (token is not null)
        {
            stash.Put(token, stashedData);
            if (request.Has("max_count") && count > ParseInt(request.Get("max_count"), 0))
            {
                response.Status = 200;
                response.Headers.RemoveAll(header => header.Key == "Location");
                response.Content = (count - 1).ToString(CultureInfo.InvariantCulture);
                return;
            }
        }

        response.Status = status;
    }

    /// <summary>urllib.parse.urlencode (quote_plus) of a byte string.</summary>
    private static string PythonUrlEncode(string value)
    {
        var result = new StringBuilder();
        foreach (var b in Encoding.Latin1.GetBytes(value))
        {
            if (b is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9' or (byte)'_' or (byte)'.' or (byte)'-' or (byte)'~')
            {
                result.Append((char)b);
            }
            else if (b == ' ')
            {
                result.Append('+');
            }
            else
            {
                result.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return result.ToString();
    }

    // fetch/api/resources/inspect-headers.py
    private static void FetchInspectHeaders(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var checkedHeaders = request.Has("headers") ? request.Get("headers")!.Split('|') : [];
        foreach (var header in checkedHeaders)
        {
            if (request.Header(header) is { } value)
            {
                response.Add("x-request-" + header, value);
            }
        }

        if (request.Has("cors"))
        {
            response.Add("Access-Control-Allow-Origin", request.HasHeader("Origin") ? request.Header("Origin") : "*");
            response.Add("Access-Control-Allow-Credentials", "true");
            response.Add("Access-Control-Allow-Methods", "GET, POST, HEAD");
            response.Add("Access-Control-Expose-Headers", string.Join(", ", checkedHeaders.Select(header => "x-request-" + header)));
            response.Add("Access-Control-Allow-Headers", request.Has("allow_headers") ? request.Get("allow_headers") : string.Join(", ", request.Headers.Select(header => header.Key.ToLowerInvariant()).Distinct(StringComparer.Ordinal)));
        }

        response.Add("content-type", "text/plain");
    }

    // fetch/api/resources/preflight.py
    private static void Preflight(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Add("Content-Type", "text/plain");
        var stashedData = new Dictionary<string, string?>(StringComparer.Ordinal) { ["control_request_headers"] = "", ["preflight"] = "0", ["preflight_referrer"] = "" };
        var token = request.Get("token");
        if (request.Has("origin"))
        {
            foreach (var origin in request.Get("origin")!.Split(", "))
            {
                response.Add("Access-Control-Allow-Origin", origin);
            }
        }
        else
        {
            response.Add("Access-Control-Allow-Origin", "*");
        }

        if (request.Has("clear-stash"))
        {
            response.Content = token is not null && stash.TakeValue(token) is not null ? "1" : "0";
            return;
        }

        if (request.Has("credentials"))
        {
            response.Add("Access-Control-Allow-Credentials", "true");
        }

        if (request.Method == "OPTIONS")
        {
            if (!request.HasHeader("Access-Control-Request-Method"))
            {
                response.Status = 400;
                response.Content = "ERROR: No access-control-request-method in preflight!";
                return;
            }

            if (request.Header("Accept") != "*/*")
            {
                response.Status = 400;
                response.Content = "ERROR: Invalid access in preflight!";
                return;
            }

            if (request.Has("control_request_headers"))
            {
                stashedData["control_request_headers"] = request.Header("Access-Control-Request-Headers");
            }

            if (request.Has("max_age"))
            {
                response.Add("Access-Control-Max-Age", request.Get("max_age"));
            }

            if (request.Has("allow_headers"))
            {
                response.Add("Access-Control-Allow-Headers", request.Get("allow_headers"));
            }

            if (request.Has("allow_methods"))
            {
                response.Add("Access-Control-Allow-Methods", request.Get("allow_methods"));
            }

            response.Status = ParseInt(request.Get("preflight_status"), 200);
            stashedData["preflight"] = "1";
            stashedData["preflight_referrer"] = request.Header("Referer") ?? "";
            stashedData["preflight_user_agent"] = request.Header("User-Agent") ?? "";
            if (token is not null)
            {
                stash.Put(token, stashedData);
            }

            return;
        }

        if (token is not null && stash.TakeValue(token) is Dictionary<string, string?> data)
        {
            stashedData = data;
        }

        if (request.Has("checkUserAgentHeaderInPreflight") && request.Header("User-Agent") != stashedData.GetValueOrDefault("preflight_user_agent"))
        {
            response.Status = 400;
            response.Content = "ERROR: No user-agent header in preflight";
            return;
        }

        response.Add("Access-Control-Expose-Headers", "x-did-preflight, x-control-request-headers, x-referrer, x-preflight-referrer, x-origin");
        response.Add("x-did-preflight", stashedData["preflight"]);
        if (stashedData["control_request_headers"] is { } controlRequestHeaders)
        {
            response.Add("x-control-request-headers", controlRequestHeaders);
        }

        response.Add("x-preflight-referrer", stashedData["preflight_referrer"]);
        response.Add("x-referrer", request.Header("Referer") ?? "");
        response.Add("x-origin", request.Header("Origin") ?? "");
        if (token is not null)
        {
            stash.Put(token, stashedData);
        }
    }

    // fetch/api/resources/stash-put.py
    private static void StashPut(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        if (request.Method == "OPTIONS")
        {
            response.Set("Access-Control-Allow-Origin", "*");
            response.Set("Access-Control-Allow-Methods", "*");
            response.Set("Access-Control-Allow-Headers", "*");
            response.Content = "done";
            return;
        }

        if (!request.Has("disallow_cross_origin"))
        {
            response.Set("Access-Control-Allow-Origin", "*");
        }
        else if (request.Get("mode") != "no-cors" && request.Get("frame_origin") != request.Url.Origin)
        {
            response.Content = "not stashing for cors request";
            return;
        }

        stash.Put("stash-take|" + request.Get("key"), Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(request.Get("value") ?? "")));
        response.Content = "done";
    }

    // fetch/api/resources/stash-take.py (a JSON handler)
    private static void StashTake(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var value = stash.TakeValue("stash-take|" + request.Get("key"));
        response.Set("Access-Control-Allow-Origin", "*");
        response.Set("Content-Type", "application/json");
        response.Body = Encoding.UTF8.GetBytes(value is string text ? JsonSerializer.Serialize(text) : "null");
    }

    // fetch/api/resources/authentication.py
    private static void FetchAuthentication(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var (user, password) = request.Auth;
        if (user == "user" && password == "password")
        {
            response.Content = "Authentication done";
            return;
        }

        response.Status = 401;
        response.StatusText = "Unauthorized";
        response.Add("WWW-Authenticate", "Basic realm=\"" + request.Get("realm", "test") + "\"");
        response.Content = "Please login with credentials 'user' and 'password'";
    }

    // fetch/api/resources/cache.py
    private static void FetchCache(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        const string ETag = "\"123abc\"";
        if (request.Header("If-None-Match") == ETag)
        {
            response.Set("X-HTTP-STATUS", "304");
            response.Status = 304;
            response.StatusText = "Not Modified";
            return;
        }

        response.Set("ETag", ETag);
        response.Set("Content-Type", "text/plain");
        response.Content = "lorem ipsum dolor sit amet";
    }

    // fetch/api/request/resources/cache.py: the requests are recorded in the stash
    private static void RequestCache(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var token = request.Get("token") ?? "";
        if (request.Has("querystate"))
        {
            response.Set("Content-Type", "text/plain");
            var states = stash.TakeValue(token) as List<Dictionary<string, string>>;
            response.Content = states is null ? "null" : JsonSerializer.Serialize(states);
            return;
        }

        var tag = request.Get("tag");
        var date = request.Get("date");
        var inm = request.Header("If-None-Match");
        var ims = request.Header("If-Modified-Since");
        if (tag is not null)
        {
            tag = "\"" + tag + "\"";
        }

        var serverState = stash.TakeValue(token) as List<Dictionary<string, string>> ?? [];
        var state = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!request.Has("ignore"))
        {
            foreach (var name in (ReadOnlySpan<string>)["If-None-Match", "If-Modified-Since", "Pragma", "Cache-Control"])
            {
                if (request.Header(name) is { Length: > 0 } value)
                {
                    state[name] = value;
                }
            }
        }

        serverState.Add(state);
        stash.Put(token, serverState);
        if (tag is not null)
        {
            response.Set("ETag", tag);
        }
        else if (date is not null)
        {
            response.Set("Last-Modified", date);
        }

        response.Set("Expires", request.Get("expires"));
        response.Set("Vary", request.Get("vary"));
        response.Set("Cache-Control", request.Get("cache_control"));
        response.Set("Access-Control-Allow-Origin", "*");
        if (request.Get("redirect") is { } redirect)
        {
            response.Set("Location", redirect);
            response.Status = 302;
            response.StatusText = "Redirect";
        }
        else if ((inm is not null && inm == tag) || (ims is not null && ims == date))
        {
            response.Status = 304;
            response.StatusText = "Not Modified";
        }
        else
        {
            response.Set("Content-Type", "text/plain");
            response.Content = request.Get("content") ?? "";
        }
    }

    // fetch/http-cache/resources/http-cache.py: the responses described by the Test-Requests header (the JSON of the requests
    // of the test, encoded in base64), one for each request received for the uuid; the requests are recorded in the stash
    private static void FetchHttpCache(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var uuid = request.Get("uuid");
        response.Set("Access-Control-Allow-Credentials", "true");
        void Fail(int status, string statusText, string body)
        {
            response.Status = status;
            response.StatusText = statusText;
            response.Set("Content-Type", "text/plain");
            response.Content = body;
        }

        if (request.Method == "OPTIONS")
        {
            response.Set("Access-Control-Allow-Origin", request.Header("Origin") ?? "*");
            response.Set("Access-Control-Allow-Methods", "GET");
            response.Set("Access-Control-Allow-Headers", request.Header("Access-Control-Request-Headers") ?? "*");
            response.Set("Access-Control-Max-Age", "86400");
            response.Content = "Preflight request";
            return;
        }

        if (uuid is null)
        {
            Fail(404, "Not Found", "UUID not found");
            return;
        }

        switch (request.Get("dispatch"))
        {
            case "state":
                response.Set("Content-Type", "text/plain");
                response.Content = stash.TakeValue(uuid) is System.Text.Json.Nodes.JsonArray state ? state.ToJsonString() : "null";
                return;
            case "test":
                break;
            default:
                Fail(404, "Not Found", "Fallthrough");
                return;
        }

        var serverState = stash.TakeValue(uuid) as System.Text.Json.Nodes.JsonArray ?? [];
        System.Text.Json.Nodes.JsonArray requests;
        try
        {
            requests = System.Text.Json.Nodes.JsonNode.Parse(Encoding.Latin1.GetString(Convert.FromBase64String(request.Header("Test-Requests") ?? ""))) as System.Text.Json.Nodes.JsonArray ?? throw new FormatException();
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            Fail(400, "Bad Request", "No or bad Test-Requests request header");
            return;
        }

        if (serverState.Count >= requests.Count)
            throw new InvalidOperationException("list index out of range");

        if (requests[serverState.Count] is not System.Text.Json.Nodes.JsonObject config || config.Count == 0)
        {
            Fail(404, "Not Found", "Config not found");
            return;
        }

        // The magic locations (the URL of the request, with the target) and the magic dates (a number of seconds from now)
        var now = request.Now;
        var notedHeaders = new System.Text.Json.Nodes.JsonObject();
        foreach (var header in config["response_headers"] as System.Text.Json.Nodes.JsonArray ?? [])
        {
            if (header is not System.Text.Json.Nodes.JsonArray { Count: >= 2 } pair)
                continue;

            var name = pair[0]!.GetValue<string>();
            var lowerName = name.ToLowerInvariant();
            string value;
            if (pair[1] is System.Text.Json.Nodes.JsonValue number && number.GetValueKind() == JsonValueKind.Number && lowerName is "date" or "expires" or "last-modified")
            {
                value = now.AddSeconds(number.GetValue<double>()).UtcDateTime.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture);
            }
            else
            {
                value = pair[1] is System.Text.Json.Nodes.JsonValue jsonValue && jsonValue.GetValueKind() == JsonValueKind.String ? jsonValue.GetValue<string>() : pair[1]?.ToJsonString() ?? "";
            }

            if (lowerName is "location" or "content-location")
            {
                value = value.Length > 0 ? request.Url.Href + "&target=" + value : request.Url.Href;
            }

            response.Set(name, value);
            if (lowerName is "content-type" or "access-control-allow-origin" or "last-modified" or "etag")
            {
                notedHeaders[lowerName] = value;
            }
        }

        var requestHeaders = new System.Text.Json.Nodes.JsonObject();
        foreach (var (name, _) in request.Headers)
        {
            requestHeaders[name.ToLowerInvariant()] = request.Header(name);
        }

        serverState.Add(new System.Text.Json.Nodes.JsonObject
        {
            ["now"] = (now - DateTimeOffset.UnixEpoch).TotalSeconds,
            ["request_method"] = request.Method,
            ["request_headers"] = requestHeaders,
            ["response_headers"] = notedHeaders,
        });
        stash.Put(uuid, serverState);

        if (!notedHeaders.ContainsKey("access-control-allow-origin"))
        {
            response.Set("Access-Control-Allow-Origin", "*");
        }

        if (!notedHeaders.ContainsKey("content-type"))
        {
            response.Set("Content-Type", "text/plain");
        }

        response.Set("Server-Request-Count", serverState.Count.ToString(CultureInfo.InvariantCulture));
        var (status, statusText) = config["response_status"] is System.Text.Json.Nodes.JsonArray { Count: >= 2 } statusPair
            ? (statusPair[0]!.GetValue<int>(), statusPair[1]!.GetValue<string>())
            : (200, "OK");

        // A request expected to be validated gets a 304 response when it has the validator of the first response
        if (config["expected_type"]?.GetValue<string>() is { } expectedType && expectedType.EndsWith("validated", StringComparison.Ordinal))
        {
            var referenceHeaders = serverState[0]!["response_headers"]!.AsObject();
            if (referenceHeaders["last-modified"]?.GetValue<string>() is { } previousLastModified && request.Header("If-Modified-Since") == previousLastModified)
            {
                (status, statusText) = (304, "Not Modified");
            }

            if (referenceHeaders["etag"]?.GetValue<string>() is { } previousETag && request.Header("If-None-Match") == previousETag)
            {
                (status, statusText) = (304, "Not Modified");
            }

            if (status != 304)
            {
                (status, statusText) = (999, "304 Not Generated");
            }
        }

        response.Status = status;
        response.StatusText = statusText;
        if (status is not (204 or 304))
        {
            // A null response_body is an empty body
            response.Content = !config.TryGetPropertyValue("response_body", out var body) ? uuid : body is System.Text.Json.Nodes.JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : body?.ToJsonString() ?? "";
        }
    }

    // fetch/http-cache/resources/cached_pragma_rand.py, cached_pragma_immutable_rand.py: a unique token, with a Cache-Control
    // that allows caching and Pragma: no-cache
    private static void CachedPragmaRand(WptHandlerResponse response, string cacheControl)
    {
        response.Set("X-XSS-Protection", "0");
        response.Set("Content-Type", "text/html");
        response.Set("Cache-Control", cacheControl);
        response.Set("Pragma", "no-cache");
        response.Content = "Token: " + Guid.NewGuid().ToString("D");
    }

    // fetch/api/resources/dump-authorization-header.py
    private static void DumpAuthorizationHeader(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Add("Content-Type", "text/html");
        response.Add("Cache-Control", "no-cache");
        if (request.Has("strip_auth_header") && request.Method == "OPTIONS" && (request.Header("Access-Control-Request-Headers") ?? "").Contains("authorization", StringComparison.OrdinalIgnoreCase))
        {
            response.Status = 500;
            response.Content = "fail";
            return;
        }

        if (request.HasHeader("Origin"))
        {
            response.Add("Access-Control-Allow-Origin", request.Header("Origin"));
            response.Add("Access-Control-Allow-Credentials", "true");
        }
        else
        {
            response.Add("Access-Control-Allow-Origin", "*");
        }

        response.Add("Access-Control-Allow-Headers", "Authorization");
        response.Content = request.Header("Authorization") ?? "none";
    }

    // fetch/api/resources/echo-content.py
    private static void EchoContent(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Add("X-Request-Method", request.Method);
        response.Add("X-Request-Content-Length", request.Header("Content-Length") ?? "NO");
        response.Add("X-Request-Content-Type", request.Header("Content-Type") ?? "NO");
        response.Add("Content-Type", "text/plain");
        response.Body = request.Body;
    }

    // fetch/api/resources/method.py
    private static void Method(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        if (request.Has("cors"))
        {
            response.Add("Access-Control-Allow-Origin", "*");
            response.Add("Access-Control-Allow-Credentials", "true");
            response.Add("Access-Control-Allow-Methods", "GET, POST, PUT, FOO");
            response.Add("Access-Control-Allow-Headers", "x-test, x-foo");
            response.Add("Access-Control-Expose-Headers", "x-request-method");
        }

        response.Add("x-request-method", request.Method);
        foreach (var name in (ReadOnlySpan<string>)["Content-Type", "Content-Length", "Content-Encoding", "Content-Language", "Content-Location"])
        {
            response.Add("x-request-" + name.ToLowerInvariant(), request.Header(name) ?? "NO");
        }

        response.Body = request.Body;
    }

    // fetch/api/resources/infinite-slow-response.py: the response never ends (the request is aborted by the test)
    private static void InfiniteSlowResponse(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        if (request.Get("stateKey") is { Length: > 0 } stateKey)
        {
            stash.Put("stash-take|" + stateKey, "open");
        }

        response.Set("Content-type", "text/plain");
        response.Pending = true;
    }

    // fetch/api/resources/preflight-unterminated-body.py: the body of the preflight response never ends (the headers suffice)
    private static void PreflightUnterminatedBody(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        response.Add("Access-Control-Allow-Origin", request.Header("Origin") ?? "*");
        response.Add("Access-Control-Allow-Methods", "GET");
        response.Add("Access-Control-Allow-Headers", "x-preflight-test");
        response.Add("Access-Control-Max-Age", "0");
        response.Add("Content-Type", "text/plain");
        if (request.Method != "OPTIONS")
        {
            response.Content = "actual request";
        }
    }
}
