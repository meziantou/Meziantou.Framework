namespace Meziantou.Framework.Http.Caching;

/// <summary>
/// The URL variation config conveyed by the <c>No-Vary-Search</c> response header field, as defined by
/// <see href="https://httpwg.org/http-extensions/draft-ietf-httpbis-no-vary-search.html">draft-ietf-httpbis-no-vary-search</see>.
/// </summary>
/// <remarks>
/// The config tells which query parameters make two URLs different. A response stored with a non-default
/// config can answer a request for any URL that is equivalent modulo the config, which is decided by
/// comparing the queries canonicalized by <see cref="NormalizeQuery"/>.
/// </remarks>
internal sealed class UrlVariationConfig
{
    /// <summary>The config used when the response does not carry a usable <c>No-Vary-Search</c> header: every parameter, and their order, matters.</summary>
    public static UrlVariationConfig Default { get; } = new(noVaryParams: [], varyParams: null, varyOnKeyOrder: true);

    // Section 4: exactly one of the two lists is the wildcard, which is represented by null.
    private readonly string[]? _noVaryParams;
    private readonly string[]? _varyParams;

    private UrlVariationConfig(string[]? noVaryParams, string[]? varyParams, bool varyOnKeyOrder)
    {
        _noVaryParams = noVaryParams;
        _varyParams = varyParams;
        VaryOnKeyOrder = varyOnKeyOrder;
    }

    /// <summary>Gets a value indicating whether the order of the query parameters makes two URLs different.</summary>
    public bool VaryOnKeyOrder { get; }

    /// <summary>Gets a value indicating whether the config leaves the standard URL comparison untouched.</summary>
    public bool IsDefault => VaryOnKeyOrder && _varyParams is null && _noVaryParams!.Length is 0;

    /// <summary>Parses a <c>No-Vary-Search</c> header value (Section 5.1).</summary>
    /// <remarks>
    /// Parsing is deliberately strict: anything unrecognized falls back to <see cref="Default"/>, which only
    /// costs cache hits. Two spellings of the allowlist form are accepted, because the header shipped in
    /// browsers before the syntax was revised: <c>except=("a")</c>, as specified by the draft, and
    /// <c>params, except=("a")</c>, as documented by MDN. Both describe the same config.
    /// </remarks>
    public static UrlVariationConfig Parse(string? headerValue)
    {
        if (headerValue is null)
            return Default;

        if (!StructuredFieldsParser.TryParseDictionary(headerValue, out var dictionary))
            return Default;

        var varyOnKeyOrder = true;
        if (dictionary.TryGetValue("key-order", out var keyOrder))
        {
            if (keyOrder.IsInnerList || keyOrder.Item.Type is not StructuredFieldItemType.Boolean)
                return Default;

            varyOnKeyOrder = !keyOrder.Item.BooleanValue;
        }

        var hasParams = dictionary.TryGetValue("params", out var paramsValue);
        var hasExcept = dictionary.TryGetValue("except", out var exceptValue);

        // "params" with a boolean value is the browser spelling of the wildcard: every parameter is ignored,
        // except the ones listed by "except".
        if (hasParams && !paramsValue.IsInnerList && paramsValue.Item.Type is StructuredFieldItemType.Boolean)
        {
            if (!paramsValue.Item.BooleanValue)
            {
                // "params=?0" means the same as omitting the entry
                hasParams = false;
            }
            else if (hasExcept)
            {
                return TryGetKeys(exceptValue, out var exceptKeys)
                    ? new UrlVariationConfig(noVaryParams: null, exceptKeys, varyOnKeyOrder)
                    : Default;
            }
            else
            {
                return new UrlVariationConfig(noVaryParams: null, varyParams: [], varyOnKeyOrder);
            }
        }

        // Section 5.1: the two entries cannot be combined
        if (hasParams && hasExcept)
            return Default;

        if (hasParams)
        {
            return TryGetKeys(paramsValue, out var paramsKeys)
                ? new UrlVariationConfig(paramsKeys, varyParams: null, varyOnKeyOrder)
                : Default;
        }

        if (hasExcept)
        {
            return TryGetKeys(exceptValue, out var exceptKeys)
                ? new UrlVariationConfig(noVaryParams: null, exceptKeys, varyOnKeyOrder)
                : Default;
        }

        return varyOnKeyOrder ? Default : new UrlVariationConfig(noVaryParams: [], varyParams: null, varyOnKeyOrder);
    }

    /// <summary>Writes the config back to a canonical <c>No-Vary-Search</c> header value.</summary>
    /// <remarks>
    /// The parameter names are sorted and deduplicated so that two configs with the same meaning produce the
    /// same text. Cache entries are keyed on this value, so equivalent configs share a single entry.
    /// </remarks>
    public string ToHeaderValue()
    {
        var builder = new StringBuilder();
        if (!VaryOnKeyOrder)
        {
            builder.Append("key-order");
        }

        if (_varyParams is not null)
        {
            AppendKeys(builder, "except", _varyParams);
        }
        else if (_noVaryParams!.Length > 0)
        {
            AppendKeys(builder, "params", _noVaryParams);
        }

        return builder.ToString();
    }

