namespace Meziantou.Framework.Http.Caching.Fuzz;

/// <summary>
/// A reference implementation of the URL variation config of draft-ietf-httpbis-no-vary-search, built from the canonical
/// <c>No-Vary-Search</c> value <see cref="UrlVariationConfig.ToHeaderValue"/> writes.
/// </summary>
internal sealed class ReferenceVariationConfig
{
    private readonly bool _varyOnKeyOrder;
    private readonly string[]? _noVaryParams;
    private readonly string[]? _varyParams;

    private ReferenceVariationConfig(bool varyOnKeyOrder, string[]? noVaryParams, string[]? varyParams)
    {
        _varyOnKeyOrder = varyOnKeyOrder;
        _noVaryParams = noVaryParams;
        _varyParams = varyParams;
    }

    /// <summary>Reads <c>key-order</c>, <c>params=("a" "b")</c> and <c>except=("a")</c>, whose names are percent-encoded.</summary>
    public static ReferenceVariationConfig FromCanonicalValue(string value)
    {
        var varyOnKeyOrder = true;
        string[]? noVaryParams = [];
        string[]? varyParams = null;
        foreach (var member in value.Split(", ", StringSplitOptions.RemoveEmptyEntries))
        {
            if (member is "key-order")
            {
                varyOnKeyOrder = false;
            }
            else if (member.StartsWith("params=(", StringComparison.Ordinal))
            {
                noVaryParams = ParseNames(member["params=(".Length..^1]);
            }
            else if (member.StartsWith("except=(", StringComparison.Ordinal))
            {
                noVaryParams = null;
                varyParams = ParseNames(member["except=(".Length..^1]);
            }
            else
            {
                throw new InvalidOperationException($"Unexpected member '{member}' in the canonical value '{value}'");
            }
        }

        return new ReferenceVariationConfig(varyOnKeyOrder, noVaryParams, varyParams);

        static string[] ParseNames(string list) => [.. list.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(name => Uri.UnescapeDataString(name.Trim('"')))];
    }

    /// <summary>Section 6: parses the query, removes the parameters that do not matter, and sorts them when their order does not matter.</summary>
    public string NormalizeQuery(string query)
    {
        if (_varyOnKeyOrder && _varyParams is null && _noVaryParams!.Length is 0)
            return query;

        IEnumerable<(string Name, string Value)> parameters = ParseFormUrlEncoded(query.StartsWith('?', StringComparison.Ordinal) ? query[1..] : query);
        parameters = _noVaryParams is not null
            ? parameters.Where(parameter => !_noVaryParams.Contains(parameter.Name, StringComparer.Ordinal))
            : parameters.Where(parameter => _varyParams!.Contains(parameter.Name, StringComparer.Ordinal));

        if (!_varyOnKeyOrder)
        {
            // OrderBy is a stable sort
            parameters = parameters.OrderBy(parameter => parameter.Name, StringComparer.Ordinal);
        }

        return string.Join('&', parameters.Select(parameter => Uri.EscapeDataString(parameter.Name) + "=" + Uri.EscapeDataString(parameter.Value)));
    }

    /// <summary>The application/x-www-form-urlencoded parser of the URL Standard.</summary>
    private static List<(string Name, string Value)> ParseFormUrlEncoded(string input)
    {
        var result = new List<(string Name, string Value)>();
        foreach (var sequence in input.Split('&'))
        {
            if (sequence.Length is 0)
                continue;

            var index = sequence.IndexOf('=', StringComparison.Ordinal);
            var name = index < 0 ? sequence : sequence[..index];
            var value = index < 0 ? "" : sequence[(index + 1)..];
            result.Add((PercentDecode(name.Replace('+', ' ')), PercentDecode(value.Replace('+', ' '))));
        }

        return result;
    }

    private static string PercentDecode(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var output = new List<byte>(bytes.Length);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] is (byte)'%' && i + 2 < bytes.Length && Uri.IsHexDigit((char)bytes[i + 1]) && Uri.IsHexDigit((char)bytes[i + 2]))
            {
                output.Add((byte)((Uri.FromHex((char)bytes[i + 1]) << 4) | Uri.FromHex((char)bytes[i + 2])));
                i += 2;
            }
            else
            {
                output.Add(bytes[i]);
            }
        }

        return Encoding.UTF8.GetString([.. output]);
    }
}
