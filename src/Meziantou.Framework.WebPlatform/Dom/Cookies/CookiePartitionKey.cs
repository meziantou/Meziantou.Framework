namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The partition key of a partitioned cookie (a cookie with the <c>Partitioned</c> attribute, CHIPS,
/// https://github.com/privacycg/CHIPS): the site of the top-level document, and whether the cookie was set in a context
/// with a cross-site ancestor (a cookie partitioned by a top-level site is not available to a same-site iframe that has a
/// cross-site ancestor).
/// </summary>
public readonly record struct CookiePartitionKey
{
    /// <summary>Gets the scheme and the registrable domain of the top-level site (e.g. <c>https://example.com</c>).</summary>
    public required string TopLevelSite { get; init; }

    /// <summary>Gets whether the cookie was set in a context with a cross-site ancestor.</summary>
    public bool HasCrossSiteAncestor { get; init; }
}