    /// <summary>Canonicalizes a query so that two URLs equivalent modulo this config produce the same text (Section 6).</summary>
    /// <param name="query">The query of the URL, including its leading <c>?</c>, as returned by <see cref="Uri.Query"/>.</param>
    public string NormalizeQuery(string query)
    {
        // Section 6: with the default config the queries are compared character by character, which makes
        // "/a" and "/a?" different URLs.
        if (IsDefault)
            return query;

        var parameters = ParseQuery(query);
        if (_noVaryParams is not null)
        {
            parameters.RemoveAll(parameter => Array.IndexOf(_noVaryParams, parameter.Key) >= 0);
        }
        else
        {
            parameters.RemoveAll(parameter => Array.IndexOf(_varyParams!, parameter.Key) < 0);
        }

        // Section 6: sorting is by key in code unit order, and must be stable so that repeated keys keep
        // their relative order.
        IEnumerable<KeyValuePair<string, string>> ordered = VaryOnKeyOrder ? parameters : parameters.OrderBy(parameter => parameter.Key, StringComparer.Ordinal);

        var builder = new StringBuilder();
        foreach (var (key, value) in ordered)
        {
            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
        }

        return builder.ToString();
    }

    private static void AppendKeys(StringBuilder builder, string name, string[] keys)
    {
        if (builder.Length > 0)
        {
            builder.Append(", ");
        }

        builder.Append(name).Append("=(");

        var first = true;
        foreach (var key in keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!first)
            {
                builder.Append(' ');
            }

            first = false;

            // Percent-encoding keeps the value inside the printable ASCII range allowed for a structured
            // field string, and is undone by ParseKey when the value is read back.
            builder.Append('"').Append(Uri.EscapeDataString(key)).Append('"');
        }

        builder.Append(')');
    }

    private static bool TryGetKeys(StructuredFieldValue value, out string[] keys)
    {
        keys = [];
        if (!value.IsInnerList)
            return false;

        var items = value.InnerList;
        if (items.Count is 0)
            return true;

        var result = new string[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            // Section 5.1: tokens and other item types are not valid parameter names
            if (items[i].Type is not StructuredFieldItemType.String)
                return false;

            result[i] = Decode(items[i].StringValue);
        }

        keys = result;
        return true;
    }

    private static List<KeyValuePair<string, string>> ParseQuery(string query)
    {
        // The application/x-www-form-urlencoded parser defined by the URL Standard
        var input = query.AsSpan();
        if (input.Length > 0 && input[0] is '?')
        {
            input = input[1..];
        }

        var parameters = new List<KeyValuePair<string, string>>();
        foreach (var range in input.Split('&'))
        {
            var sequence = input[range];
            if (sequence.IsEmpty)
                continue;

            var separator = sequence.IndexOf('=');
            var name = separator < 0 ? sequence : sequence[..separator];
            var value = separator < 0 ? ReadOnlySpan<char>.Empty : sequence[(separator + 1)..];

            parameters.Add(new KeyValuePair<string, string>(Decode(name), Decode(value)));
        }

        return parameters;
    }

    private static string Decode(ReadOnlySpan<char> value)
    {
        // Section 5.3, and the percent-decoding done by the application/x-www-form-urlencoded parser: "+" is
        // a space, percent-escapes are decoded, and the bytes are read as UTF-8. Uri.UnescapeDataString
        // cannot be used, because it leaves invalid UTF-8 sequences untouched instead of replacing them with
        // U+FFFD, which would make "?a=%f6" and "?a=%ef%bf%bd" different.
        if (value.IsEmpty)
            return string.Empty;

        var bytes = new byte[Encoding.UTF8.GetMaxByteCount(value.Length)];
        var count = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is '+')
            {
                bytes[count++] = (byte)' ';
            }
            else if (c is '%' && i + 2 < value.Length && TryParseHexDigit(value[i + 1], out var high) && TryParseHexDigit(value[i + 2], out var low))
            {
                bytes[count++] = (byte)((high << 4) | low);
                i += 2;
            }
            else if (c <= 0xFF)
            {
                bytes[count++] = (byte)c;
            }
            else
            {
                count += Encoding.UTF8.GetBytes(value.Slice(i, 1), bytes.AsSpan(count));
            }
        }

        return Encoding.UTF8.GetString(bytes, 0, count);
    }

    private static bool TryParseHexDigit(char c, out int value)
    {
        value = c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1,
        };

        return value >= 0;
    }
}
