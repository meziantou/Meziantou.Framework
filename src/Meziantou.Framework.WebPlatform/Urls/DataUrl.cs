using System.Text;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Urls;

/// <summary>The data: URL processor (https://fetch.spec.whatwg.org/#data-url-processor).</summary>
internal static class DataUrl
{
    /// <summary>Returns the MIME type and the body of a data: URL, or null when the URL cannot be processed.</summary>
    public static (string MimeType, byte[] Body)? Process(UrlRecord url)
    {
        if (url.Scheme != "data")
            return null;

        var input = url.Serialize(excludeFragment: true)["data:".Length..];
        var comma = input.IndexOf(',', StringComparison.Ordinal);
        if (comma < 0)
            return null;

        var mimeType = InfraAscii.StripLeadingAndTrailingAsciiWhitespace(input.AsSpan(0, comma)).ToString();
        var body = PercentEncoding.PercentDecode(input[(comma + 1)..]);

        // A MIME type ending with ";base64" (ASCII case-insensitive, with optional spaces) is base64-decoded
        var withoutTrailingSpaces = mimeType.TrimEnd(' ');
        if (withoutTrailingSpaces.EndsWith("base64", StringComparison.OrdinalIgnoreCase))
        {
            var beforeBase64 = withoutTrailingSpaces[..^"base64".Length].TrimEnd(' ');
            if (beforeBase64.EndsWith(';', StringComparison.Ordinal))
            {
                mimeType = beforeBase64[..^1];
                var decoded = Dom.Window.ForgivingBase64Decode(Encoding.Latin1.GetString(body));
                if (decoded is null)
                    return null;

                body = decoded;
            }
        }

        if (mimeType.StartsWith(';', StringComparison.Ordinal))
        {
            mimeType = "text/plain" + mimeType;
        }

        // The MIME type is serialized, so that it is a valid Content-Type
        return (MimeType.Parse(mimeType)?.ToString() ?? "text/plain;charset=US-ASCII", body);
    }
}
