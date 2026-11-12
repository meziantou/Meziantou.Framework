using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.DevTools.Browser;

// The cookies of the context (the Storage domain and the cookie commands of the Network domain), on the thread of the context
internal sealed partial class BrowserContextHost
{
    /// <summary>Returns the cookies of the URLs (all the cookies when <paramref name="urls"/> is null), as the protocol serializes them.</summary>
    public JsonArray GetCookies(IReadOnlyList<string>? urls)
    {
        IEnumerable<StoredCookie> cookies;
        if (urls is null)
        {
            cookies = Cookies.GetCookies();
        }
        else
        {
            var set = new HashSet<StoredCookie>();
            foreach (var url in urls)
            {
                set.UnionWith(Cookies.GetCookies(url));
            }

            cookies = Cookies.GetCookies().Where(set.Contains);
        }

        var result = new JsonArray();
        foreach (var cookie in cookies)
        {
            result.Add((JsonNode)SerializeCookie(cookie));
        }

        return result;
    }

    private static JsonObject SerializeCookie(StoredCookie cookie)
    {
        var result = new JsonObject
        {
            ["name"] = cookie.Name,
            ["value"] = cookie.Value,
            ["domain"] = cookie.HostOnly ? cookie.Domain : "." + cookie.Domain,
            ["path"] = cookie.Path,
            ["expires"] = cookie.Expires is { } expires ? expires.ToUnixTimeMilliseconds() / 1000d : -1,
            ["size"] = cookie.Name.Length + cookie.Value.Length,
            ["httpOnly"] = cookie.HttpOnly,
            ["secure"] = cookie.Secure,
            ["session"] = cookie.Expires is null,
            ["priority"] = "Medium",
            ["sameParty"] = false,
            ["sourceScheme"] = cookie.Secure ? "Secure" : "NonSecure",
            ["sourcePort"] = cookie.Secure ? 443 : 80,
        };
        switch (cookie.SameSite)
        {
            case CookieSameSite.Strict:
                result["sameSite"] = "Strict";
                break;

            case CookieSameSite.Lax:
                result["sameSite"] = "Lax";
                break;

            case CookieSameSite.None:
                result["sameSite"] = "None";
                break;
        }

        return result;
    }

    /// <summary>Sets cookies (<c>Storage.setCookies</c>, <c>Network.setCookies</c>): each cookie has a URL, or a domain.</summary>
    public void SetCookies(JsonElement cookies)
    {
        if (cookies.ValueKind != JsonValueKind.Array)
            return;

        foreach (var cookie in cookies.EnumerateArray())
        {
            SetCookie(cookie);
        }
    }

    /// <summary>Sets a cookie (<c>Network.setCookie</c>), and returns whether it was stored.</summary>
    public bool SetCookie(JsonElement cookie)
    {
        var name = cookie.GetString("name") ?? "";
        var value = cookie.GetString("value") ?? "";
        var url = cookie.GetString("url");
        var domain = cookie.GetString("domain");
        var path = cookie.GetString("path");
        var secure = cookie.GetBoolean("secure") ?? (url?.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ?? false);
        Uri uri;
        if (url is not null)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri!) || uri.Scheme is not ("http" or "https"))
                throw new ProtocolException("Invalid cookie fields");
        }
        else if (domain is not null)
        {
            uri = new Uri((secure ? "https://" : "http://") + domain.TrimStart('.') + (path is { Length: > 0 } && path.StartsWith('/', StringComparison.Ordinal) ? path : "/"));
        }
        else
        {
            throw new ProtocolException("Invalid cookie fields");
        }

        var header = new StringBuilder();
        header.Append(name).Append('=').Append(value);
        header.Append("; Path=").Append(path ?? GetDefaultPath(uri));
        if (domain is not null && (domain.StartsWith('.', StringComparison.Ordinal) || url is null))
        {
            header.Append("; Domain=").Append(domain);
        }

        if (cookie.GetDouble("expires") is { } expires && expires > 0)
        {
            header.Append("; Expires=").Append(DateTimeOffset.FromUnixTimeMilliseconds((long)(expires * 1000)).ToString("R", CultureInfo.InvariantCulture));
        }

        if (secure)
        {
            header.Append("; Secure");
        }

        if (cookie.GetBoolean("httpOnly", defaultValue: false))
        {
            header.Append("; HttpOnly");
        }

        if (cookie.GetString("sameSite") is { } sameSite)
        {
            header.Append("; SameSite=").Append(sameSite);
        }

        return Cookies.SetCookie(uri.GetLeftPart(UriPartial.Path), header.ToString());

        static string GetDefaultPath(Uri uri)
        {
            var path = uri.AbsolutePath;
            var slash = path.LastIndexOf('/', StringComparison.Ordinal);
            return slash <= 0 ? "/" : path[..slash];
        }
    }

    /// <summary>Removes the cookies of a name that match a URL, a domain and a path (<c>Network.deleteCookies</c>).</summary>
    public void DeleteCookies(JsonElement parameters)
    {
        var name = parameters.GetRequiredString("name");
        var url = parameters.GetString("url");
        var domain = parameters.GetString("domain");
        var path = parameters.GetString("path");
        var candidates = url is not null ? Cookies.GetCookies(url) : Cookies.GetCookies();
        foreach (var cookie in candidates.ToArray())
        {
            if (cookie.Name != name)
                continue;

            if (domain is not null && !string.Equals(cookie.Domain, domain.TrimStart('.'), StringComparison.OrdinalIgnoreCase))
                continue;

            if (path is not null && cookie.Path != path)
                continue;

            Cookies.Remove(cookie);
        }
    }
}
