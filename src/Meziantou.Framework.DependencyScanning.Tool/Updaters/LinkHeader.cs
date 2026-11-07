namespace Meziantou.Framework.DependencyScanning.Tool;

/// <summary>Reads the <c>rel="next"</c> entry of an RFC 8288 <c>Link</c> response header, which both the
/// GitHub API and Docker registries use to paginate their tag listings.</summary>
internal static class LinkHeader
{
    public static Uri? TryGetNextPageUri(HttpResponseMessage response, Uri requestUri)
    {
        if (!response.Headers.TryGetValues("Link", out var headerValues))
            return null;

        foreach (var headerValue in headerValues)
        {
            // Splitting on ',' is enough for the URLs GitHub and Docker registries produce; a comma inside
            // a quoted parameter value would need a full RFC 8288 parser.
            foreach (var link in headerValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (link.Length is 0 || link[0] is not '<')
                    continue;

                var urlEnd = link.IndexOf('>', StringComparison.Ordinal);
                if (urlEnd < 0)
                    continue;

                var parameters = link[(urlEnd + 1)..];
                if (!parameters.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase) && !parameters.Contains("rel=next", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (Uri.TryCreate(requestUri, link[1..urlEnd], out var nextUri))
                    return nextUri;
            }
        }

        return null;
    }
}
