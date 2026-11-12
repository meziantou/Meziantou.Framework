using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Cryptography;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The signatures of Signature-based Subresource Integrity (https://wicg.github.io/signature-based-sri/): the HTTP Message
/// Signatures (https://www.rfc-editor.org/rfc/rfc9421) of a response, whose <c>Signature-Input</c> and <c>Signature</c>
/// headers sign the <c>Unencoded-Digest</c> header (and optionally other components) with an Ed25519 key. The signatures of
/// the profile have the <c>ed25519-integrity</c> tag and the public key as their <c>keyid</c>; the other signatures are
/// ignored.
/// </summary>
internal static class IntegritySignatures
{
    /// <summary>The tag of the signatures of the profile (https://wicg.github.io/signature-based-sri/#profile).</summary>
    public const string Tag = "ed25519-integrity";

    // https://wicg.github.io/signature-based-sri/#profile: the derived components that may be signed (with the "req" parameter)
    private static readonly HashSet<string> RequestDerivedComponents = new(StringComparer.Ordinal) { "@authority", "@method", "@path", "@query", "@request-target", "@scheme", "@target-uri" };

    /// <summary>
    /// https://wicg.github.io/signature-based-sri/#abstract-opdef-append-the-accept-signature-header: the value of the
    /// <c>Accept-Signature</c> header of a request whose integrity metadata has public keys, or null.
    /// </summary>
    public static string? GetAcceptSignature(string integrityMetadata)
    {
        var keys = SubresourceIntegrity.ParsePublicKeys(integrityMetadata);
        if (keys.Count == 0)
            return null;

        var result = new StringBuilder();
        for (var i = 0; i < keys.Count; i++)
        {
            if (i > 0)
            {
                result.Append(", ");
            }

            result.Append("sig").Append(i.ToString(CultureInfo.InvariantCulture)).Append("=(\"unencoded-digest\";sf);keyid=\"").Append(keys[i]).Append("\";tag=\"").Append(Tag).Append('"');
        }

        return result.ToString();
    }

    /// <summary>
    /// https://wicg.github.io/signature-based-sri/#verify-signature-assertions: verifies the signatures of the profile of a
    /// response. The result is a failure when one of them is not valid (or when the response has no valid
    /// <c>Unencoded-Digest</c> header), else the public keys of the valid signatures.
    /// </summary>
    public static IntegritySignatureResult Verify(IntegritySignatureContext context, DateTimeOffset now)
    {
        var validKeys = new List<byte[]>();
        if (HeaderList.Get(context.ResponseHeaders, "Signature-Input") is null)
            return new IntegritySignatureResult(Failed: false, validKeys);

        if (StructuredFields.GetDictionary(context.ResponseHeaders, "Signature-Input") is not { Count: > 0 } inputs
            || StructuredFields.GetDictionary(context.ResponseHeaders, "Signature") is not { } signatures)
        {
            return new IntegritySignatureResult(Failed: false, validKeys);
        }

        var hasValidDigest = IsUnencodedDigestValid(context.ResponseHeaders);
        foreach (var (label, input) in inputs)
        {
            var signatureMember = signatures.Find(entry => entry.Key == label).Value;
            if (signatureMember is null || input.InnerList is not { } components)
                continue;

            // The signatures of the profile: they cover "unencoded-digest";sf, they have no alg, their keyid is an Ed25519
            // public key and their tag is ed25519-integrity
            if (!components.Any(IsUnencodedDigestComponent) || input.GetParameter("alg") is not null || input.GetParameter("tag") is not Tag
                || input.GetParameter("keyid") is not string keyId || DecodeBase64(keyId) is not { Length: Ed25519.KeySize } publicKey)
            {
                continue;
            }

            if (!hasValidDigest)
                return new IntegritySignatureResult(Failed: true, validKeys);

            // The signature is expired, or it is created in the future
            var nowSeconds = now.ToUnixTimeSeconds();
            if ((input.GetParameter("expires") is long expires && expires < nowSeconds) || (input.GetParameter("created") is long created && created > nowSeconds))
                return new IntegritySignatureResult(Failed: true, validKeys);

            if (GetSignatureBase(context, components, input.InnerListParameters) is not { } signatureBase
                || signatureMember.Item?.Value is not byte[] { Length: Ed25519.SignatureSize } signature
                || !Ed25519.Verify(publicKey, Encoding.UTF8.GetBytes(signatureBase), signature))
            {
                return new IntegritySignatureResult(Failed: true, validKeys);
            }

            validKeys.Add(publicKey);
        }

        return new IntegritySignatureResult(Failed: false, validKeys);
    }

    private static bool IsUnencodedDigestComponent(StructuredFieldItem component)
        => component.Value is "unencoded-digest" && component.Parameters is [{ Key: "sf", Value: true }];

    /// <summary>
    /// https://wicg.github.io/signature-based-sri/#unencoded-digest-validity: the <c>Unencoded-Digest</c> header is a non-empty
    /// dictionary of digests of the SHA-2 algorithms, with their lengths, and it has a digest of an enforced algorithm
    /// (<c>sha-256</c> or <c>sha-512</c>).
    /// </summary>
    internal static bool IsUnencodedDigestValid(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        if (StructuredFields.GetDictionary(headers, "Unencoded-Digest") is not { Count: > 0 } dictionary)
            return false;

        var enforceable = false;
        foreach (var (algorithm, member) in dictionary)
        {
            var length = algorithm switch
            {
                "sha-256" => 32,
                "sha-384" => 48,
                "sha-512" => 64,
                _ => -1,
            };

            if (member.Item?.Value is not byte[] digest || digest.Length != length)
                return false;

            enforceable |= algorithm is "sha-256" or "sha-512";
        }

        return enforceable;
    }

    /// <summary>
    /// https://www.rfc-editor.org/rfc/rfc9421#name-creating-the-signature-base: the lines of the covered components, then the
    /// signature parameters. Null when a component cannot be computed (a missing header, a parameter or a derived component
    /// that the profile does not allow, a component covered twice).
    /// </summary>
    internal static string? GetSignatureBase(IntegritySignatureContext context, IReadOnlyList<StructuredFieldItem> components, IReadOnlyList<KeyValuePair<string, object>> parameters)
    {
        var result = new StringBuilder();
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in components)
        {
            if (component.Value is not string name)
                return null;

            var identifier = StructuredFields.SerializeItem(component);
            if (!identifiers.Add(identifier) || GetComponentValue(context, name, component.Parameters) is not { } value)
                return null;

            result.Append(identifier).Append(": ").Append(value).Append('\n');
        }

        result.Append("\"@signature-params\": ").Append(StructuredFields.SerializeInnerList(components, parameters));
        return result.ToString();
    }

    /// <summary>https://www.rfc-editor.org/rfc/rfc9421#name-http-message-components: the value of a component.</summary>
    private static string? GetComponentValue(IntegritySignatureContext context, string name, IReadOnlyList<KeyValuePair<string, object>> parameters)
    {
        var isRequest = false;
        var isStructured = false;
        foreach (var (key, value) in parameters)
        {
            switch (key)
            {
                case "req" when value is true:
                    isRequest = true;
                    break;

                case "sf" when value is true && !name.StartsWith('@', StringComparison.Ordinal):
                    isStructured = true;
                    break;

                default:
                    return null;
            }
        }

        if (name.StartsWith('@', StringComparison.Ordinal))
        {
            // https://wicg.github.io/signature-based-sri/#profile: the request derived components with "req", and "@status"
            if (name == "@status")
                return isRequest ? null : context.Status.ToString("000", CultureInfo.InvariantCulture);

            if (!isRequest || !RequestDerivedComponents.Contains(name))
                return null;

            var url = context.RequestUrl;
            return name switch
            {
                "@method" => context.Method,
                "@target-uri" => url.Serialize(excludeFragment: true),
                "@authority" => (url.Host?.Serialized ?? "").ToLowerInvariant() + (url.Port >= 0 && url.Port != UrlSchemes.GetDefaultPort(url.Scheme) ? ":" + url.Port.ToString(CultureInfo.InvariantCulture) : ""),
                "@scheme" => url.Scheme.ToLowerInvariant(),
                "@request-target" => GetPath(url) + (url.Query is { } requestQuery ? "?" + requestQuery : ""),
                "@path" => GetPath(url),
                _ => "?" + (url.Query ?? ""),
            };
        }

        // https://www.rfc-editor.org/rfc/rfc9421#name-http-fields: the values of the header, trimmed and combined; a
        // structured field is serialized again
        if (name.Length == 0 || name != InfraAscii.ToAsciiLowercase(name))
            return null;

        var headers = isRequest ? context.RequestHeaders : context.ResponseHeaders;
        var values = headers.Where(header => InfraAscii.EqualsIgnoreAsciiCase(header.Key, name)).Select(header => header.Value.Trim(' ', '\t')).ToList();
        if (values.Count == 0)
            return null;

        var combined = string.Join(", ", values);
        if (!isStructured)
            return combined;

        if (StructuredFields.ParseDictionary(combined) is { } dictionary)
            return StructuredFields.SerializeDictionary(dictionary);

        return StructuredFields.ParseItem(combined) is { } item ? StructuredFields.SerializeItem(item) : null;

        static string GetPath(UrlRecord url) => url.SerializePath() is { Length: > 0 } path ? path : "/";
    }

    private static byte[]? DecodeBase64(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>The request and the response whose signatures are verified (see <see cref="IntegritySignatures.Verify"/>).</summary>
/// <param name="RequestUrl">The URL of the request (the derived components with the <c>req</c> parameter).</param>
/// <param name="Method">The method of the request.</param>
/// <param name="RequestHeaders">The headers of the request.</param>
/// <param name="Status">The status of the response.</param>
/// <param name="ResponseHeaders">The headers of the response.</param>
internal readonly record struct IntegritySignatureContext(UrlRecord RequestUrl, string Method, IReadOnlyList<KeyValuePair<string, string>> RequestHeaders, int Status, IReadOnlyList<KeyValuePair<string, string>> ResponseHeaders);

/// <summary>The result of the verification of the signatures of a response.</summary>
/// <param name="Failed">Whether a signature of the profile is not valid.</param>
/// <param name="ValidKeys">The public keys of the valid signatures.</param>
internal readonly record struct IntegritySignatureResult(bool Failed, IReadOnlyList<byte[]> ValidKeys);
