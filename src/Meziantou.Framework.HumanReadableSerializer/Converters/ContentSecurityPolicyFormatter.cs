using System.Text.RegularExpressions;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed partial class ContentSecurityPolicyFormatter : HttpHeaderValueFormatter
{
    public override string FormatHeaderValue(string headerName, string headerValue)
    {
        if (string.Equals(headerName, "Content-Security-Policy", StringComparison.OrdinalIgnoreCase))
            headerValue = ContentSecurityPolicyNonceRegex.Replace(headerValue, "[redacted]");

        return headerValue;
    }

    [GeneratedRegex("(?<=nonce-).*?(?=')", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ContentSecurityPolicyNonceRegex { get; }
}
