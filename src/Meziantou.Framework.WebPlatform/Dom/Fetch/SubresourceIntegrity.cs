using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>Subresource Integrity (https://w3c.github.io/webappsec-subresource-integrity/).</summary>
internal static class SubresourceIntegrity
{
    private static readonly System.Buffers.SearchValues<char> Base64Characters = System.Buffers.SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/-_");

    /// <summary>
    /// https://w3c.github.io/webappsec-subresource-integrity/#does-response-match-metadatalist: the digest of the body must
    /// match one of the metadata of the strongest algorithm (SHA-256, SHA-384 or SHA-512). Metadata without valid hash is a
    /// match, and an opaque response (a cross-origin response of a no-cors request) matches no hash.
    /// </summary>
    public static bool Matches(ReadOnlySpan<byte> body, bool opaque, string metadata)
    {
        var items = ParseItems(metadata);
        if (items.Count == 0)
            return true;

        if (opaque)
            return false;

        var strongest = items.Max(item => item.Strength);
        foreach (var (strength, algorithm, value) in items)
        {
            if (strength != strongest)
                continue;

            var digest = algorithm switch
            {
                "sha256" => System.Security.Cryptography.SHA256.HashData(body),
                "sha384" => System.Security.Cryptography.SHA384.HashData(body),
                _ => System.Security.Cryptography.SHA512.HashData(body),
            };

            // The base64url encoding is accepted too
            var actual = Convert.ToBase64String(digest);
            if (value == actual || value.Replace('-', '+').Replace('_', '/') == actual.TrimEnd('=') || value.Replace('-', '+').Replace('_', '/') == actual)
                return true;
        }

        return false;
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-subresource-integrity/#parse-metadata: the set of the hash expressions of the supported
    /// algorithms (<c>algorithm-value</c>, without their options). It is empty when there is no metadata.
    /// </summary>
    public static HashSet<string> Parse(string metadata)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (metadata.Length == 0)
            return result;

        foreach (var (_, algorithm, value) in ParseItems(metadata))
        {
            result.Add(algorithm + "-" + value);
        }

        return result;
    }

    private static List<(int Strength, string Algorithm, string Value)> ParseItems(string metadata)
    {
        var items = new List<(int Strength, string Algorithm, string Value)>();
        foreach (var token in metadata.Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var expression = token.Split('?')[0];
            var dash = expression.IndexOf('-', StringComparison.Ordinal);
            if (dash < 0)
                continue;

            var algorithm = InfraAscii.ToAsciiLowercase(expression[..dash]);
            var strength = algorithm switch
            {
                "sha256" => 1,
                "sha384" => 2,
                "sha512" => 3,
                _ => 0,
            };

            // https://w3c.github.io/webappsec-subresource-integrity/#the-integrity-attribute: as in the browsers, an expression
            // whose value has a character that is not a base64 character (base64url included) is ignored; the padding is
            // not validated (a value with too many "=" is a hash that matches nothing)
            var value = expression[(dash + 1)..];
            var digits = value.AsSpan().TrimEnd('=');
            if (strength > 0 && digits.Length > 0 && !digits.ContainsAnyExcept(Base64Characters))
            {
                items.Add((strength, algorithm, value));
            }
        }

        return items;
    }
}
