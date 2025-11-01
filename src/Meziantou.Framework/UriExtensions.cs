namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="Uri"/>.
/// </summary>
/// <example>
/// <code>
/// var uri = new Uri("https://example.com");
/// bool isWeb = uri.IsHttpOrHttps(); // true
/// </code>
/// </example>
public static class UriExtensions
{
    /// <summary>Determines whether the URI uses the HTTP or HTTPS scheme.</summary>
    public static bool IsHttpOrHttps(this Uri uri)
    {
        if (!uri.IsAbsoluteUri)
            return false;

        return uri.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp) || uri.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps);
    }
}
