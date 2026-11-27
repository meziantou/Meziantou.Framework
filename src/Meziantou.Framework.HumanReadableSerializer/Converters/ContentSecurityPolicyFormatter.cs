using System.Text.RegularExpressions;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed partial class ContentSecurityPolicyFormatter : HttpHeaderValueFormatter
{
    public override string FormatHeaderValue(string headerName, string headerValue)
    {
        // Covers Content-Security-Policy and Content-Security-Policy-Report-Only, which carries
        // nonces with the same syntax.
        if (headerName.StartsWith("Content-Security-Policy", StringComparison.OrdinalIgnoreCase))
            headerValue = ContentSecurityPolicyNonceRegex.Replace(headerValue, "[redacted]");

        return headerValue;
    }

    // nonce-source = "'nonce-" base64-value "'" (CSP3). Matching only the base64 / base64url
    // characters keeps the match linear and inside a single source expression.
    [GeneratedRegex("(?<='nonce-)[A-Za-z0-9+/=_-]*(?=')", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ContentSecurityPolicyNonceRegex { get; }
}
