using System.Net.Http.Headers;

namespace Meziantou.Framework.Http;

/// <summary>Provides extension methods for working with Link header values.</summary>
public static class LinkHeaderValueExtensions
{
    /// <summary>Enumerates all Link header values from HTTP headers.</summary>
    /// <param name="headers">The HTTP headers to parse.</param>
    /// <returns>A collection of <see cref="LinkHeaderValue"/> instances.</returns>
    public static IEnumerable<LinkHeaderValue> EnumerateLinkHeaders(this HttpHeaders headers) => LinkHeaderValue.Parse(headers);

    /// <summary>Gets the first link with the specified relation type.</summary>
    /// <param name="links">The collection of links to search.</param>
    /// <param name="rel">The relation type to find.</param>
    /// <returns>The first <see cref="LinkHeaderValue"/> with the specified relation type, or <see langword="null"/> if not found.</returns>
    public static LinkHeaderValue? GetLink(this IEnumerable<LinkHeaderValue> links, string rel) => links.FirstOrDefault(l => string.Equals(l.Rel, rel, StringComparison.OrdinalIgnoreCase));

    /// <summary>Gets the URL of the first link with the specified relation type.</summary>
    /// <param name="links">The collection of links to search.</param>
    /// <param name="rel">The relation type to find.</param>
    /// <returns>The URL of the first link with the specified relation type, or <see langword="null"/> if not found.</returns>
    [SuppressMessage("Design", "CA1055:URI-like return values should not be strings")]
    public static string? GetLinkUrl(this IEnumerable<LinkHeaderValue> links, string rel) => links.GetLink(rel)?.Url;
}