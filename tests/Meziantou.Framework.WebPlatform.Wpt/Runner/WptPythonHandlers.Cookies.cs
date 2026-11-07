using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

// The handlers of the cookies and cookiestore tests (cookies/resources/*.py, cookiestore/resources/cookie_helper.py...)
internal static partial class WptPythonHandlers
{
    private static readonly Dictionary<string, Handler> CookieHandlers = new(StringComparer.Ordinal)
    {
        ["/cookies/resources/cookie.py"] = CookiePy,
        ["/cookies/resources/drop.py"] = (request, response, _) =>
        {
            SetNoCacheAndCorsHeaders(request, response);
            try
            {
                var name = ReadParameter(request, "name");
                response.Add("Set-Cookie", MakeDropCookie(name, request.Url.Protocol == "https:"));
                response.Content = """{"success": true}""";
            }
            catch (InvalidOperationException)
            {
                response.Status = 500;
                response.Content = """{"error" : "Empty or missing name parameter."}""";
            }
        },
        ["/cookies/resources/dropSameSite.py"] = (request, response, _) => DropCookies(request, response, ("samesite_strict", false), ("samesite_lax", false), ("samesite_none", false), ("samesite_unspecified", false)),
        ["/cookies/resources/dropSameSiteMultiAttribute.py"] = (request, response, _) => DropCookies(request, response,
            ("samesite_unsupported", true), ("samesite_unsupported_none", true), ("samesite_unsupported_lax", false), ("samesite_unsupported_strict", false),
            ("samesite_none_unsupported", true), ("samesite_lax_unsupported", true), ("samesite_strict_unsupported", true), ("samesite_lax_none", true)),
        ["/cookies/resources/dropSameSiteNone.py"] = (request, response, _) => DropCookies(request, response, ("samesite_none_insecure", false), ("samesite_none_secure", true)),
        ["/cookies/resources/dropSecure.py"] = (request, response, _) => DropCookies(request, response, ("alone_secure", false), ("alone_insecure", false)),
        ["/cookies/resources/echo-json.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "application/json");
            response.Add("Access-Control-Allow-Credentials", "true");
            if (request.Header("Origin") is { } origin)
            {
                response.Add("Access-Control-Allow-Origin", origin);
            }

            // The values are the CookieValue objects, whose string is the output string of their morsel ("name=value")
            var values = ParseRequestCookies(request).Select(cookie => $"\"{cookie.Key}\": \"{cookie.Key}={cookie.Value}\"");
            response.Body = Encoding.UTF8.GetBytes("{ " + string.Join(",", values) + " }");
        },
        ["/cookies/resources/imgIfMatch.py"] = (request, response, _) =>
        {
            SetNoCacheAndCorsHeaders(request, response);
            var name = ReadParameter(request, "name");
            var value = ReadParameter(request, "value");
            if (ParseRequestCookies(request).Any(cookie => cookie.Key == name && cookie.Value == value))
            {
                response.Add("Content-Type", "image/gif");
                response.Body = Convert.FromBase64String("R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw==");
                return;
            }

            response.Status = 500;
            response.Content = """{"error": {"message": "The cookie's value did not match the given value."}}""";
        },
        ["/cookies/resources/list.py"] = (request, response, _) =>
        {
            SetNoCacheAndCorsHeaders(request, response);
            response.Content = PythonJsonDump(ReadCookies(request));
        },
        ["/cookies/resources/list-cookies-for-script.py"] = (request, response, _) =>
        {
            SetNoCacheAndCorsHeaders(request, response);
            response.Set("Content-Type", "text/javascript");
            response.Body = Encoding.UTF8.GetBytes("self._cookies = [" + string.Join(", ", ReadCookies(request).Select(cookie => "\"" + cookie.Key + "\"")) + "];\n");
        },
        ["/cookies/resources/postToParent.py"] = (request, response, _) =>
        {
            SetNoCacheAndCorsHeaders(request, response);
            response.Add("Content-Type", "text/html; charset=utf-8");
            response.Body = Encoding.UTF8.GetBytes($$"""

                <!DOCTYPE html>
                <script>
                  var data = {{PythonJsonDump(ReadCookies(request))}};
                  data.type = "COOKIES";

                  try {
                    data.domcookies = document.cookie;
                  } catch (e) {}

                  if (window.parent != window) {
                    window.parent.postMessage(data, "*");
                    if (window.top != window.parent)
                      window.top.postMessage(data, "*");
                  }


                  if (window.opener)
                    window.opener.postMessage(data, "*");

                  window.addEventListener("message", e => {
                    console.log(e);
                    if (e.data == "reload")
                      window.location.reload();
                  });
                </script>

                """);
        },
        ["/cookies/resources/redirectWithCORSHeaders.py"] = (request, response, _) =>
        {
            response.Status = ParseInt(request.Get("status"), 302);
            SetNoCacheAndCorsHeaders(request, response);
            response.Add("Location", request.Get("location") ?? throw new InvalidOperationException("Missing location"));
        },
        ["/cookies/resources/set-cookie.py"] = (request, response, _) =>
        {
            var name = request.Get("name") ?? throw new InvalidOperationException("Missing name");
            var path = request.Get("path") ?? throw new InvalidOperationException("Missing path");
            var expiryYear = DateTime.UtcNow.Year + 1;
            var cookie = $"{name}=1; Path={path}; Expires=09 Jun {expiryYear} 10:18:14 GMT";
            if (request.Get("samesite") is { Length: > 0 } sameSite)
            {
                cookie += ";SameSite=" + sameSite;
            }

            if (request.Has("secure"))
            {
                cookie += ";Secure";
            }

            response.Add("Content-Type", "application/json");
            response.Add("Set-Cookie", cookie);
            if (request.Header("Origin") is { } origin && origin != "null")
            {
                response.Add("Access-Control-Allow-Origin", origin);
                response.Add("Access-Control-Allow-Credentials", "true");
            }

            response.Content = "var dummy='value';";
        },
        ["/cookies/resources/set.py"] = (request, response, _) =>
        {
            SetNoCacheAndCorsHeaders(request, response);
            response.Add("Set-Cookie", IsomorphicEncode(PythonUnquote(request.Query)));
            response.Content = """{"success": true}""";
        },
        ["/cookies/resources/setSameSite.py"] = (request, response, _) => SetSameSiteCookies(request, response, domain: null),
        ["/cookies/resources/setSameSiteDomain.py"] = (request, response, _) => SetSameSiteCookies(request, response, domain: (request.Header("Host") ?? throw new InvalidOperationException("Missing Host header")).Split(':')[0]),
        ["/cookies/resources/setSameSiteMultiAttribute.py"] = (request, response, _) =>
        {
            SetNoCacheAndCorsHeaders(request, response);
            var value = request.Query;
            response.Add("Content-Type", "text/html; charset=utf-8");

            // A Python dictionary keeps the last value of a duplicate key, at the position of its first occurrence
            response.Add("Set-Cookie", MakeCookieHeader("samesite_unsupported", value, ("SameSite", "Unsupported"), ("path", "/"), ("Secure", "")));
            response.Add("Set-Cookie", MakeCookieHeader("samesite_unsupported_none", value, ("SameSite", "None"), ("path", "/"), ("Secure", "")));
            response.Add("Set-Cookie", MakeCookieHeader("samesite_unsupported_lax", value, ("SameSite", "Lax"), ("path", "/")));
            response.Add("Set-Cookie", MakeCookieHeader("samesite_unsupported_strict", value, ("SameSite", "Strict"), ("path", "/")));
            response.Add("Set-Cookie", MakeCookieHeader("samesite_none_unsupported", value, ("SameSite", "Unsupported"), ("path", "/"), ("Secure", "")));
            response.Add("Set-Cookie", MakeCookieHeader("samesite_lax_unsupported", value, ("SameSite", "Unsupported"), ("path", "/"), ("Secure", "")));
            response.Add("Set-Cookie", MakeCookieHeader("samesite_strict_unsupported", value, ("SameSite", "Unsupported"), ("path", "/"), ("Secure", "")));
            response.Add("Set-Cookie", MakeCookieHeader("samesite_lax_none", value, ("SameSite", "None"), ("path", "/"), ("Secure", "")));
            response.Add("Set-Cookie", MakeCookieHeader("samesite_lax_strict", value, ("SameSite", "Strict"), ("path", "/")));
            response.Add("Set-Cookie", MakeCookieHeader("samesite_strict_lax", value, ("SameSite", "Lax"), ("path", "/")));
            response.Content = SameSiteNavigationDocument;
        },
        ["/cookies/resources/setSameSiteNone.py"] = (request, response, _) =>
        {
            SetNoCacheAndCorsHeaders(request, response);
            var value = request.Query;
            response.Add("Set-Cookie", MakeCookieHeader("samesite_none_insecure", value, ("SameSite", "None"), ("path", "/")));
            response.Add("Set-Cookie", MakeCookieHeader("samesite_none_secure", value, ("SameSite", "None"), ("Secure", ""), ("path", "/")));
            response.Content = """{"success": true}""";
        },
        ["/cookies/resources/setSecure.py"] = (request, response, _) =>
        {
            SetNoCacheAndCorsHeaders(request, response);
            var value = request.Query;
            response.Add("Set-Cookie", MakeCookieHeader("alone_secure", value, ("secure", ""), ("path", "/")));
            response.Add("Set-Cookie", MakeCookieHeader("alone_insecure", value, ("path", "/")));
            response.Content = """{"success": true}""";
        },
        ["/cookies/path/resources/echo.py"] = (request, response, _) =>
        {
            response.Add("Content-Type", "text/plain");
            response.Add("Cache-Control", "no-store");
            response.Add("Access-Control-Allow-Origin", "*");
            response.Content = request.Header("Cookie") ?? "";
        },
        ["/cookies/partitioned-cookies/resources/redirect-and-echo-cookie-header.py"] = (request, response, _) =>
        {
            response.Status = 302;
            var location = request.Get("location") ?? throw new InvalidOperationException("Missing location");
            response.Set("Location", location + "?cookies=" + (request.Header("Cookie") ?? ""));
        },
        ["/cookies/domain/support/idn.py"] = IdnPy,
        ["/cookiestore/resources/cookie_helper.py"] = CookieHelperPy,

        // The cookie checks of the credentials tests of the module scripts (html/semantics/scripting-1/the-script-element)
        ["/html/semantics/scripting-1/the-script-element/module/resources/check-cookie.py"] = (request, response, _) =>
        {
            ModuleCredentialsHeaders(request, response, "text/javascript");
            var identifier = request.Get("id");
            var cookieName = request.Get("cookieName");
            if (identifier is null || cookieName is null)
                return;

            var cookie = ParseRequestCookies(request).Find(cookie => cookie.Key == cookieName);
            var result = cookie.Key is null ? "not found" : cookie.Value == "1" ? "found" : "different value: " + cookie.Value;
            response.Content = "window." + identifier + " = '" + result + "';";
        },
        ["/html/semantics/scripting-1/the-script-element/css-module/resources/cross-origin.py"] = (request, response, _) =>
        {
            ModuleCredentialsHeaders(request, response, "text/css");
            response.Content = HasMilkCookie(request) ? ".requestHadCookies { }" : ".requestDidNotHaveCookies { }";
        },
        ["/html/semantics/scripting-1/the-script-element/json-module/cross-origin.py"] = (request, response, _) =>
        {
            ModuleCredentialsHeaders(request, response, "application/json");
            response.Content = HasMilkCookie(request) ? """{"requestHadCookies": true}""" : """{"requestHadCookies": false}""";
        },
        ["/html/semantics/scripting-1/the-script-element/text-module/cross-origin.py"] = (request, response, _) =>
        {
            ModuleCredentialsHeaders(request, response, "text/plain");
            response.Content = HasMilkCookie(request) ? "request had cookies" : "request did not have cookies";
        },
    };

