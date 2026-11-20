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

    /// <summary>The tokens of the byte ranges of the HTTP cache range target.</summary>
    private static string[] HttpCacheRangeTokens => ["bytes ", "bytes=", "-", "/", "*", "\n", "0", "1", "9", "10", "999999999999999999", ",", " "];

    /// <summary>
    /// Stores a response whose <c>Content-Range</c> header is the first line of the input and whose body is the second line
    /// (a <c>206</c> response, then the same body as a complete <c>200</c> response), and answers the <c>Range</c> header of
    /// the third line with it: the parsed <c>Content-Range</c> round-trips through <see cref="HttpCache.FormatContentRange"/>,
    /// the stored range is within the body and the complete length, and a <c>206</c> response given from the stored response
    /// has a range within the stored range and the bytes of the stored body at that range.
    /// </summary>
    public static void CacheRanges(string input)
    {
        var lines = input.Split('\n', 3);
        var contentRange = lines[0];
        var body = System.Text.Encoding.UTF8.GetBytes(lines.Length > 1 ? lines[1] : "");
        var range = lines.Length > 2 ? lines[2] : "bytes=0-";
        var time = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        List<KeyValuePair<string, string>> headers = [new("Content-Range", contentRange)];
        if (HttpCache.ParseContentRange(headers) is var (first, last, completeLength))
        {
            FuzzAssert.True(first <= last && last < completeLength, $"The range {first}-{last}/{completeLength} of '{contentRange}' is not valid");
            FuzzAssert.True(HttpCache.ParseContentRange([new("Content-Range", HttpCache.FormatContentRange(first, last, completeLength))]) == (first, last, completeLength), $"The range of '{contentRange}' does not round-trip");
        }

        Check(new HttpCacheEntry(IncludeCredentials: false, [], 206, "Partial Content", headers, body, time, time));
        Check(new HttpCacheEntry(IncludeCredentials: false, [], 200, "OK", [], body, time, time));

        void Check(HttpCacheEntry entry)
        {
            if (HttpCache.GetStoredRange(entry) is not var (storedFirst, storedLast, storedLength))
            {
                FuzzAssert.True(HttpCache.GetRangeEntry(entry, range) is null, "A response without stored range answers a range request");
                return;
            }

            FuzzAssert.True(storedFirst <= storedLast && storedLast < storedLength && storedLast - storedFirst < entry.Body.Length, $"The stored range {storedFirst}-{storedLast}/{storedLength} is not within the body of {entry.Body.Length} bytes");
            if (HttpCache.GetRangeEntry(entry, range) is not { } rangeEntry)
                return;

            FuzzAssert.Equal(206, rangeEntry.Status, "The response of a range is not a 206 response");
            FuzzAssert.True(HttpCache.ParseContentRange(rangeEntry.Headers) is var (rangeFirst, rangeLast, rangeLength)
                && rangeLength == storedLength && rangeFirst >= storedFirst && rangeLast <= storedLast && rangeEntry.Body.Length == rangeLast - rangeFirst + 1
                && rangeEntry.Body.AsSpan().SequenceEqual(entry.Body.AsSpan((int)(rangeFirst - storedFirst), rangeEntry.Body.Length)), $"The response of '{range}' is not the bytes of the stored range {storedFirst}-{storedLast}/{storedLength}");
        }
    }

    private static int HttpCacheFreshnessRank(HttpCacheFreshness freshness) => freshness switch
    {
        HttpCacheFreshness.Fresh => 2,
        HttpCacheFreshness.StaleWhileRevalidate => 1,
        _ => 0,
    };
}
