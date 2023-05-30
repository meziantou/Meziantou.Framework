using System.Text.RegularExpressions;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class ContentSecurityPolicyFormatter : HttpHeaderValueFormatter
{
    [SuppressMessage("Security", "MA0009:Add regex evaluation timeout")]
    public override string FormatHeaderValue(string headerName, string headerValue)
    {
        if (string.Equals(headerName, "Content-Security-Policy", StringComparison.OrdinalIgnoreCase))
            headerValue = Regex.Replace(headerValue, "(?<=nonce-).*?(?=')", "[redacted]", RegexOptions.None);

        return headerValue;
    }
}
