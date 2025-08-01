namespace Meziantou.Framework;

public static class UriExtensions
{
    public static bool IsHttpOrHttps(this Uri uri)
    {
        if (!uri.IsAbsoluteUri)
            return false;

        return uri.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp) || uri.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps);
    }
}
