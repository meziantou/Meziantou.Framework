using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;

namespace Meziantou.Framework.WebPlatform.Playwright.Dispatchers;

// The cookies and the storage state of the context (on the thread of the context)
internal sealed partial class BrowserContextDispatcher
{
    /// <summary>Returns the cookies of the URLs (all the cookies when there is no URL), as Playwright serializes them.</summary>
    internal JsonArray GetCookies(IReadOnlyList<string> urls)
    {
        IEnumerable<StoredCookie> cookies;
        if (urls.Count == 0)
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

    internal static JsonObject SerializeCookie(StoredCookie cookie) => new()
    {
        ["name"] = cookie.Name,
        ["value"] = cookie.Value,
        ["domain"] = cookie.HostOnly ? cookie.Domain : "." + cookie.Domain,
        ["path"] = cookie.Path,
        ["expires"] = cookie.Expires is { } expires ? expires.ToUnixTimeMilliseconds() / 1000d : -1,
        ["httpOnly"] = cookie.HttpOnly,
        ["secure"] = cookie.Secure,
        ["sameSite"] = cookie.SameSite switch
        {
            CookieSameSite.Strict => "Strict",
            CookieSameSite.None => "None",
            _ => "Lax",
        },
    };

    /// <summary>Adds cookies (<c>addCookies</c>): each cookie has a URL, or a domain and a path.</summary>
    internal void AddCookies(IEnumerable<JsonElement> cookies)
    {
        foreach (var cookie in cookies)
        {
            var name = cookie.GetString("name") ?? "";
            var value = cookie.GetString("value") ?? "";
            var url = cookie.GetString("url");
            var domain = cookie.GetString("domain");
            var path = cookie.GetString("path");
            if (url is null && (domain is null || path is null))
                throw new PlaywrightError("Cookie should have a url or a domain/path pair");

            if (url is not null && domain is not null)
                throw new PlaywrightError("Cookie should have either url or domain");

            var secure = cookie.GetBoolean("secure") ?? (url?.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ?? false);
            Uri uri;
            if (url is not null)
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri!) || uri.Scheme is not ("http" or "https"))
                    throw new PlaywrightError($"Invalid cookie URL: {url}");
            }
            else
            {
                uri = new Uri((secure ? "https://" : "http://") + domain!.TrimStart('.') + path);
            }

            var header = new StringBuilder();
            header.Append(name).Append('=').Append(value);
            header.Append("; Path=").Append(path ?? GetDefaultPath(uri));
            if (domain is not null && domain.StartsWith(".", StringComparison.Ordinal))
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

            Cookies.SetCookie(uri.GetLeftPart(UriPartial.Path), header.ToString());
        }

        static string GetDefaultPath(Uri uri)
        {
            var path = uri.AbsolutePath;
            var slash = path.LastIndexOf("/", StringComparison.Ordinal);
            return slash <= 0 ? "/" : path[..slash];
        }
    }

    /// <summary>Removes the cookies that match the filters of <c>clearCookies</c> (all the cookies without filter).</summary>
    internal void ClearCookies(JsonElement parameters)
    {
        var name = CreateMatcher(parameters, "name");
        var domain = CreateMatcher(parameters, "domain");
        var path = CreateMatcher(parameters, "path");
        foreach (var cookie in Cookies.GetCookies().ToArray())
        {
            var cookieDomain = cookie.HostOnly ? cookie.Domain : "." + cookie.Domain;
            if (name(cookie.Name) && domain(cookieDomain) && path(cookie.Path))
            {
                Cookies.Remove(cookie);
            }
        }

        static Func<string, bool> CreateMatcher(JsonElement parameters, string name)
        {
            if (parameters.GetString(name) is { } value)
                return candidate => string.Equals(candidate, value, StringComparison.Ordinal);

            if (parameters.GetString(name + "RegexSource") is { } source)
            {
                var regex = JavaScriptRegex.Create(source, parameters.GetString(name + "RegexFlags"));
                return candidate => regex.IsMatch(candidate);
            }

            return static _ => true;
        }
    }

    /// <summary>Returns the storage state of the context (<c>storageState</c>): its cookies and the local storage of the origins.</summary>
    internal JsonObject GetStorageState()
    {
        var origins = new JsonArray();
        foreach (var origin in LocalStorage.GetOrigins())
        {
            origins.Add((JsonNode)new JsonObject
            {
                ["origin"] = origin,
                ["localStorage"] = JsonExtensions.ToNameValues(LocalStorage.GetItems(origin)),
            });
        }

        return new JsonObject
        {
            ["cookies"] = GetCookies([]),
            ["origins"] = origins,
        };
    }

    /// <summary>Restores a storage state (the <c>storageState</c> option of the context, or <c>setStorageState</c>).</summary>
    internal void RestoreStorageState(JsonElement storageState)
    {
        if (storageState.ValueKind != JsonValueKind.Object)
            return;

        AddCookies(storageState.GetArray("cookies"));
        foreach (var origin in storageState.GetArray("origins"))
        {
            var name = origin.GetString("origin");
            if (name is null)
                continue;

            foreach (var (key, value) in origin.GetNameValues("localStorage"))
            {
                LocalStorage.SetItem(name, key, value);
            }
        }
    }
}

/// <summary>Converts the regular expressions of the clients (a JavaScript source and flags) to .NET regular expressions.</summary>
internal static class JavaScriptRegex
{
    public static Regex Create(string source, string? flags)
    {
        var options = RegexOptions.ECMAScript | RegexOptions.CultureInvariant;
        if (flags is not null)
        {
            if (flags.Contains('i', StringComparison.Ordinal))
            {
                options |= RegexOptions.IgnoreCase;
            }

            if (flags.Contains('m', StringComparison.Ordinal))
            {
                options |= RegexOptions.Multiline;
            }

            // The ECMAScript option cannot be combined with Singleline or with the Unicode categories of the 'u' flag
            if (flags.Contains('s', StringComparison.Ordinal) || flags.Contains('u', StringComparison.Ordinal) || flags.Contains('v', StringComparison.Ordinal))
            {
                options &= ~RegexOptions.ECMAScript;
                if (flags.Contains('s', StringComparison.Ordinal))
                {
                    options |= RegexOptions.Singleline;
                }
            }
        }

        return new Regex(source, options, TimeSpan.FromSeconds(5));
    }
}
