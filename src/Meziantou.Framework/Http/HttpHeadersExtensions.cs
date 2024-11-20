using System.Net.Http.Headers;

namespace Meziantou.Framework.Http;

public static class HttpHeadersExtensions
{
    public static IEnumerable<LinkHeaderValue> ParseLinkHeaders(this HttpHeaders headers)
    {
        return LinkHeaderValue.Parse(headers);
    }

    public static string? GetNextLink(this IEnumerable<LinkHeaderValue> links)
    {
        return links.FirstOrDefault(l => string.Equals(l.Rel, "next", StringComparison.OrdinalIgnoreCase))?.Url;
    }
}
