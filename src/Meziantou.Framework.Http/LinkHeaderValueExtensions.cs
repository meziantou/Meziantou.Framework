using System.Net.Http.Headers;

namespace Meziantou.Framework.Http;

public static class LinkHeaderValueExtensions
{
    public static IEnumerable<LinkHeaderValue> EnumerateLinkHeaders(this HttpHeaders headers) => LinkHeaderValue.Parse(headers);

    public static string? GetLink(this IEnumerable<LinkHeaderValue> links, string rel) => links.FirstOrDefault(l => string.Equals(l.Rel, rel, StringComparison.OrdinalIgnoreCase))?.Url;
}