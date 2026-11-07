using System.Globalization;
using System.Text;
using System.Text.Json;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The handlers of <c>common/security-features</c> (the framework of the <c>referrer-policy</c>, <c>mixed-content</c> and
/// <c>upgrade-insecure-requests</c> tests) and of the <c>referrer-policy</c> tests.
/// </summary>
internal static partial class WptPythonHandlers
{
    private static readonly Dictionary<string, Handler> SecurityFeaturesHandlers = new(StringComparer.Ordinal)
    {
        // The subresources report the headers of their request (subresource/subresource.py)
        ["/common/security-features/subresource/xhr.py"] = (request, response, stash) =>
            Subresource(request, response, stash, "application/json", headers => "{\"headers\": " + headers + "}", cacheControl: "no-store"),
        ["/common/security-features/subresource/empty.py"] = (request, response, stash) => Subresource(request, response, stash, "text/plain", _ => ""),
        ["/common/security-features/subresource/script.py"] = (request, response, stash) =>
            Subresource(request, response, stash, "application/javascript", headers => SubresourceTemplate("script.js.template").Replace("%(headers)s", headers, StringComparison.Ordinal)),
        ["/common/security-features/subresource/worker.py"] = (request, response, stash) =>
            Subresource(request, response, stash, "application/javascript", headers => SubresourceTemplate("worker.js.template").Replace("%(headers)s", headers, StringComparison.Ordinal)),
        ["/common/security-features/subresource/shared-worker.py"] = (request, response, stash) =>
            Subresource(request, response, stash, "application/javascript", headers => SubresourceTemplate("shared-worker.js.template").Replace("%(headers)s", headers, StringComparison.Ordinal)),
        ["/common/security-features/subresource/document.py"] = (request, response, stash) =>
            Subresource(request, response, stash, "text/html", headers => SubresourceTemplate("document.html.template").Replace("%(headers)s", headers, StringComparison.Ordinal)),
        ["/common/security-features/subresource/static-import.py"] = StaticImport,
        ["/common/security-features/subresource/image.py"] = (request, response, stash) => ReportingSubresource(request, response, stash, "image/bmp", data => EncodeStringAsBmpImage(data)),
        ["/common/security-features/subresource/font.py"] = (request, response, stash) => ReportingSubresource(request, response, stash, "application/x-font-truetype", _ => Convert.FromBase64String(SubresourceFont)),
        ["/common/security-features/subresource/svg.py"] = SubresourceSvg,
        ["/common/security-features/subresource/stylesheet.py"] = SubresourceStylesheet,
        ["/common/security-features/subresource/audio.py"] = (request, response, stash) =>
            Subresource(request, response, stash, "audio/wav", _ => "", body: () => ReadWptFile("webaudio/resources/sin_440Hz_-6dBFS_1s.wav")),
        ["/common/security-features/subresource/video.py"] = (request, response, stash) =>
            Subresource(request, response, stash, "video/webm", _ => "", body: () => ReadWptFile("media/movie_5.webm")),
        ["/common/security-features/subresource/referrer.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/javascript");
            response.Content = "window.referrer = '" + (request.Header("Referer") ?? "") + "'";
        },

        // scope/document.py: a document that delivers the policies (meta or Referrer-Policy header) and runs invokeRequest()
        // for its parent
        ["/common/security-features/scope/document.py"] = ScopeDocument,

        // scope/worker.py: the script of a dedicated or shared worker that delivers the policies (headers) and runs
        // invokeRequest() for its parent
        ["/common/security-features/scope/worker.py"] = ScopeWorker,

        // referrer-policy/generic/resources/referrer.py: posts the Referer header to the parent
        ["/referrer-policy/generic/resources/referrer.py"] = (request, response, _) =>
        {
            response.Set("Access-Control-Allow-Origin", "*");
            response.Set("Content-Type", "text/html");
            response.Content = "\n    <p id=referrer>" + (request.Header("Referer") ?? "") + "</p>\n    <script>\n      const referrer_text = referrer.textContent;\n      window.parent.postMessage(referrer_text, \"*\");\n    </script>\n    ";
        },

        // referrer-policy/generic/resources/refresh-policy.py: a Refresh header to the url parameter, with the Referrer-Policy
        // of the policy parameter
        ["/referrer-policy/generic/resources/refresh-policy.py"] = (request, response, _) =>
        {
            response.Set("Content-Type", "text/html");
            var body = "<!doctype html>";
            var url = DecodeUtf8(request.Get("url") ?? "");
            if (url.Length > 0)
            {
                response.Add("Refresh", "0; url=" + url);
                body += "Refreshing to " + url;
            }
            else
            {
                body += "Not refreshing";
            }

            response.Add("Referrer-Policy", DecodeUtf8(request.Get("policy") ?? ""));
            response.Body = Encoding.UTF8.GetBytes(body);
        },
    };

    private static string SubresourceTemplate(string name) => File.ReadAllText(Path.Combine(WptPaths.Wpt, "common", "security-features", "subresource", "template", name));

    private static byte[] ReadWptFile(string path) => File.ReadAllBytes(Path.Combine(WptPaths.Wpt, path.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// <c>subresource.respond()</c> (common/security-features/subresource/subresource.py): the redirections and the stash
    /// actions of the query, else the payload generated from the headers of the request (a JSON object).
    /// </summary>
    private static void Subresource(WptHandlerRequest request, WptHandlerResponse response, WptStash stash, string contentType, Func<string, string> payload, string cacheControl = "no-cache; must-revalidate", string? accessControlAllowOrigin = "*", IEnumerable<KeyValuePair<string, string>>? additionalHeaders = null, Func<byte[]>? body = null)
    {
        if (PreprocessRedirection(request, response) || PreprocessStashAction(request, response, stash))
            return;

        if (accessControlAllowOrigin is not null)
        {
            response.Add("Access-Control-Allow-Origin", accessControlAllowOrigin);
        }

        response.Add("Content-Type", contentType);
        response.Add("Cache-Control", cacheControl);
        foreach (var header in additionalHeaders ?? [])
        {
            response.Add(header.Key, header.Value);
        }

        response.Body = body is not null ? body() : Encoding.UTF8.GetBytes(payload(SubresourceHeadersJson(request)));
    }

    /// <summary>
    /// The subresources that store the headers of their request in the stash (the <c>id</c> parameter), so that the test gets
    /// them with a request with the <c>report-headers</c> parameter (image.py, font.py).
    /// </summary>
    private static void ReportingSubresource(WptHandlerRequest request, WptHandlerResponse response, WptStash stash, string contentType, Func<string, byte[]> payload)
    {
        if (request.Has("report-headers"))
        {
            Subresource(request, response, stash, "application/json", _ => stash.TakeValue(request.Get("id") ?? "", request.Path) as string ?? "None");
            return;
        }

        string? data = null;
        Subresource(request, response, stash, contentType, headers =>
        {
            data = "{\"headers\": " + headers + "}";
            if (request.Get("id") is { } id)
            {
                stash.Put(id, data, request.Path);
            }

            return "";
        });

        if (data is not null)
        {
            response.Body = payload(data);
        }
    }

    // subresource/svg.py: an empty SVG image (whose request headers are stashed), or an SVG document whose style sheet
    // (stylesheet.py) references svg.py with the property of the query
    private static void SubresourceSvg(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        if (request.Has("report-headers"))
        {
            Subresource(request, response, stash, "application/json", _ => stash.TakeValue(request.Get("id") ?? "", request.Path) as string ?? "None");
            return;
        }

        Subresource(request, response, stash, "image/svg+xml", headers =>
        {
            if (request.Has("embedded-svg"))
            {
                return SubresourceTemplate("svg.embedded.template")
                    .Replace("%(id)s", DecodeUtf8(request.Get("id") ?? ""), StringComparison.Ordinal)
                    .Replace("%(property)s", DecodeUtf8(request.Get("property") ?? ""), StringComparison.Ordinal);
            }

            if (request.Get("id") is { } id)
            {
                stash.TakeValue(id, request.Path);
                stash.Put(id, "{\"headers\": " + headers + "}", request.Path);
            }

            return "<svg xmlns='http://www.w3.org/2000/svg'></svg>";
        });
    }

    // subresource/stylesheet.py: a style sheet that loads a nested resource (image, font, svg) whose request headers are
    // stashed, or that imports itself from the other origin (import-rule). The Referrer-Policy of the response is the
    // referrer-policy parameter (unsafe-url by default).
    private static void SubresourceStylesheet(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var referrerPolicy = request.Get("referrer-policy") ?? "unsafe-url";
        if (request.Has("report-headers"))
        {
            Subresource(request, response, stash, "application/json", _ => stash.TakeValue(request.Get("id") ?? "", request.Path) as string ?? "None", additionalHeaders: [new("Referrer-Policy", referrerPolicy)]);
            return;
        }

        Subresource(request, response, stash, "text/css", headers =>
        {
            if (request.Has("import-rule"))
                return "@import url('" + CreateSubresourceUrl(request, swapOrigin: true, queryParameterToRemove: "import-rule") + "');";

            var id = DecodeUtf8(request.Get("id") ?? "");
            if (request.Get("id") is { } stashId)
            {
                stash.Put(stashId, "{\"headers\": " + headers + "}", request.Path);
            }

            return (request.Get("type") ?? "image") switch
            {
                "image" => SubresourceTemplate("image.css.template").Replace("%(id)s", id, StringComparison.Ordinal),
                "font" => SubresourceTemplate("font.css.template").Replace("%(id)s", id, StringComparison.Ordinal),
                "svg" => SubresourceTemplate("svg.css.template").Replace("%(id)s", id, StringComparison.Ordinal).Replace("%(property)s", DecodeUtf8(request.Get("property") ?? ""), StringComparison.Ordinal),
                _ => "",
            };
        }, additionalHeaders: [new("Referrer-Policy", referrerPolicy)]);
    }

    // subresource/static-import.py: a module importing the import_url parameter, with the Content-Security-Policy of the
    // contentSecurityPolicy parameter
    private static void StaticImport(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var headers = new List<KeyValuePair<string, string>>();
        if (request.Get("contentSecurityPolicy") is { } csp)
        {
            headers.Add(new("Content-Security-Policy", Uri.UnescapeDataString(DecodeUtf8(csp)) switch
            {
                "script-src-wildcard" => "script-src * 'unsafe-inline'",
                "script-src-self" => "script-src 'self' 'unsafe-inline'",
                "script-src-none" => "script-src 'none'",
                "worker-src-wildcard" => "worker-src *",
                "worker-src-self" => "worker-src 'self'",
                "worker-src-none" => "worker-src 'none'",
                var value => throw new InvalidOperationException("Invalid delivery_value: " + value),
            }));
        }

        Subresource(request, response, stash, "application/javascript", _ => SubresourceTemplate("static-import.js.template").Replace("%(import_url)s", Uri.UnescapeDataString(DecodeUtf8(request.Get("import_url") ?? "")), StringComparison.Ordinal), additionalHeaders: headers);
    }

    // scope/document.py
    private static void ScopeDocument(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var meta = "";
        var error = "";
        response.Add("Access-Control-Allow-Origin", "*");
        response.Add("Content-Type", "text/html");
        response.Add("Cache-Control", "no-cache; must-revalidate");
        using (var deliveries = JsonDocument.Parse(DecodeUtf8(request.Get("policyDeliveries") ?? "[]")))
        {
            foreach (var delivery in deliveries.RootElement.EnumerateArray())
            {
                var key = delivery.GetProperty("key").GetString();
                var value = delivery.GetProperty("value").GetString() ?? "";
                switch (delivery.GetProperty("deliveryType").GetString())
                {
                    case "meta" when key == "referrerPolicy":
                        meta += "<meta name=\"referrer\" content=\"" + value + "\">";
                        break;
                    case "http-rp" when key == "referrerPolicy":
                        response.Add("Referrer-Policy", value);
                        break;
                    case "meta" or "http-rp":
                        error = "invalid delivery key";
                        break;
                    default:
                        error = "invalid deliveryType";
                        break;
                }
            }
        }

        var template = File.ReadAllText(Path.Combine(WptPaths.Wpt, "common", "security-features", "scope", "template", "document.html.template"));
        response.Body = Encoding.UTF8.GetBytes(template
            .Replace("%(meta)s", meta, StringComparison.Ordinal)
            .Replace("%(error)s", error, StringComparison.Ordinal)
            .Replace("%(scriptOrigin)s", request.Url.Protocol + "//" + request.Url.Hostname + ":" + (request.Url.Port.Length > 0 ? request.Url.Port : request.Url.Protocol == "https:" ? "443" : "80"), StringComparison.Ordinal));
    }

    private static void ScopeWorker(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var error = "";
        response.Add("Access-Control-Allow-Origin", "*");
        response.Add("Content-Type", "text/javascript");
        response.Add("Cache-Control", "no-cache; must-revalidate");
        using (var deliveries = JsonDocument.Parse(DecodeUtf8(request.Get("policyDeliveries") ?? "[]")))
        {
            foreach (var delivery in deliveries.RootElement.EnumerateArray())
            {
                var key = delivery.GetProperty("key").GetString();
                var value = delivery.GetProperty("value").GetString() ?? "";
                switch (delivery.GetProperty("deliveryType").GetString())
                {
                    case "meta":
                        error = "<meta> cannot be used in WorkerGlobalScope";
                        break;
                    case "http-rp" when key == "referrerPolicy":
                        response.Add("Referrer-Policy", value);
                        break;
                    case "http-rp" when key == "mixedContent" && value == "opt-in":
                        response.Add("Content-Security-Policy", "block-all-mixed-content");
                        break;
                    case "http-rp" when key == "upgradeInsecureRequests" && value == "upgrade":
                        response.Add("Content-Security-Policy", "upgrade-insecure-requests");
                        break;
                    case "http-rp":
                        error = "invalid delivery key for http-rp: " + key;
                        break;
                    default:
                        error = "invalid deliveryType: " + delivery.GetProperty("deliveryType").GetString();
                        break;
                }
            }
        }

        var commonJsUrl = request.Url.Protocol + "//" + request.Url.Hostname + ":" + (request.Url.Port.Length > 0 ? request.Url.Port : request.Url.Protocol == "https:" ? "443" : "80") + "/common/security-features/resources/common.sub.js";
        var import = (request.Get("type") ?? "classic") == "classic" ? "importScripts(\"" + commonJsUrl + "\");" : "import \"" + commonJsUrl + "\";";
        var template = File.ReadAllText(Path.Combine(WptPaths.Wpt, "common", "security-features", "scope", "template", "worker.js.template"));
        response.Body = Encoding.UTF8.GetBytes(template
            .Replace("%(import)s", import, StringComparison.Ordinal)
            .Replace("%(error)s", error, StringComparison.Ordinal));
    }

    /// <summary>
    /// The headers of the request as the JSON object of <c>subresource.respond()</c>: the names are lowercase, and the value
    /// of a header that is repeated is an array (<c>json.dumps()</c>, so the non-ASCII characters are escaped).
    /// </summary>
    private static string SubresourceHeadersJson(WptHandlerRequest request)
    {
        var result = new StringBuilder("{");
        var first = true;
        foreach (var group in request.Headers.GroupBy(header => header.Key.ToLowerInvariant(), StringComparer.Ordinal))
        {
            if (!first)
            {
                result.Append(", ");
            }

            first = false;
            result.Append(PythonJsonString(group.Key)).Append(": ");
            var values = group.Select(header => PythonJsonString(DecodeUtf8(header.Value))).ToList();
            result.Append(values.Count == 1 ? values[0] : "[" + string.Join(", ", values) + "]");
        }

        return result.Append('}').ToString();
    }

    /// <summary><c>preprocess_redirection()</c>: a <c>301</c> response to the URL of the redirection parameter.</summary>
    private static bool PreprocessRedirection(WptHandlerRequest request, WptHandlerResponse response)
    {
        var url = request.Get("redirection") switch
        {
            null or "no-redirect" => null,
            "keep-scheme" or "keep-origin" => CreateSubresourceUrl(request),
            "swap-scheme" => CreateSubresourceUrl(request, swapScheme: true),
            "downgrade" => CreateSubresourceUrl(request, downgrade: true),
            "swap-origin" => CreateSubresourceUrl(request, swapOrigin: true),
            var value => throw new InvalidOperationException("Invalid redirection type '" + value + "'"),
        };

        if (url is null)
            return false;

        response.Status = 301;
        response.Add("Access-Control-Allow-Origin", "*");
        response.Add("Location", url);
        return true;
    }

    /// <summary>
    /// <c>create_url()</c>: the URL of the request, with the other scheme (on the HTTPS port), the HTTP scheme on the port of
    /// the scheme (downgrade), or the other origin (the <c>www1</c> subdomain), without the <paramref name="queryParameterToRemove"/> parameter.
    /// </summary>
    private static string CreateSubresourceUrl(WptHandlerRequest request, bool swapScheme = false, bool swapOrigin = false, bool downgrade = false, string queryParameterToRemove = "redirection")
    {
        var url = request.Url;
        var scheme = url.Protocol.TrimEnd(':');
        var netloc = url.Host;
        if (swapScheme)
        {
            scheme = scheme == "https" ? "http" : "https";
            netloc = url.Hostname + ":8443";
        }

        if (downgrade)
        {
            (scheme, var port) = scheme switch
            {
                "https" => ("http", "8443"),
                "wss" => ("ws", "8889"),
                _ => throw new InvalidOperationException("Downgrade redirection: Invalid scheme '" + scheme + "'"),
            };
            netloc = url.Hostname + ":" + port;
        }

        if (swapOrigin)
        {
            netloc = netloc.StartsWith("www1.", StringComparison.Ordinal) ? netloc["www1.".Length..] : "www1." + netloc;
        }

        // urlencode() of the remaining parameters (quote_plus)
        var query = string.Join('&', request.GetParameters.Where(parameter => parameter.Key != queryParameterToRemove).Select(parameter => PythonQuotePlus(parameter.Key) + "=" + PythonQuotePlus(parameter.Value)));
        return scheme + "://" + netloc + url.Pathname + (query.Length > 0 ? "?" + query : "");
    }

    /// <summary><c>urllib.parse.quote_plus()</c> of a byte string.</summary>
    private static string PythonQuotePlus(string value)
    {
        var result = new StringBuilder();
        foreach (var b in Encoding.Latin1.GetBytes(value))
        {
            if (b is (>= (byte)'a' and <= (byte)'z') or (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'0' and <= (byte)'9') or (byte)'_' or (byte)'.' or (byte)'-' or (byte)'~')
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

    /// <summary><c>preprocess_stash_action()</c>: the put, purge, check-scheme and take actions of the query.</summary>
    private static bool PreprocessStashAction(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        if (request.Get("action") is not { } action)
            return false;

        var key = request.Get("key") ?? "";
        var path = request.Get("path") ?? request.Url.Href.Split('?')[0];
        string data;
        switch (action)
        {
            case "put":
                stash.TakeValue(key, path);
                stash.Put(key, DecodeUtf8(request.Get("value") ?? ""), path);
                data = "{\"status\": \"success\", \"result\": " + PythonJsonString(DecodeUtf8(key)) + "}";
                break;
            case "purge":
                stash.TakeValue(key, path);
                return false;
            case "check-scheme":
                if (request.Url.Protocol is "https:" or "wss:")
                {
                    stash.TakeValue(key, path);
                    stash.Put(key, "upgraded", path);
                }

                return false;
            case "take":
                var value = stash.TakeValue(key, path) as string;
                var status = value switch
                {
                    "upgraded" => "upgraded",
                    null => "allowed",
                    _ => "blocked",
                };
                data = "{\"status\": \"" + status + "\", \"result\": " + (value is null ? "null" : PythonJsonString(value)) + "}";
                break;
            default:
                return false;
        }

        response.Add("Content-Type", "text/javascript");
        response.Add("Cache-Control", "no-cache; must-revalidate");
        response.Body = Encoding.UTF8.GetBytes(data);
        return true;
    }

    /// <summary>
    /// <c>encode_string_as_bmp_image()</c> (subresource/image.py): each byte of the UTF-8 string is encoded in three pixels
    /// (one bit per channel), in a square 24-bit BMP image.
    /// </summary>
    private static byte[] EncodeStringAsBmpImage(string data)
    {
        var colors = new List<(byte R, byte G, byte B)>();
        foreach (var b in Encoding.UTF8.GetBytes(data))
        {
            byte Bit(int index) => (b & (0x80 >> index)) != 0 ? (byte)255 : (byte)0;
            colors.Add((Bit(0), Bit(1), Bit(2)));
            colors.Add((Bit(3), Bit(4), Bit(5)));
            colors.Add((Bit(6), Bit(7), 0));
        }

        var size = (int)Math.Ceiling(Math.Sqrt(colors.Count));
        var pixels = new byte[3 * size * size];
        for (var i = 0; i < colors.Count; i++)
        {
            // The pixels are stored as BGR
            pixels[(i * 3) + 0] = colors[i].B;
            pixels[(i * 3) + 1] = colors[i].G;
            pixels[(i * 3) + 2] = colors[i].R;
        }

        var padding = (4 - (size * 3 % 4)) % 4;
        using var stream = new MemoryStream();
        var fileSize = 54 + (3 * size * size);
        stream.Write([(byte)'B', (byte)'M']);
        stream.Write(BitConverter.GetBytes(fileSize));
        stream.Write([0, 0, 0, 0, 54, 0, 0, 0]);
        stream.Write([40, 0, 0, 0]);
        stream.Write(BitConverter.GetBytes(size));
        stream.Write(BitConverter.GetBytes(size));
        stream.Write([1, 0, 24]);
        stream.Write(new byte[25]);
        for (var row = 0; row < size; row++)
        {
            // The rows are stored bottom-up
            stream.Write(pixels.AsSpan(size * (size - row - 1) * 3, 3 * size));
            stream.Write(new byte[padding]);
        }

        return stream.ToArray();
    }

    /// <summary>The TrueType font of subresource/font.py.</summary>
    private const string SubresourceFont =
        "AAEAAAANAIAAAwBQRkZUTU6u6MkAAAXcAAAAHE9TLzJWYW" +
        "QKAAABWAAAAFZjbWFwAA8D7wAAAcAAAAFCY3Z0IAAhAnkA" +
        "AAMEAAAABGdhc3D//wADAAAF1AAAAAhnbHlmCC6aTwAAAx" +
        "QAAACMaGVhZO8ooBcAAADcAAAANmhoZWEIkAV9AAABFAAA" +
        "ACRobXR4EZQAhQAAAbAAAAAQbG9jYQBwAFQAAAMIAAAACm" +
        "1heHAASQA9AAABOAAAACBuYW1lehAVOgAAA6AAAAIHcG9z" +
        "dP+uADUAAAWoAAAAKgABAAAAAQAAMhPyuV8PPPUACwPoAA" +
        "AAAMU4Lm0AAAAAxTgubQAh/5wFeAK8AAAACAACAAAAAAAA" +
        "AAEAAAK8/5wAWgXcAAAAAAV4AAEAAAAAAAAAAAAAAAAAAA" +
        "AEAAEAAAAEAAwAAwAAAAAAAgAAAAEAAQAAAEAALgAAAAAA" +
        "AQXcAfQABQAAAooCvAAAAIwCigK8AAAB4AAxAQIAAAIABg" +
        "kAAAAAAAAAAAABAAAAAAAAAAAAAAAAUGZFZABAAEEAQQMg" +
        "/zgAWgK8AGQAAAABAAAAAAAABdwAIQAAAAAF3AAABdwAZA" +
        "AAAAMAAAADAAAAHAABAAAAAAA8AAMAAQAAABwABAAgAAAA" +
        "BAAEAAEAAABB//8AAABB////wgABAAAAAAAAAQYAAAEAAA" +
        "AAAAAAAQIAAAACAAAAAAAAAAAAAAAAAAAAAQAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAwAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAhAnkAAAAqACoAKgBGAAAAAgAhAA" +
        "ABKgKaAAMABwAusQEALzyyBwQA7TKxBgXcPLIDAgDtMgCx" +
        "AwAvPLIFBADtMrIHBgH8PLIBAgDtMjMRIREnMxEjIQEJ6M" +
        "fHApr9ZiECWAAAAwBk/5wFeAK8AAMABwALAAABNSEVATUh" +
        "FQE1IRUB9AH0/UQDhPu0BRQB9MjI/tTIyP7UyMgAAAAAAA" +
        "4ArgABAAAAAAAAACYATgABAAAAAAABAAUAgQABAAAAAAAC" +
        "AAYAlQABAAAAAAADACEA4AABAAAAAAAEAAUBDgABAAAAAA" +
        "AFABABNgABAAAAAAAGAAUBUwADAAEECQAAAEwAAAADAAEE" +
        "CQABAAoAdQADAAEECQACAAwAhwADAAEECQADAEIAnAADAA" +
        "EECQAEAAoBAgADAAEECQAFACABFAADAAEECQAGAAoBRwBD" +
        "AG8AcAB5AHIAaQBnAGgAdAAgACgAYwApACAAMgAwADAAOA" +
        "AgAE0AbwB6AGkAbABsAGEAIABDAG8AcgBwAG8AcgBhAHQA" +
        "aQBvAG4AAENvcHlyaWdodCAoYykgMjAwOCBNb3ppbGxhIE" +
        "NvcnBvcmF0aW9uAABNAGEAcgBrAEEAAE1hcmtBAABNAGUA" +
        "ZABpAHUAbQAATWVkaXVtAABGAG8AbgB0AEYAbwByAGcAZQ" +
        "AgADIALgAwACAAOgAgAE0AYQByAGsAQQAgADoAIAA1AC0A" +
        "MQAxAC0AMgAwADAAOAAARm9udEZvcmdlIDIuMCA6IE1hcm" +
        "tBIDogNS0xMS0yMDA4AABNAGEAcgBrAEEAAE1hcmtBAABW" +
        "AGUAcgBzAGkAbwBuACAAMAAwADEALgAwADAAMAAgAABWZX" +
        "JzaW9uIDAwMS4wMDAgAABNAGEAcgBrAEEAAE1hcmtBAAAA" +
        "AgAAAAAAAP+DADIAAAABAAAAAAAAAAAAAAAAAAAAAAAEAA" +
        "AAAQACACQAAAAAAAH//wACAAAAAQAAAADEPovuAAAAAMU4" +
        "Lm0AAAAAxTgubQ==";
}