    private static void ModuleCredentialsHeaders(WptHandlerRequest request, WptHandlerResponse response, string contentType)
    {
        response.Add("Content-Type", contentType);
        response.Add("Access-Control-Allow-Origin", request.Get("origin") ?? throw new InvalidOperationException("Missing origin"));
        response.Add("Access-Control-Allow-Credentials", "true");
    }

    private static bool HasMilkCookie(WptHandlerRequest request) => ParseRequestCookies(request).Find(cookie => cookie.Key == "milk").Value == "1";

    /// <summary>The document of the setSameSite*.py handlers: a same-site navigation to a page that posts the cookies to its opener.</summary>
    private const string SameSiteNavigationDocument = """

        <!DOCTYPE html>
        <script>
          // A same-site navigation, which should attach all cookies including SameSite ones.
          // This is necessary because this page may have been reached via a cross-site navigation, so
          // we might not have access to some SameSite cookies from here.
          window.location = "../samesite/resources/echo-cookies.html";
        </script>

        """;

    /// <summary>cookies/resources/cookie.py: sets the cookies of the JSON <c>set</c> parameter, and redirects to <c>location</c>.</summary>
    private static void CookiePy(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        SetNoCacheAndCorsHeaders(request, response);
        if (request.Get("set") is { } set)
        {
            // The parameter is a byte string decoded as ISO-8859-1, so the strings of the JSON are the bytes of the cookies
            using var json = JsonDocument.Parse(set);
            var cookies = json.RootElement.ValueKind == JsonValueKind.Array ? json.RootElement.EnumerateArray().Select(e => e.GetString()!).ToList() : [json.RootElement.GetString()!];
            foreach (var cookie in cookies)
            {
                response.Add("Set-Cookie", IsomorphicEncode(cookie));
            }
        }

        if (request.Get("location") is { } location)
        {
            response.Add("Location", location);
            response.Status = 302;
            response.Content = """{"redirect": true}""";
            return;
        }

        response.Content = """{"success": true}""";
    }

