using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The handlers of the <c>resource-timing</c> and <c>navigation-timing</c> tests, and the ones of the <c>import-maps</c> tests and of <c>fetch/content-type</c> that they use.</summary>
internal static partial class WptPythonHandlers
{
    private static readonly Dictionary<string, Handler> TimingHandlers = new(StringComparer.Ordinal)
    {
        ["/resource-timing/SyntheticResponse.py"] = SyntheticResponse,
        ["/resource-timing/resources/TAOResponse.py"] = TimingAllowOriginResponse,

        // A response that is not cached: the library has no HTTP cache, so the requests are never conditional
        ["/resource-timing/resources/cacheable-and-validated.py"] = (request, response, _) =>
        {
            if (request.Get("timing_allow_origin") is { Length: > 0 } tao)
            {
                response.Set("Timing-Allow-Origin", tao);
            }

            if (request.Header("origin") is { } origin)
            {
                response.Set("Access-Control-Allow-Origin", origin);
            }

            var content = request.Get("content") ?? throw new InvalidOperationException("The content parameter is missing");
            response.Set("Cache-Control", "max-age=60");
            response.Set("ETag", "assdfsdfe");
            if (request.Method == "OPTIONS")
            {
                AllowPreflight(request, response);
            }
            else if (request.HasHeader("Cache-Control"))
            {
                response.Status = 304;
                response.StatusText = "NotModified";
            }
            else
            {
                response.StatusText = "OK";
                response.Content = content;
            }
        },

        // A body compressed with a dictionary: the body is not decoded (the dictionaries are not supported)
        ["/resource-timing/resources/compressed-data.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/plain");
            if (request.Get("content_encoding") is { } contentEncoding)
            {
                response.Set("Content-Encoding", contentEncoding);
                response.Body = contentEncoding switch
                {
                    "dcb" => Convert.FromHexString("ff44434253969bcf5e960e0edbf0a4bdde6b0b3e9381e156de7f5b91ce8391624270f416a198018062a44c1ddf12848caec2ca6022076e810514c9b7c3448ebc16e0150eecc1ee34333e0d"),
                    "dcz" => Convert.FromHexString("5e2a4d182000000053969bcf5e960e0edbf0a4bdde6b0b3e9381e156de7f5b91ce8391624270f41628b52ffd2434f5000098636f6d70726573736564617461207573696e67030059f97354462726109e99f2bc"),
                    _ => [],
                };
            }

            if (request.Get("allow_origin") is { } allowOrigin)
            {
                response.Set("access-control-allow-origin", allowOrigin);
            }
        },

        // dummy.js with a content coding: the body is decoded by the network layer, the Content-Length is the one of the encoded body
        ["/resource-timing/resources/compressed-js.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/javascript");
            if (request.Get("allow_origin") is { } allowOrigin)
            {
                response.Set("access-control-allow-origin", allowOrigin);
            }

