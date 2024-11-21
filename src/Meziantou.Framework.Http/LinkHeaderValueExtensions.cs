using System.Net.Http.Headers;

namespace Meziantou.Framework.Http;

public static class LinkHeaderValueExtensions
{
    public static IEnumerable<LinkHeaderValue> EnumerateLinkHeaders(this HttpHeaders headers) => LinkHeaderValue.Parse(headers);

    public static LinkHeaderValue GetLink(this IEnumerable<LinkHeaderValue> links, string rel) => links.FirstOrDefault(l => string.Equals(l.Rel, rel, StringComparison.OrdinalIgnoreCase));

    [SuppressMessage("Design", "CA1055:URI-like return values should not be strings")]
    public static string? GetLinkUrl(this IEnumerable<LinkHeaderValue> links, string rel) => links.GetLink(rel)?.Url;
}