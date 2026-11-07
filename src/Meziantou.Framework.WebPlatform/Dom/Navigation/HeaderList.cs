using System.Text;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The algorithms of a header list (https://fetch.spec.whatwg.org/#concept-header-list), used by <see cref="DocumentResource.Headers"/>.</summary>
internal static class HeaderList
{
    /// <summary>
    /// https://fetch.spec.whatwg.org/#concept-header-list-get: the values of the headers named <paramref name="name"/>
    /// (ASCII case-insensitive), separated by ", ", or null when there is none.
    /// </summary>
    public static string? Get(IReadOnlyList<KeyValuePair<string, string>> headers, string name)
    {
        string? result = null;
        StringBuilder? builder = null;
        foreach (var (headerName, value) in headers)
        {
            if (!InfraAscii.EqualsIgnoreAsciiCase(headerName, name))
                continue;

            if (result is null)
            {
                result = value;
            }
            else
            {
                builder ??= new StringBuilder(result);
                builder.Append(", ").Append(value);
            }
        }

        return builder?.ToString() ?? result;
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-header-list-get-decode-split: null when there is no such header.</summary>
    public static List<string>? GetDecodeAndSplit(IReadOnlyList<KeyValuePair<string, string>> headers, string name)
    {
        var input = Get(headers, name);
        if (input is null)
            return null;

        // A value without quoted string nor comma is a single value
        if (input.AsSpan().IndexOfAny('"', ',') < 0)
            return [input.Trim('\t', ' ')];

        var position = 0;
        var values = new List<string>();
        var value = new StringBuilder();
        while (true)
        {
            while (position < input.Length && input[position] is not '"' and not ',')
            {
                value.Append(input[position]);
                position++;
            }

            if (position < input.Length && input[position] == '"')
            {
                value.Append(MimeType.CollectHttpQuotedString(input, ref position, extractValue: false));
                if (position < input.Length)
                    continue;
            }

            values.Add(value.ToString().Trim('\t', ' '));
            value.Clear();
            if (position >= input.Length)
                return values;

            // The character is a ','
            position++;
        }
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-header-extract-mime-type: null on failure.</summary>
    public static MimeType? ExtractMimeType(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        // A single Content-Type header without quoted string nor comma is a single value (the common case)
        if (Get(headers, "Content-Type") is { } contentType && contentType.AsSpan().IndexOfAny('"', ',') < 0)
            return MimeType.Parse(contentType.Trim('\t', ' ')) is { Essence: not "*/*" } singleMimeType ? singleMimeType : null;

        if (GetDecodeAndSplit(headers, "Content-Type") is not { } values)
            return null;

        string? charset = null;
        string? essence = null;
        MimeType? mimeType = null;
        foreach (var value in values)
        {
            var temporaryMimeType = MimeType.Parse(value);
            if (temporaryMimeType is null || temporaryMimeType.Essence == "*/*")
                continue;

            mimeType = temporaryMimeType;
            if (mimeType.Essence != essence)
            {
                charset = mimeType.GetParameter("charset");
                essence = mimeType.Essence;
            }
            else if (mimeType.GetParameter("charset") is null && charset is not null)
            {
                mimeType.SetParameter("charset", charset);
            }
        }

        return mimeType;
    }

    /// <summary>
    /// Parses an HTTP-date (https://httpwg.org/specs/rfc9110.html#http.date): the IMF-fixdate format, and the obsolete RFC 850
    /// and asctime formats that recipients must accept. Returns null when the value is not a valid date.
    /// </summary>
    public static DateTimeOffset? ParseHttpDate(string value)
    {
        string[] formats =
        [
            "ddd, dd MMM yyyy HH:mm:ss 'GMT'",
            "dddd, dd-MMM-yy HH:mm:ss 'GMT'",
            "ddd MMM d HH:mm:ss yyyy",
        ];

        return DateTimeOffset.TryParseExact(value.Trim(), formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AllowInnerWhite | System.Globalization.DateTimeStyles.AssumeUniversal, out var result)
            ? result
            : null;
    }

    /// <summary>https://fetch.spec.whatwg.org/#determine-nosniff</summary>
    public static bool DetermineNosniff(IReadOnlyList<KeyValuePair<string, string>> headers)
        => GetDecodeAndSplit(headers, "X-Content-Type-Options") is [var first, ..] && InfraAscii.EqualsIgnoreAsciiCase(first, "nosniff");

    /// <summary>https://fetch.spec.whatwg.org/#ok-status</summary>
    public static bool IsOkStatus(int status) => status is >= 200 and <= 299;
}