    /// <summary>cookies/domain/support/idn.py: sets and deletes cookies whose Domain attribute is an internationalized domain name.</summary>
    private static void IdnPy(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        var host = request.Get("host");
        var utf8Subdomain = "Domain=" + Encoding.Latin1.GetString(Encoding.UTF8.GetBytes("élève."));
        var utf8DotSubdomain = "Domain=" + Encoding.Latin1.GetString(Encoding.UTF8.GetBytes("élève。"));
        const string PunycodeSubdomain = "Domain=xn--lve-6lad.";
        var wrongUtf8Subdomain = "Domain=" + Encoding.Latin1.GetString(Encoding.UTF8.GetBytes("ÿlève."));
        const string WrongPunycodeSubdomain = "Domain=xn--lve-6la7i.";
        var invalidByteSubdomain = "Domain=" + Encoding.Latin1.GetString(Encoding.UTF8.GetBytes("élève")) + "ÿ.";

        void SetCookie(string cookie, string content)
        {
            response.Add("Set-Cookie", cookie + host);
            response.Content = content;
        }

        // The first "if" is separate from the "elif" chain
        if (request.Has("set-utf8"))
        {
            SetCookie("utf8=set;" + utf8Subdomain, "set");
        }

        if (request.Has("set-utf8-dot"))
        {
            SetCookie("utf8-dot=set;" + utf8DotSubdomain, "set");
        }
        else if (request.Has("set-wrong-utf8"))
        {
            SetCookie("wrong-utf8=set;" + wrongUtf8Subdomain, "set");
        }
        else if (request.Has("set-punycode"))
        {
            SetCookie("punycode=set;" + PunycodeSubdomain, "set");
        }
        else if (request.Has("set-wrong-punycode"))
        {
            SetCookie("wrong-punycode=set;" + WrongPunycodeSubdomain, "set");
        }
        else if (request.Has("set-invalid-byte"))
        {
            SetCookie("invalid-byte=set;" + invalidByteSubdomain, "set");
        }
        else if (request.Has("get"))
        {
            response.Content = request.Header("Cookie") ?? "no cookies";
        }
        else if (request.Has("delete-utf8"))
        {
            SetCookie("utf8=unset;Max-Age=0;" + utf8Subdomain, "delete");
        }
        else if (request.Has("delete-utf8-dot"))
        {
            SetCookie("utf8-dot=unset;Max-Age=0;" + utf8DotSubdomain, "delete");
        }
        else if (request.Has("delete-wrong-utf8"))
        {
            SetCookie("wrong-utf8=unset;Max-Age=0;" + wrongUtf8Subdomain, "delete");
        }
        else if (request.Has("delete-punycode"))
        {
            SetCookie("punycode=unset;Max-Age=0;" + PunycodeSubdomain, "delete");
        }
        else if (request.Has("delete-wrong-punycode"))
        {
            SetCookie("wrong-punycode=unset;Max-Age=0;" + WrongPunycodeSubdomain, "delete");
        }
        else if (request.Has("delete-invalid-byte"))
        {
            SetCookie("invalid-byte=unset;Max-Age=0;" + invalidByteSubdomain, "delete");
        }

        response.Add("Content-Type", "text/plain");
    }

