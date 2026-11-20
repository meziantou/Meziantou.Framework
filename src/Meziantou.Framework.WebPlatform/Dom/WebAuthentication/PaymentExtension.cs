using System.Text;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The browser bound key store of Secure Payment Confirmation (https://w3c.github.io/secure-payment-confirmation/#sctn-browser-bound-key-store):
/// the key pairs of the user agent bound to the credentials created or used for a payment. The keys are software keys kept
/// in memory, per <see cref="VirtualAuthenticatorEnvironment"/> (the user agent of the browsing contexts that share it).
/// </summary>
internal sealed class BrowserBoundKeyStore
{
    private readonly Lock _lock = new();

    /// <summary>The browser_bound_map and the keypair_map: the key pair bound to a credential ID (base64url-encoded).</summary>
    private readonly Dictionary<string, CredentialKey> _keys = new(StringComparer.Ordinal);

    /// <summary>
    /// https://w3c.github.io/secure-payment-confirmation/#sctn-creating-a-keypair: the key pair of the first allowed algorithm
    /// of the <c>public-key</c> type that the user agent supports (ES256, then RS256 when the list is empty), or null when
    /// there is none.
    /// </summary>
    public static CredentialKey? CreateKeyPair(IReadOnlyList<PublicKeyCredentialParameters> allowedAlgorithms)
    {
        if (allowedAlgorithms.Count == 0)
            return CredentialKey.Generate(CredentialKey.ES256);

        foreach (var parameters in allowedAlgorithms)
        {
            if (parameters.Type == "public-key" && CredentialKey.IsSupported(parameters.Alg))
                return CredentialKey.Generate(parameters.Alg);
        }

        return null;
    }

    /// <summary>https://w3c.github.io/secure-payment-confirmation/#sctn-binding-a-keypair</summary>
    public void Bind(ReadOnlySpan<byte> credentialId, CredentialKey key)
    {
        var id = WebAuthenticationEncoding.Base64UrlEncode(credentialId);
        lock (_lock)
        {
            _keys[id] = key;
        }
    }

    /// <summary>
    /// https://w3c.github.io/secure-payment-confirmation/#sctn-retrieving-a-browser-bound-key: the key pair bound to the credential, or
    /// a new key pair bound to it (null when no allowed algorithm is supported).
    /// </summary>
    public CredentialKey? GetOrCreate(ReadOnlySpan<byte> credentialId, IReadOnlyList<PublicKeyCredentialParameters> allowedAlgorithms)
    {
        var id = WebAuthenticationEncoding.Base64UrlEncode(credentialId);
        lock (_lock)
        {
            if (_keys.TryGetValue(id, out var key))
                return key;

            key = CreateKeyPair(allowedAlgorithms);
            if (key is not null)
            {
                _keys[id] = key;
            }

            return key;
        }
    }
}

/// <summary>The client data of the <c>payment</c> extension of Secure Payment Confirmation (https://w3c.github.io/secure-payment-confirmation/#sctn-payment-extension-registration).</summary>
internal static class PaymentExtension
{
    /// <summary>
    /// The JSON object of a <c>CollectedClientAdditionalPaymentRegistrationData</c> (https://w3c.github.io/secure-payment-confirmation/#sctn-collectedclientadditionalpaymentregistrationdata-dictionary):
    /// the browser bound public key, when there is a browser bound key.
    /// </summary>
    public static string SerializeRegistrationData(CredentialKey? browserBoundKey)
    {
        var result = new StringBuilder("{");
        if (browserBoundKey is not null)
        {
            AppendMember(result, "browserBoundPublicKey", WebAuthenticationEncoding.Base64UrlEncode(browserBoundKey.ExportCoseKey()), first: true);
        }

        return result.Append('}').ToString();
    }

    /// <summary>
    /// The JSON object of a <c>CollectedClientAdditionalPaymentData</c> (https://w3c.github.io/secure-payment-confirmation/#sctn-collectedclientadditionalpaymentdata-dictionary),
    /// whose members are in lexicographical order (as a dictionary converted to a JavaScript object).
    /// </summary>
    public static string SerializeAuthenticationData(AuthenticationExtensionsPaymentInputs inputs, CredentialKey? browserBoundKey)
    {
        var result = new StringBuilder("{");
        var first = true;
        if (browserBoundKey is not null)
        {
            AppendMember(result, "browserBoundPublicKey", WebAuthenticationEncoding.Base64UrlEncode(browserBoundKey.ExportCoseKey()), first);
            first = false;
        }

        if (inputs.Instrument is { } instrument)
        {
            AppendName(result, "instrument", first);
            first = false;
            result.Append('{');
            var firstMember = true;
            if (instrument.Details is { } details)
            {
                AppendMember(result, "details", details, firstMember);
                firstMember = false;
            }

            AppendMember(result, "displayName", instrument.DisplayName, firstMember);
            AppendMember(result, "icon", instrument.Icon, first: false);
            AppendName(result, "iconMustBeShown", first: false);
            result.Append(instrument.IconMustBeShown ? "true" : "false");
            result.Append('}');
        }

        if (inputs.PayeeName is { } payeeName)
        {
            AppendMember(result, "payeeName", payeeName, first);
            first = false;
        }

        if (inputs.PayeeOrigin is { } payeeOrigin)
        {
            AppendMember(result, "payeeOrigin", payeeOrigin, first);
            first = false;
        }

        if (inputs.PaymentEntitiesLogos is { Count: > 0 } logos)
        {
            AppendName(result, "paymentEntitiesLogos", first);
            first = false;
            result.Append('[');
            for (var i = 0; i < logos.Count; i++)
            {
                if (i > 0)
                {
                    result.Append(',');
                }

                result.Append('{');
                AppendMember(result, "label", logos[i].Label, first: true);
                AppendMember(result, "url", logos[i].Url, first: false);
                result.Append('}');
            }

            result.Append(']');
        }

        if (inputs.RpId is { } rpId)
        {
            AppendMember(result, "rpId", rpId, first);
            first = false;
        }

        if (inputs.TopOrigin is { } topOrigin)
        {
            AppendMember(result, "topOrigin", topOrigin, first);
            first = false;
        }

        if (inputs.Total is { } total)
        {
            AppendName(result, "total", first);
            result.Append('{');
            AppendMember(result, "currency", total.Currency, first: true);
            AppendMember(result, "value", total.Value, first: false);
            result.Append('}');
        }

        return result.Append('}').ToString();
    }

    private static void AppendName(StringBuilder result, string name, bool first)
    {
        if (!first)
        {
            result.Append(',');
        }

        WebAuthenticationEncoding.AppendCcdString(result, name);
        result.Append(':');
    }

    private static void AppendMember(StringBuilder result, string name, string value, bool first)
    {
        AppendName(result, name, first);
        WebAuthenticationEncoding.AppendCcdString(result, value);
    }
}
