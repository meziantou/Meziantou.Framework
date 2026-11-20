using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting.Generated;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The additional data types of the payment methods that the library implements: the data of a payment method is an object
// (PaymentMethodData.data) that the payment request converts itself
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    // The kinds and the name of the BufferSource type, as the generated conversions give them
    private const uint IdlBufferSourceKinds = 32765u;
    private const string IdlBufferSourceTypeName = "(ArrayBuffer or (Int8Array or Int16Array or Int32Array or Uint8Array or Uint16Array or Uint32Array or Uint8ClampedArray or BigInt64Array or BigUint64Array or Float16Array or Float32Array or Float64Array or DataView))";

    /// <summary>The conversion of a value to a SecurePaymentConfirmationRequest (https://webidl.spec.whatwg.org/#js-dictionary): the members in lexicographical order.</summary>
    SecurePaymentConfirmationRequest IScriptValueOperations.ToSecurePaymentConfirmationRequest(object? value)
    {
        if (value is SecurePaymentConfirmationRequest request)
            return request;

        if (!TryGetScriptValue(value, out var jsValue))
            return DotNetScriptValueOperations.Instance.ToSecurePaymentConfirmationRequest(value);

        const string Name = "SecurePaymentConfirmationRequest";
        CheckDictionary(jsValue, Name);
        var browserBoundPubKeyCredParams = GetDictionaryMember(jsValue, "browserBoundPubKeyCredParams");
        var browserBoundAlgorithms = Adapter.IsUndefined(browserBoundPubKeyCredParams) ? null : ToSequence(browserBoundPubKeyCredParams, static (r, e) => WebIdlHelpers<TAdapter, TValue>.ToDictionary_Dom_PublicKeyCredentialParameters(r, e));

        var challengeValue = GetDictionaryMember(jsValue, "challenge");
        if (Adapter.IsUndefined(challengeValue))
            throw MissingRequiredDictionaryMember(Name, "challenge");

        var challenge = ToBufferSource(challengeValue, IdlBufferSourceKinds, allowShared: false, IdlBufferSourceTypeName).Bytes.ToArray();

        var credentialIdsValue = GetDictionaryMember(jsValue, "credentialIds");
        if (Adapter.IsUndefined(credentialIdsValue))
            throw MissingRequiredDictionaryMember(Name, "credentialIds");

        var credentialIds = ToSequence(credentialIdsValue, static (r, e) => (ReadOnlyMemory<byte>)r.ToBufferSource(e, IdlBufferSourceKinds, allowShared: false, IdlBufferSourceTypeName).Bytes.ToArray());

        var extensionsValue = GetDictionaryMember(jsValue, "extensions");
        var extensions = Adapter.IsUndefined(extensionsValue) ? null : WebIdlHelpers<TAdapter, TValue>.ToDictionary_Dom_AuthenticationExtensionsClientInputs(this, extensionsValue);

        var instrumentValue = GetDictionaryMember(jsValue, "instrument");
        if (Adapter.IsUndefined(instrumentValue))
            throw MissingRequiredDictionaryMember(Name, "instrument");

        var instrument = WebIdlHelpers<TAdapter, TValue>.ToDictionary_Dom_PaymentCredentialInstrument(this, instrumentValue);

        var localeValue = GetDictionaryMember(jsValue, "locale");
        var locale = Adapter.IsUndefined(localeValue) ? null : ToSequence(localeValue, static (r, e) => r.ToUsvString(e));

        var payeeNameValue = GetDictionaryMember(jsValue, "payeeName");
        var payeeName = Adapter.IsUndefined(payeeNameValue) ? null : ToUsvString(payeeNameValue);

        var payeeOriginValue = GetDictionaryMember(jsValue, "payeeOrigin");
        var payeeOrigin = Adapter.IsUndefined(payeeOriginValue) ? null : ToUsvString(payeeOriginValue);

        var logosValue = GetDictionaryMember(jsValue, "paymentEntitiesLogos");
        var logos = Adapter.IsUndefined(logosValue) ? null : ToSequence(logosValue, static (r, e) => WebIdlHelpers<TAdapter, TValue>.ToDictionary_Dom_PaymentEntityLogo(r, e));

        var rpIdValue = GetDictionaryMember(jsValue, "rpId");
        if (Adapter.IsUndefined(rpIdValue))
            throw MissingRequiredDictionaryMember(Name, "rpId");

        var rpId = ToUsvString(rpIdValue);

        var showOptOutValue = GetDictionaryMember(jsValue, "showOptOut");
        bool? showOptOut = Adapter.IsUndefined(showOptOutValue) ? null : ToBoolean(showOptOutValue);

        var timeoutValue = GetDictionaryMember(jsValue, "timeout");
        uint? timeout = Adapter.IsUndefined(timeoutValue) ? null : ToUnsignedLong(timeoutValue);

        return new SecurePaymentConfirmationRequest
        {
            BrowserBoundPubKeyCredParams = browserBoundAlgorithms,
            Challenge = challenge,
            CredentialIds = credentialIds,
            Extensions = extensions,
            Instrument = instrument,
            Locale = locale,
            PayeeName = payeeName,
            PayeeOrigin = payeeOrigin,
            PaymentEntitiesLogos = logos,
            RpId = rpId,
            ShowOptOut = showOptOut,
            Timeout = timeout,
        };
    }
}