            if (request.Get("content_encoding") is { } contentEncoding)
            {
                response.Set("Content-Encoding", contentEncoding);
                var content = ReadWptFile("resource-timing/resources/dummy.js");
                switch (contentEncoding)
                {
                    case "deflate":
                        SetEncodedBody(response, content, gzip: false);
                        break;
                    case "gzip" or "unrecognizedname":
                        SetEncodedBody(response, content, gzip: true);
                        break;
                    case "identity":
                        response.Body = content;
                        break;
                }
            }
        },
        ["/resource-timing/resources/content-encoding-headers.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/javascript");
            response.Body = ReadWptFile("resource-timing/resources/dummy.js");
            if (request.Get("content_encoding") is { } contentEncoding)
            {
                response.Set("Content-Encoding", contentEncoding);
            }
        },
        ["/resource-timing/resources/content-type.py"] = (request, response, _) =>
        {
            if (request.Get("content_type") is { } contentType)
            {
                response.Set("content-type", contentType);
            }

            if (request.Get("allow_origin") is { } allowOrigin)
            {
                response.Set("access-control-allow-origin", allowOrigin);
            }
        },
        ["/resource-timing/resources/cors-ahem.py"] = (request, response, _) =>
        {
            const string ETag = "123abc";
            if (request.Header("If-None-Match") == ETag)
            {
                response.Set("X-HTTP-STATUS", "304");
                response.Status = 304;
                response.StatusText = "Not Modified";
                return;
            }

            response.Set("Cache-Control", "public, max-age=86400");
            response.Set("Content-Type", "font/truetype");
            response.Set("Access-Control-Allow-Origin", "*");
            response.Set("Timing-Allow-Origin", "*");
            response.Set("ETag", ETag);
            response.Body = ReadWptFile("fonts/Ahem.ttf");
        },
        ["/resource-timing/resources/delay-css.py"] = (request, response, _) =>
        {
            response.Delay = Milliseconds(request.Get("delay"), 1000);
            response.Set("Content-Type", "text/css");
            response.Content = "/* */";
        },
        ["/resource-timing/resources/empty.py"] = (_, response, _) => response.Set("Content-Type", "text/plain"),
        ["/resource-timing/resources/eventsource.py"] = (_, response, _) => response.Set("Content-Type", "text/event-stream"),
        ["/resource-timing/resources/fake_responses.py"] = FakeResponses,

        // resource_timing_test0.xml compressed with gzip: the body is decoded by the network layer
        ["/resource-timing/resources/gzip_xml.py"] = (request, response, _) =>
        {
            response.Set("Content-type", "text/plain");
            response.Set("Content-Encoding", "gzip");
            SetEncodedBody(response, ReadWptFile("resource-timing/resources/resource_timing_test0.xml"), gzip: true);
            if (request.Get("allow_origin") is { } allowOrigin)
            {
                response.Add("access-control-allow-origin", allowOrigin);
            }
        },

        // A response whose headers are delayed, after delayed interim responses (which the loader does not expose)
        ["/resource-timing/resources/header-delay.py"] = HeaderDelay,
        ["/resource-timing/resources/header-delay.h2.py"] = HeaderDelay,
        ["/resource-timing/resources/multi_redirect.py"] = MultiRedirect,
        ["/resource-timing/resources/preflight.py"] = (request, response, _) =>
        {
            response.Set("Access-Control-Allow-Origin", "*");
            response.Set("Access-Control-Max-Age", "0");
            response.Set("Timing-Allow-Origin", "*");
            if (request.Method == "OPTIONS")
            {
                response.Set("Access-Control-Allow-Headers", "X-Require-Preflight");
            }
        },
        ["/resource-timing/resources/redirect-cors.py"] = (request, response, _) =>
        {
            if (request.Get("allow_origin") is { } allowOrigin)
            {
                response.Set("Access-Control-Allow-Origin", allowOrigin);
            }

            if (request.Get("timing_allow_origin") is { } timingAllowOrigin)
            {
                response.Set("Timing-Allow-Origin", timingAllowOrigin);
            }

            if (request.Method == "OPTIONS")
            {
                AllowPreflight(request, response);
            }
            else
            {
                response.Status = 302;
                response.Set("Location", request.Get("location") ?? throw new InvalidOperationException("The location parameter is missing"));
            }
        },
        ["/resource-timing/resources/redirect-without-location.py"] = (_, response, _) => response.Status = 302,

        // A raw response whose Content-Length may differ from the length of its content: the network layer reads at most
        // Content-Length bytes, and the connection is closed after the content
        ["/resource-timing/resources/resource-timing-content-length.py"] = (request, response, _) =>
        {
            var content = Encoding.Latin1.GetBytes(request.Get("content") ?? throw new InvalidOperationException("The content parameter is missing"));
            var length = request.Get("length") ?? throw new InvalidOperationException("The length parameter is missing");
            response.Set("Content-Type", "text/plain;charset=UTF-8");
            response.Set("Connection", "close");

            // The Python handler compares the decoded parameter with byte strings, so the header always has the value of the
            // parameter ("auto" and "none" included, which are not valid lengths: the body ends when the connection is closed)
            response.Set("Content-Length", length);
            if (int.TryParse(length, NumberStyles.None, CultureInfo.InvariantCulture, out var declaredLength) && declaredLength < content.Length)
            {
                content = content[..declaredLength];
            }

            response.Body = content;
        },
        ["/resource-timing/resources/status-code.py"] = (request, response, _) =>
        {
            response.Status = int.Parse(request.Get("status") ?? throw new InvalidOperationException("The status parameter is missing"), CultureInfo.InvariantCulture);
            response.StatusText = "";
            if (request.Get("tao_value") is { } tao)
            {
                response.Set("timing-allow-origin", tao);
            }

            if (request.Get("allow_origin") is { } allowOrigin)
            {
                response.Set("access-control-allow-origin", allowOrigin);
            }
        },

        ["/navigation-timing/resources/navigation_type_post_back.py"] = (_, response, _) =>
        {
            response.Set("Content-Type", "text/html");
            response.Content = """

                    <!DOCTYPE html>
                      <html>
                        <body>
                            <a href="blank_page_green.html">navigate away</a>.
                        </body>
                      </html>

                """;
        },
        ["/navigation-timing/resources/redirect-tao.py"] = (request, response, _) =>
        {
            response.Status = ParseInt(request.Get("status"), 302);
            response.Set("Location", request.Get("location") ?? throw new InvalidOperationException("The location parameter is missing"));
            if (request.Get("tao") is { } tao)
            {
                response.Set("Timing-Allow-Origin", tao);
            }
        },

        // A document whose body arrives over one second
        ["/navigation-timing/resources/respond-slowly.py"] = (_, response, _) =>
        {
            const string Head = """
                <script>
                    let navigationTiming = performance.getEntriesByType('navigation')[0];
                    let originalResponseEnd = navigationTiming.responseEnd;
                    let originalDuration = navigationTiming.duration;
                    function checkResponseEnd() {
                        let responseEndDuringLoadEvent = navigationTiming.responseEnd;
                        let durationDuringLoadEvent = navigationTiming.duration;
                        setTimeout(function() {
                            parent.postMessage([
                                originalResponseEnd,
                                originalDuration,
                                responseEndDuringLoadEvent,
                                durationDuringLoadEvent,
                                navigationTiming.responseEnd,
                                navigationTiming.duration], '*');
                        }, 0);
                    }
                    </script><body onload='checkResponseEnd()'>
                """;
            var head = Encoding.ASCII.GetBytes(Head.TrimEnd('\n'));
            response.Set("Content-Length", (head.Length + 1000).ToString(CultureInfo.InvariantCulture));
            response.Set("Content-Type", "text/html");
            response.Body = head;
            for (var i = 0; i < 100; i++)
            {
                response.Chunks.Add((i == 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(10), "1234567890"u8.ToArray()));
            }

            response.Chunks.Add((TimeSpan.FromMilliseconds(10), []));
        },

        // fetch/content-type/resources/content-type.py: a raw response whose Content-Type headers are the value parameters
        // (in one header, separated by commas, with single_header)
        ["/fetch/content-type/resources/content-type.py"] = (request, response, _) =>
        {
            var values = request.GetParameters.Where(parameter => parameter.Key == "value").Select(parameter => parameter.Value).ToList();
            response.Set("X-Content-Type-Options", "nosniff");
            if (request.Has("single_header"))
            {
                response.Add("Content-Type", string.Join(',', values));
            }
            else
            {
                foreach (var value in values)
                {
                    response.Add("Content-Type", value);
                }
            }

            response.Add("Connection", "close");
            response.Content = request.Get("content", "<b>hi</b>\n") ?? "";
        },

        // A module script that imports the url parameter
        ["/import-maps/static-import.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/javascript");
            response.Content = "import \"" + (request.Get("url") ?? throw new InvalidOperationException("The url parameter is missing")) + "\";\n";
        },
    };

    /// <summary>The response of a CORS-preflight request that allows the requested method and headers.</summary>
    private static void AllowPreflight(WptHandlerRequest request, WptHandlerResponse response)
    {
        response.Status = 204;
        if (request.Header("Access-Control-Request-Method") is { Length: > 0 } requestedMethod)
        {
            response.Set("Access-Control-Allow-Methods", requestedMethod);
        }

        if (request.Header("Access-Control-Request-Headers") is { Length: > 0 } requestedHeaders)
        {
            response.Set("Access-Control-Allow-Headers", requestedHeaders);
        }
    }

    /// <summary>
    /// Sets the body of a response with a content coding: the body is decoded by the network layer, and the
    /// <c>Content-Length</c> is the length of the encoded body (gzip, or zlib for <c>deflate</c>).
    /// </summary>
    private static void SetEncodedBody(WptHandlerResponse response, byte[] content, bool gzip)
    {
        using var compressed = new MemoryStream();
        using (Stream stream = gzip ? new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true) : new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            stream.Write(content);
        }

        response.Set("Content-Length", compressed.Length.ToString(CultureInfo.InvariantCulture));
        response.Body = content;
    }

    // resource-timing/SyntheticResponse.py: the commands of the query (separated by '&') build the response: a delay
    // ("100ms"), a redirect ("redirect:<url>"), a header ("mime:<type>"), a status ("status:<code>") and a part of the body
    // ("send:<text>"). The interim responses (1xx statuses) are not exposed by the loader.
    private static void SyntheticResponse(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        // sleep.py sleeps for at least the delay, plus a padding of 15 ms
        const double SleepPadding = 15;
        var sent = false;
        var pendingDelay = TimeSpan.Zero;
        foreach (var argument in request.Query.Split('&'))
        {
            if (argument.StartsWith("ignored", StringComparison.Ordinal))
                continue;

            if (argument.EndsWith("ms", StringComparison.Ordinal))
            {
                pendingDelay += TimeSpan.FromMilliseconds(double.Parse(argument[..^2], CultureInfo.InvariantCulture) + SleepPadding);
            }
            else if (argument.StartsWith("redirect:", StringComparison.Ordinal))
            {
                response.Status = 302;
                response.StatusText = "WEBPERF MARKETING";
                response.Headers.Clear();
                response.Set("Location", Uri.UnescapeDataString(argument[9..]));
                response.Content = "TEST";
                response.Delay += pendingDelay;
                return;
            }
            else if (argument.StartsWith("mime:", StringComparison.Ordinal))
            {
                response.Add("Content-Type", Uri.UnescapeDataString(argument[5..]));
            }
            else if (argument.StartsWith("send:", StringComparison.Ordinal))
            {
                var text = Encoding.UTF8.GetBytes(Uri.UnescapeDataString(argument[5..]));
                if (!sent)
                {
                    sent = true;
                    response.Streamed = true;
                    response.Delay += pendingDelay;
                    response.Body = text;
                }
                else
                {
                    response.Chunks.Add((pendingDelay, text));
                }

                pendingDelay = TimeSpan.Zero;
            }
            else if (argument.StartsWith("status:", StringComparison.Ordinal))
            {
                var code = int.Parse(Uri.UnescapeDataString(argument[7..]), CultureInfo.InvariantCulture);
                if (code / 100 != 1)
                {
                    response.Status = code;
                }
            }
        }

        if (sent && pendingDelay > TimeSpan.Zero)
        {
            response.Chunks.Add((pendingDelay, []));
        }
        else if (!sent)
        {
            response.Delay += pendingDelay;
        }
    }

    // resource-timing/resources/TAOResponse.py: a response with the Timing-Allow-Origin headers of the tao parameter
    private static void TimingAllowOriginResponse(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var origin = request.Header("origin");
        if (origin is not null)
        {
            response.Set("Access-Control-Allow-Origin", origin);
        }

        var tao = request.Get("tao") ?? throw new InvalidOperationException("The tao parameter is missing");

        // The Python handler fails when it needs the Origin header of a request that has none
        string Origin() => origin ?? throw new InvalidOperationException("The request has no Origin header");
        switch (tao)
        {
            case "wildcard":
                response.Set("Timing-Allow-Origin", "*");
                break;
            case "null":
                response.Set("Timing-Allow-Origin", "null");
                break;
            case "Null":
                response.Set("Timing-Allow-Origin", "Null");
                break;
            case "origin":
                response.Set("Timing-Allow-Origin", Origin());
                break;
            case "space":
                response.Set("Timing-Allow-Origin", Origin() + " *");
                break;
            case "multi":
                response.Set("Timing-Allow-Origin", Origin());
                response.Add("Timing-Allow-Origin", "*");
                break;
            case "multi_wildcard":
                response.Set("Timing-Allow-Origin", "*");
                response.Add("Timing-Allow-Origin", "*");
                break;
            case "match_origin":
                response.Set("Timing-Allow-Origin", Origin());
                response.Add("Timing-Allow-Origin", "fake");
                break;
            case "match_wildcard":
                response.Set("Timing-Allow-Origin", "fake");
                response.Add("Timing-Allow-Origin", "*");
                break;
            case "uppercase":
                response.Set("Timing-Allow-Origin", Origin().ToUpperInvariant());
                break;
            default:
                if (tao.StartsWith("origin_port", StringComparison.Ordinal))
                {
                    var originParts = Origin().Split(':');
                    var port = tao.Split("origin_port_")[1];
                    response.Set("Timing-Allow-Origin", originParts[0] + ":" + originParts[1] + ":" + port);
                }

                break;
        }

        if (request.Has("img"))
        {
            response.Set("Content-Type", "image/png");
            response.Body = ReadWptFile("resource-timing/resources/blue.png");
        }
        else
        {
            response.Set("Content-Type", "text/plain");
            response.Content = "TEST";
        }
    }

    // resource-timing/resources/fake_responses.py: a response with a validator (ETag or Last-Modified), a redirect, or a
    // script of the resources directory
    private static void FakeResponses(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        if (request.Method == "OPTIONS")
        {
            response.Set("Access-Control-Allow-Headers", "*");
            response.Set("Access-Control-Allow-Origin", "*");
            response.Status = 204;
            response.StatusText = "No Content";
            return;
        }

        var tag = request.Get("tag");
        var redirect = request.Get("redirect");
        var match = request.Header("If-None-Match");
        var date = request.Get("date") ?? "";
        var modified = request.Header("If-Modified-Since");
        var url = request.Get("url");
        response.Set("Access-Control-Allow-Origin", "*");
        response.Set("Timing-Allow-Origin", "*");
        if (!string.IsNullOrEmpty(tag))
        {
            response.Set("ETag", "\"" + tag + "\"");
        }
        else if (date.Length > 0)
        {
            response.Set("Last-Modified", date);
        }

        if (!string.IsNullOrEmpty(redirect))
        {
            response.Set("Location", redirect);
            response.Status = 302;
            response.StatusText = "Moved";
            return;
        }

        if (!string.IsNullOrEmpty(url))
        {
            response.Set("Content-Type", "text/javascript");
            response.Body = ReadWptFile("resource-timing/resources/" + DecodeUtf8(url).Split('?')[0]);
            return;
        }

        if ((match is not null && match == tag) || (modified is not null && modified == date))
        {
            response.Status = 304;
            response.StatusText = "SUPERCOOL";
            return;
        }

        response.Set("Content-Type", "text/plain");
        response.Content = "MAYBE NOT";
    }

    // resource-timing/resources/header-delay.py: the headers are sent after the delay, and after each interim response
    private static void HeaderDelay(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var delay = Milliseconds(request.Get("delay") ?? throw new InvalidOperationException("The delay parameter is missing"), 0);
        var steps = 1 + (request.Has("with100") ? 1 : 0) + (request.Has("with103") ? 1 : 0);
        response.Delay = delay * steps;
        if (request.Has("tao"))
        {
            response.Set("timing-allow-origin", "*");
        }

        response.Set("content-type", "text/plain");
        response.Set("access-control-allow-origin", "*");
        response.Streamed = true;
        response.Content = "Hello World";
    }

    // resource-timing/resources/multi_redirect.py: a redirect to the cross origin, then to the page origin, then to the final
    // resource; the first tao_steps redirects have the Timing-Allow-Origin header
    private static void MultiRedirect(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var step = ParseInt(request.Get("step"), 1);
        var pageOrigin = request.Get("page_origin") ?? throw new InvalidOperationException("The page_origin parameter is missing");
        var crossOrigin = request.Get("cross_origin") ?? throw new InvalidOperationException("The cross_origin parameter is missing");
        var finalResource = request.Get("final_resource") ?? throw new InvalidOperationException("The final_resource parameter is missing");
        var taoValue = request.Get("tao_value", "*");
        var taoSteps = ParseInt(request.Get("tao_steps"), 0);

        var redirectUrlPath = "/resource-timing/resources/multi_redirect.py?"
            + "page_origin=" + pageOrigin
            + "&cross_origin=" + crossOrigin
            + "&final_resource=" + PythonQuote(finalResource)
            + "&tao_value=" + taoValue
            + "&tao_steps=" + (taoSteps - 1).ToString(CultureInfo.InvariantCulture)
            + "&step=";
        if (taoSteps > 0)
        {
            response.Set("timing-allow-origin", taoValue);
        }

        response.Status = 302;
        response.Set("Location", step switch
        {
            1 => crossOrigin + redirectUrlPath + "2",
            2 => pageOrigin + redirectUrlPath + "3",
            _ => pageOrigin + finalResource,
        });
    }

    /// <summary><c>urllib.parse.quote(value)</c> in Python: the characters other than the unreserved characters and <c>/</c> are percent-encoded.</summary>
    private static string PythonQuote(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var b in Encoding.Latin1.GetBytes(value))
        {
            var c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-' or '~' or '/')
            {
                result.Append(c);
            }
            else
            {
                result.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return result.ToString();
    }
}
