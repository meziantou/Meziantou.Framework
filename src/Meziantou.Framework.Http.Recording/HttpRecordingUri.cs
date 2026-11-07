namespace Meziantou.Framework.Http.Recording;

/// <summary>Helpers to rewrite recorded request URIs so credentials do not reach recordings or diagnostics.</summary>
internal static class HttpRecordingUri
{
    public const string RedactedValue = "***";

    /// <summary>Removes the userinfo component (<c>https://user:password@host/</c>) from an absolute URI.</summary>
    public static string RemoveUserInfo(string requestUri)
    {
        if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.UserInfo))
            return requestUri;

        var builder = new UriBuilder(uri) { UserName = "", Password = "" };
        return builder.Uri.AbsoluteUri;
    }

    /// <summary>Replaces the value of every query parameter whose name is in <paramref name="parameterNames"/> with <see cref="RedactedValue"/>.</summary>
    public static string MaskQueryParameters(string requestUri, HashSet<string> parameterNames)
    {
        return RewriteQuery(requestUri, name => parameterNames.Contains(name));
    }

    /// <summary>Produces a form of the URI that is safe to put in an exception message or a log: no userinfo and no query values.</summary>
    public static string Redact(string requestUri)
    {
        return RewriteQuery(RemoveUserInfo(requestUri), static _ => true);
    }

    private static string RewriteQuery(string requestUri, Func<string, bool> shouldMask)
    {
        if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var uri))
            return requestUri;

        var query = uri.Query;
        if (query.Length <= 1)
            return requestUri;

        var masked = false;
        var parts = query[1..].Split('&');
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var separatorIndex = part.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex < 0)
                continue;

            var name = part[..separatorIndex];
            if (!shouldMask(Uri.UnescapeDataString(name)))
                continue;

            parts[i] = name + "=" + RedactedValue;
            masked = true;
        }

        if (!masked)
            return requestUri;

        var builder = new UriBuilder(uri) { Query = string.Join('&', parts) };
        return builder.Uri.AbsoluteUri;
    }
}
