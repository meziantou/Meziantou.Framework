using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Meziantou.Framework.Http.Recording;

/// <summary>Matches requests based on HTTP method and URL with sorted query parameters.</summary>
public sealed class DefaultHttpRequestMatcher : IHttpRequestMatcher
{
    /// <summary>Gets the default instance.</summary>
    public static DefaultHttpRequestMatcher Instance { get; } = new();

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1055:URI-like return values should not be strings")]
    public string ComputeFingerprint(HttpRecordingEntry entry)
    {
        var sb = new StringBuilder();
        sb.Append(entry.Method.ToUpperInvariant());
        sb.Append(' ');

        if (Uri.TryCreate(entry.RequestUri, UriKind.Absolute, out var uri))
        {
            // Scheme + host + path (normalized)
            sb.Append(uri.Scheme.ToLowerInvariant());
            sb.Append("://");
            sb.Append(uri.Host.ToLowerInvariant());
            if (!uri.IsDefaultPort)
            {
                sb.Append(':');
                sb.Append(uri.Port);
            }

            sb.Append(uri.AbsolutePath);

            // Sort query parameters for deterministic matching
            var query = uri.Query;
            if (query.Length > 1)
            {
                var queryParams = ParseAndSortQueryString(query);
                if (queryParams.Length > 0)
                {
                    sb.Append('?');
                    for (var i = 0; i < queryParams.Length; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append('&');
                        }

                        sb.Append(queryParams[i].Key);
                        sb.Append('=');
                        sb.Append(queryParams[i].Value);
                    }
                }
            }
        }
        else
        {
            sb.Append(entry.RequestUri);
        }

        return sb.ToString();
    }

    private static KeyValuePair<string, string>[] ParseAndSortQueryString(string query)
    {
        // Remove leading '?'
        var queryString = query.Substring(1);
        var parts = queryString.Split('&');
        var pairs = new List<KeyValuePair<string, string>>(parts.Length);

        foreach (var part in parts)
        {
            var eqIndex = part.IndexOf('=', StringComparison.Ordinal);
            if (eqIndex >= 0)
            {
                pairs.Add(new KeyValuePair<string, string>(
                    part[..eqIndex],
                    part[(eqIndex + 1)..]));
            }
            else
            {
                pairs.Add(new KeyValuePair<string, string>(part, ""));
            }
        }

        pairs.Sort(static (a, b) =>
        {
            var cmp = StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key);
            return cmp != 0 ? cmp : StringComparer.Ordinal.Compare(a.Value, b.Value);
        });

        return pairs.ToArray();
    }
}
