using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>Subresource Integrity (https://w3c.github.io/webappsec-subresource-integrity/).</summary>
internal static class SubresourceIntegrity
{
    private static readonly System.Buffers.SearchValues<char> Base64Characters = System.Buffers.SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/-_");

    /// <summary>
    /// https://w3c.github.io/webappsec-subresource-integrity/#does-response-match-metadatalist and
    /// https://wicg.github.io/signature-based-sri/#client-initiated-integrity-checks: the digest of the body must match one of
    /// the metadata of the strongest algorithm (SHA-256, SHA-384 or SHA-512), and, when the metadata have Ed25519 public keys
    /// (<c>ed25519-</c>), the response must have a valid signature of one of them (<paramref name="signatures"/>). Metadata
    /// without valid hash nor key is a match, and an opaque response (a cross-origin response of a no-cors request) matches
    /// no metadata.
    /// </summary>
    public static bool Matches(ReadOnlySpan<byte> body, bool opaque, string metadata, IntegritySignatureResult? signatures = null)
    {
        var items = ParseItems(metadata);
        var keys = ParsePublicKeys(metadata);
        if (items.Count == 0 && keys.Count == 0)
            return true;

        if (opaque)
            return false;

        if (keys.Count > 0 && !keys.Exists(key => signatures?.ValidKeys.Any(validKey => DecodeKey(key) is { } decoded && decoded.AsSpan().SequenceEqual(validKey)) == true))
            return false;

        if (items.Count == 0)
            return true;

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
    /// https://wicg.github.io/signature-based-sri/#verify-unencoded-digest-assertions: whether the body of a response matches
    /// the digests of its <c>Unencoded-Digest</c> header, a structured dictionary of byte sequences by algorithm: every
    /// digest of a supported algorithm (<c>sha-256</c>, <c>sha-512</c>) must match. A response without a valid header matches.
    /// </summary>
    public static bool MatchesUnencodedDigest(IReadOnlyList<KeyValuePair<string, string>> headers, ReadOnlySpan<byte> body)
    {
        if (StructuredFields.GetDictionary(headers, "Unencoded-Digest") is not { } dictionary)
            return true;

        foreach (var (algorithm, member) in dictionary)
        {
            byte[]? digest = algorithm switch
            {
                "sha-256" => System.Security.Cryptography.SHA256.HashData(body),
                "sha-512" => System.Security.Cryptography.SHA512.HashData(body),
                _ => null,
            };

            if (digest is null)
                continue;

            if (member.Item?.Value is not byte[] expected || !digest.AsSpan().SequenceEqual(expected))
                return false;
        }

        return true;
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

        foreach (var key in ParsePublicKeys(metadata))
        {
            result.Add("ed25519-" + key);
        }

        return result;
    }

    /// <summary>
    /// https://wicg.github.io/signature-based-sri/#parsing: the Ed25519 public keys of the metadata (the values of the
    /// <c>ed25519-</c> expressions, in base64), in order and without duplicates.
    /// </summary>
    public static List<string> ParsePublicKeys(string metadata)
    {
        var keys = new List<string>();
        foreach (var token in metadata.Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var expression = token.Split('?')[0];
            var dash = expression.IndexOf('-', StringComparison.Ordinal);
            if (dash < 0 || !expression.AsSpan(0, dash).Equals("ed25519", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = expression[(dash + 1)..];
            var digits = value.AsSpan().TrimEnd('=');
            if (digits.Length > 0 && !digits.ContainsAnyExcept(Base64Characters) && !keys.Contains(value, StringComparer.Ordinal))
            {
                keys.Add(value);
            }
        }

        return keys;
    }

    /// <summary>The bytes of a public key of the metadata (base64 or base64url), or null.</summary>
    private static byte[]? DecodeKey(string key)
    {
        var base64 = key.Replace('-', '+').Replace('_', '/').TrimEnd('=');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
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
