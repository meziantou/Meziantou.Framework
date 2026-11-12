using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Fuzz;

public static partial class WebPlatformFuzzTargets
{
    /// <summary>
    /// The tokens of the headers of the HTTP cache target (a property: the static fields of the other partial declarations
    /// may be initialized after <see cref="All"/>).
    /// </summary>
    private static string[] HttpCacheTokens =>
    [
        "Cache-Control: ", "Expires: ", "Date: ", "Last-Modified: ", "Age: ", "Pragma: no-cache", "\n", "\n\n", ",", "=", "\"",
        "max-age=", "s-maxage=", "stale-while-revalidate=", "stale-if-error=", "max-stale", "max-stale=", "min-fresh=", "no-cache",
        "no-store", "must-revalidate", "immutable", "public", "private", "only-if-cached", "Thu, 01 Jan 2026 00:00:00 GMT",
    ];

    /// <summary>The times after the response at which the HTTP cache target computes the freshness of the response, in seconds.</summary>
    private static readonly double[] HttpCacheAges = [0, 0.5, 1, 30, 59, 61, 3600, 86400, 31536000];

    /// <summary>
    /// Computes the freshness of a stored response (the headers before the first empty line) for a request (the headers
    /// after it) over time: it never increases (fresh, then usable while it is revalidated, then stale), a request with
    /// <c>Cache-Control: no-cache</c> always validates it, and <see cref="HttpCache.IsFresh"/> agrees with
    /// <see cref="HttpCache.GetFreshness"/>.
    /// </summary>
    public static void CacheFreshness(string input)
    {
        var separator = input.IndexOf("\n\n", StringComparison.Ordinal);
        var responseHeaders = ParseHeaders(separator < 0 ? input : input[..separator]);
        var requestHeaders = ParseHeaders(separator < 0 ? "" : input[(separator + 2)..]);
        var responseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var entry = new HttpCacheEntry(IncludeCredentials: false, requestHeaders, 200, "OK", responseHeaders, [], responseTime.AddSeconds(-1), responseTime);
        var noCache = new List<KeyValuePair<string, string>>(requestHeaders) { new("Cache-Control", "no-cache") };
        var previous = HttpCacheFreshnessRank(HttpCacheFreshness.Fresh);
        foreach (var age in HttpCacheAges)
        {
            var now = responseTime.AddSeconds(age);
            var freshness = HttpCache.GetFreshness(entry, requestHeaders, now);
            FuzzAssert.Equal(freshness == HttpCacheFreshness.Fresh, HttpCache.IsFresh(entry, requestHeaders, now), $"IsFresh disagrees with GetFreshness ({freshness}) after {age} s");
            FuzzAssert.True(HttpCacheFreshnessRank(freshness) <= previous, $"The response is {freshness} after {age} s, but it was less fresh before");
            FuzzAssert.Equal(HttpCacheFreshness.Stale, HttpCache.GetFreshness(entry, noCache, now), $"The response is used without validation by a no-cache request after {age} s");
            previous = HttpCacheFreshnessRank(freshness);
        }

        static List<KeyValuePair<string, string>> ParseHeaders(string text)
        {
            var headers = new List<KeyValuePair<string, string>>();
            foreach (var line in text.Split('\n').Take(32))
            {
                var colon = line.IndexOf(':', StringComparison.Ordinal);
                if (colon > 0)
                {
                    headers.Add(new(line[..colon], line[(colon + 1)..].Trim()));
                }
            }

            return headers;
        }
    }

    private static int HttpCacheFreshnessRank(HttpCacheFreshness freshness) => freshness switch
    {
        HttpCacheFreshness.Fresh => 2,
        HttpCacheFreshness.StaleWhileRevalidate => 1,
        _ => 0,
    };
}