    /// <summary>
    /// cookiestore/resources/cookie_helper.py: a POST request sets the cookies of its <c>set-cookie</c> fields and echoes
    /// them, a GET request echoes its <c>Cookie</c> header (percent-encoded in the <c>charset</c> parameter).
    /// </summary>
    private static void CookieHelperPy(WptHandlerRequest request, WptHandlerResponse response, WptStash stash)
    {
        if (request.Method is not ("GET" or "POST"))
            throw new InvalidOperationException("request method was neither GET nor POST");

        var queryData = request.Query;
        if (request.Method == "POST")
        {
            queryData += "&" + Encoding.Latin1.GetString(request.Body);
        }

        var arguments = ParseQueryString(queryData);
        var charset = arguments.LastOrDefault(argument => argument.Key == "charset").Value ?? "utf-8";
        charset = charset.ToLowerInvariant() switch
        {
            "utf-8" or "utf8" => "utf-8",
            "iso-8859-1" or "latin-1" or "latin1" => "iso-8859-1",
            _ => throw new InvalidOperationException("Unsupported charset " + charset),
        };

        response.Add("content-type", "text/plain; charset=" + charset);
        var body = new List<string>();
        if (request.Method == "POST")
        {
            foreach (var setCookie in arguments.Where(argument => argument.Key == "set-cookie").Select(argument => argument.Value))
            {
                var rest = setCookie;
                if (setCookie.Split(';', 2)[0].Contains('=', StringComparison.Ordinal))
                {
                    var parts = setCookie.Split('=', 2);
                    if (parts[0].Any(c => c is ';' or <= '\x1F' or '\x7F'))
                        throw new InvalidOperationException("name had disallowed characters");

                    rest = parts[1];
                }

                if (rest.Any(c => c is <= '\x1F' or '\x7F'))
                    throw new InvalidOperationException("rest had disallowed characters");

                response.Add("set-cookie", setCookie);
                body.Add("set-cookie=" + PythonQuote(setCookie));
            }
        }
        else if (request.Header("Cookie") is { } cookie)
        {
            body.Add("cookie=" + PythonQuote(cookie));
        }

        var content = string.Join("\r\n", body);
        response.Add("content-length", content.Length.ToString(CultureInfo.InvariantCulture));
        response.Content = content;

        // urllib.parse.parse_qs(..., keep_blank_values=True, encoding='iso-8859-1'): the names and values are byte strings
        static List<KeyValuePair<string, string>> ParseQueryString(string query)
        {
            var result = new List<KeyValuePair<string, string>>();
            foreach (var part in query.Split('&', ';'))
            {
                if (part.Length == 0)
                    continue;

                var index = part.IndexOf('=', StringComparison.Ordinal);
                if (index < 0)
                    continue;

                result.Add(new(Decode(part[..index]), Decode(part[(index + 1)..])));
            }

            return result;

            static string Decode(string value) => Encoding.Latin1.GetString(PercentEncoding.PercentDecode(value.Replace('+', ' ')));
        }

        // urllib.parse.quote(value, safe='', encoding='iso-8859-1') of a byte string decoded as ISO-8859-1
        static string PythonQuote(string value)
        {
            var result = new StringBuilder();
            foreach (var c in value)
            {
                if (char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-' or '~')
                {
                    result.Append(c);
                }
                else
                {
                    result.Append('%').Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
                }
            }

            return result.ToString();
        }
    }

    /// <summary>cookies/resources/helpers.py <c>setNoCacheAndCORSHeaders()</c></summary>
    private static void SetNoCacheAndCorsHeaders(WptHandlerRequest request, WptHandlerResponse response)
    {
        response.Add("Content-Type", "application/json");
        response.Add("Access-Control-Allow-Credentials", "true");
        response.Add("Access-Control-Allow-Origin", request.Header("Origin") ?? "*");
        response.Add("Cache-Control", "no-cache");
        response.Add("Expires", "Fri, 01 Jan 1990 00:00:00 GMT");
    }

    /// <summary>cookies/resources/helpers.py <c>makeCookieHeader()</c>: the attributes without value are flags.</summary>
    private static string MakeCookieHeader(string name, string value, params (string Name, string Value)[] attributes)
        => string.Join("; ", [name + "=" + value, .. attributes.Select(attribute => attribute.Value.Length == 0 ? attribute.Name : attribute.Name + "=" + attribute.Value)]);

    /// <summary>cookies/resources/helpers.py <c>makeDropCookie()</c></summary>
    private static string MakeDropCookie(string name, bool secure)
        => secure ? MakeCookieHeader(name, "", ("max-age", "0"), ("path", "/"), ("secure", "")) : MakeCookieHeader(name, "", ("max-age", "0"), ("path", "/"));

    private static void DropCookies(WptHandlerRequest request, WptHandlerResponse response, params (string Name, bool Secure)[] cookies)
    {
        SetNoCacheAndCorsHeaders(request, response);
        foreach (var (name, secure) in cookies)
        {
            response.Add("Set-Cookie", MakeDropCookie(name, secure));
        }

        response.Content = """{"success": true}""";
    }

    private static void SetSameSiteCookies(WptHandlerRequest request, WptHandlerResponse response, string? domain)
    {
        SetNoCacheAndCorsHeaders(request, response);
        var value = request.Query;
        response.Add("Content-Type", "text/html; charset=utf-8");
        (string, string)[] domainAttribute = domain is null ? [] : [("domain", domain)];
        response.Add("Set-Cookie", MakeCookieHeader("samesite_strict", value, [("SameSite", "Strict"), ("path", "/"), .. domainAttribute]));
        response.Add("Set-Cookie", MakeCookieHeader("samesite_lax", value, [("SameSite", "Lax"), ("path", "/"), .. domainAttribute]));
        response.Add("Set-Cookie", MakeCookieHeader("samesite_none", value, [("SameSite", "None"), ("path", "/"), ("Secure", ""), .. domainAttribute]));
        response.Add("Set-Cookie", MakeCookieHeader("samesite_unspecified", value, [("path", "/"), .. domainAttribute]));
        response.Content = SameSiteNavigationDocument;
    }

    /// <summary>
    /// cookies/resources/helpers.py <c>readParameter()</c>: the first value of a parameter of the query, stripped (parsed
    /// as UTF-8, then encoded as ISO-8859-1). Throws when it is missing or empty.
    /// </summary>
    private static string ReadParameter(WptHandlerRequest request, string name)
    {
        var value = request.Get(name) is { } parameter ? DecodeUtf8(parameter).Trim() : "";
        if (value.Length == 0)
            throw new InvalidOperationException("Empty or missing name parameter.");

        return IsomorphicEncode(value);
    }

    /// <summary>cookies/resources/helpers.py <c>readCookies()</c>: the last value of each cookie of the request, by name.</summary>
    private static List<KeyValuePair<string, string>> ReadCookies(WptHandlerRequest request) => ParseRequestCookies(request);

    /// <summary>
    /// The cookies of a request (<c>request.cookies</c> of wptserve): the <c>Cookie</c> header parsed by Python's
    /// <c>http.cookies.BaseCookie</c> (https://github.com/python/cpython/blob/main/Lib/http/cookies.py), whose values are
    /// byte strings (not unquoted). A cookie whose name is an attribute of a cookie (e.g. <c>path</c>) is an attribute of the
    /// previous cookie, and an invalid cookie string has no cookies. A name with an illegal character (e.g. <c>/</c>) throws,
    /// as wptserve does (the handler fails).
    /// </summary>
    private static List<KeyValuePair<string, string>> ParseRequestCookies(WptHandlerRequest request)
    {
        var header = request.Header("Cookie") ?? "";
        var parsedItems = new List<(bool IsAttribute, string Key, string? Value)>();
        var morselSeen = false;
        var index = 0;
        while (index < header.Length)
        {
            var match = PythonCookiePattern().Match(header, index);
            if (!match.Success)
                break;

            var key = match.Groups["key"].Value;
            string? value = match.Groups["val"].Success ? match.Groups["val"].Value : null;
            index = match.Index + match.Length;
            if (key[0] == '$')
            {
                if (morselSeen)
                {
                    parsedItems.Add((true, key[1..], value));
                }
            }
            else if (key.ToLowerInvariant() is "expires" or "path" or "comment" or "domain" or "max-age" or "secure" or "httponly" or "version" or "samesite")
            {
                if (!morselSeen || (value is null && key.ToLowerInvariant() is not ("secure" or "httponly")))
                    return [];

                parsedItems.Add((true, key, value));
            }
            else if (value is not null)
            {
                parsedItems.Add((false, key, value));
                morselSeen = true;
            }
            else
            {
                return [];
            }
        }

        // A dictionary: a duplicate name replaces the value, at the position of the first occurrence
        var cookies = new List<KeyValuePair<string, string>>();
        foreach (var (isAttribute, key, value) in parsedItems)
        {
            if (isAttribute)
                continue;

            if (!key.All(c => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~:".Contains(c, StringComparison.Ordinal)))
                throw new InvalidOperationException("Illegal key " + key);

            var existing = cookies.FindIndex(cookie => cookie.Key == key);
            if (existing >= 0)
            {
                cookies[existing] = new(key, value!);
            }
            else
            {
                cookies.Add(new(key, value!));
            }
        }

        return cookies;
    }

    /// <summary>The <c>_CookiePattern</c> of Python's <c>http.cookies</c> (with ASCII character classes).</summary>
    [GeneratedRegex("""\G[ \t\n\r\f\v]*(?<key>[A-Za-z0-9_!#%&'~`><@,:/$*+\-.^|)(?}{=]+?)([ \t\n\r\f\v]*=[ \t\n\r\f\v]*(?<val>"(?:[^\\"]|\\.)*"|[A-Za-z0-9_]{3},[ \t\n\r\f\v][A-Za-z0-9_ \t\n\r\f\v-]{9,11}[ \t\n\r\f\v][0-9:]{8}[ \t\n\r\f\v]GMT|[A-Za-z0-9_!#%&'~`><@,:/$*+\-.^|)(?}{=\[\]]*))?[ \t\n\r\f\v]*([ \t\n\r\f\v]+|;|$)""", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex PythonCookiePattern();

    /// <summary><c>json.dumps()</c> of a dictionary of byte strings decoded as ISO-8859-1: the non-ASCII characters are escaped.</summary>
    private static string PythonJsonDump(List<KeyValuePair<string, string>> values)
        => "{" + string.Join(", ", values.Select(value => PythonJsonString(value.Key) + ": " + PythonJsonString(value.Value))) + "}";

    /// <summary><c>isomorphic_encode()</c>: a string whose characters are bytes; a character above U+00FF throws.</summary>
    private static string IsomorphicEncode(string value)
    {
        if (value.Any(c => c > 'ÿ'))
            throw new InvalidOperationException("The string cannot be encoded as ISO-8859-1");

        return value;
    }

    /// <summary><c>urllib.parse.unquote()</c>: the percent-encoded bytes are decoded as UTF-8 (the invalid sequences are replaced).</summary>
    private static string PythonUnquote(string value)
    {
        if (!value.Contains('%', StringComparison.Ordinal))
            return value;

        var result = new StringBuilder();
        var bytes = new List<byte>();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '%' && i + 2 < value.Length && char.IsAsciiHexDigit(value[i + 1]) && char.IsAsciiHexDigit(value[i + 2]))
            {
                bytes.Add(byte.Parse(value.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                i += 2;
                continue;
            }

            if (bytes.Count > 0)
            {
                result.Append(Encoding.UTF8.GetString([.. bytes]));
                bytes.Clear();
            }

            result.Append(value[i]);
        }

        if (bytes.Count > 0)
        {
            result.Append(Encoding.UTF8.GetString([.. bytes]));
        }

        return result.ToString();
    }
}
