using System.Net.Http.Headers;

namespace Meziantou.Framework.Http;

/// <summary>Provides extension methods for working with Link header values.</summary>
public static class LinkHeaderValueExtensions
{
    /// <summary>Enumerates all Link header values from HTTP headers.</summary>
    /// <param name="headers">The HTTP headers to parse.</param>
    /// <returns>A collection of <see cref="LinkHeaderValue"/> instances.</returns>
    public static IEnumerable<LinkHeaderValue> EnumerateLinkHeaders(this HttpHeaders headers) => LinkHeaderValue.Parse(headers);

    /// <summary>Gets the first link declaring the specified relation type.</summary>
    /// <param name="links">The collection of links to search.</param>
    /// <param name="rel">The relation type to find. A link matches when <see cref="LinkHeaderValue.Rel"/> contains this relation type, which may be one of several space-separated values.</param>
    /// <returns>The first <see cref="LinkHeaderValue"/> with the specified relation type, or <see langword="null"/> if not found.</returns>
    public static LinkHeaderValue? GetLink(this IEnumerable<LinkHeaderValue> links, string rel) => links.FirstOrDefault(l => HasRelation(l.Rel, rel));

    // RFC 8288 3.3: the rel parameter carries a space-separated list of relation types.
    private static bool HasRelation(string relations, string rel)
    {
        if (string.Equals(relations, rel, StringComparison.OrdinalIgnoreCase))
            return true;

        var span = relations.AsSpan();
        foreach (var range in span.SplitAny(" \t"))
        {
            if (span[range].Equals(rel, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Gets the URL of the first link with the specified relation type.</summary>
    /// <param name="links">The collection of links to search.</param>
    /// <param name="rel">The relation type to find.</param>
    /// <returns>The URL of the first link with the specified relation type, or <see langword="null"/> if not found.</returns>
    [SuppressMessage("Design", "CA1055:URI-like return values should not be strings")]
    public static string? GetLinkUrl(this IEnumerable<LinkHeaderValue> links, string rel) => links.GetLink(rel)?.Url;
}