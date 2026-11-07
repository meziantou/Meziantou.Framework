using System.Security.Cryptography;

namespace Meziantou.Framework.Http.Recording;

/// <summary>Matches requests based on HTTP method, URL with sorted query parameters, and request body.</summary>
public sealed class DefaultHttpRequestMatcher : IHttpRequestMatcher
{
    /// <summary>Gets the default instance, which includes the request body in the fingerprint.</summary>
    public static DefaultHttpRequestMatcher Instance { get; } = new();

    /// <summary>Gets an instance that matches on method and URL only, ignoring the request body.</summary>
    public static DefaultHttpRequestMatcher IgnoringRequestBody { get; } = new(matchRequestBody: false);

    /// <summary>Initializes a new instance of the <see cref="DefaultHttpRequestMatcher"/> class.</summary>
    /// <param name="matchRequestBody">
    /// When <see langword="true"/> (the default), the request body is part of the fingerprint, so two requests to the
    /// same URL that carry different payloads do not match each other. Set it to <see langword="false"/> for endpoints
    /// whose body varies between runs (a nonce or a timestamp) and should not affect matching.
    /// </param>
    public DefaultHttpRequestMatcher(bool matchRequestBody = true)
    {
        MatchRequestBody = matchRequestBody;
    }

    /// <summary>Gets a value indicating whether the request body is part of the fingerprint.</summary>
    public bool MatchRequestBody { get; }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1055:URI-like return values should not be strings")]
    public string ComputeFingerprint(HttpRecordingEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var sb = new StringBuilder();
        sb.Append(entry.Method.ToUpperInvariant());
        sb.Append(' ');

        if (Uri.TryCreate(entry.RequestUri, UriKind.Absolute, out var uri))
        {
            // Scheme + host + path (normalized). Userinfo is deliberately excluded: it is stripped when the request is
            // captured, so including it here would make a hand-written recording that still carries it unmatchable.
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

        if (MatchRequestBody && entry.RequestBody is { Length: > 0 } body)
        {
            // Hash rather than embed: bodies can be large, and the fingerprint is used as a dictionary key.
            sb.Append(" body:");
            sb.Append(Convert.ToHexString(SHA256.HashData(body)));
        }

        return sb.ToString();
    }

    private static KeyValuePair<string, string>[] ParseAndSortQueryString(string query)
    {
        // Remove leading '?'
        var queryString = query[1..];
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
