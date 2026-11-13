using System.ComponentModel;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A public key credential of Web Authentication (https://w3c.github.io/webauthn/#iface-pkcredential): the result of a
/// registration (<c>navigator.credentials.create()</c>) or of an authentication (<c>navigator.credentials.get()</c>).
/// </summary>
/// <seealso href="https://w3c.github.io/webauthn/#publickeycredential"/>
[IdlInterface("PublicKeyCredential", "https://w3c.github.io/webauthn/#publickeycredential")]
public sealed class PublicKeyCredential : Credential
{
    private readonly byte[] _rawId;
    private readonly AuthenticationExtensionsClientOutputs _clientExtensionResults;

    internal PublicKeyCredential(byte[] rawId, AuthenticatorResponse response, string? authenticatorAttachment, AuthenticationExtensionsClientOutputs clientExtensionResults)
    {
        _rawId = rawId;
        Response = response;
        AuthenticatorAttachment = authenticatorAttachment;
        _clientExtensionResults = clientExtensionResults;
    }

    /// <summary>Gets the base64url encoding of the credential ID.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-credential-id"/>
    public override string Id => WebAuthenticationEncoding.Base64UrlEncode(_rawId);

    /// <summary>Gets the type of the credential: <c>public-key</c>.</summary>
    public override string Type => "public-key";

    /// <summary>Gets the credential ID (https://w3c.github.io/webauthn/#dom-publickeycredential-identifier-slot).</summary>
    [NotInIdl("The bytes for the .NET code: the rawId attribute is RawIdBuffer")]
    public ReadOnlyMemory<byte> RawId => _rawId;

    /// <summary>Gets the <c>ArrayBuffer</c> of the credential ID for the scripts: use <see cref="RawId"/> in .NET code.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredential-rawid"/>
    [IdlMember("rawId", "https://w3c.github.io/webauthn/#dom-publickeycredential-rawid")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public object RawIdBuffer => _rawId;

    /// <summary>Gets the response of the authenticator: an <see cref="AuthenticatorAttestationResponse"/> or an <see cref="AuthenticatorAssertionResponse"/>.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredential-response"/>
    [IdlMember("response", "https://w3c.github.io/webauthn/#dom-publickeycredential-response")]
    public AuthenticatorResponse Response { get; }

    /// <summary>Gets the attachment modality of the authenticator (<c>platform</c> or <c>cross-platform</c>), or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredential-authenticatorattachment"/>
    [IdlMember("authenticatorAttachment", "https://w3c.github.io/webauthn/#dom-publickeycredential-authenticatorattachment")]
    public string? AuthenticatorAttachment { get; }

    /// <summary>Returns the outputs of the client extensions (https://w3c.github.io/webauthn/#dom-publickeycredential-getclientextensionresults).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredential-getclientextensionresults"/>
    [IdlMember("getClientExtensionResults", "https://w3c.github.io/webauthn/#dom-publickeycredential-getclientextensionresults")]
    public AuthenticationExtensionsClientOutputs GetClientExtensionResults() => _clientExtensionResults;

    /// <summary>
    /// Returns the JSON type of the credential (https://w3c.github.io/webauthn/#dom-publickeycredential-tojson): a
    /// <see cref="RegistrationResponseJson"/> or an <see cref="AuthenticationResponseJson"/>, whose binary values are
    /// base64url-encoded.
    /// </summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredential-tojson"/>
    [IdlMember("toJSON", "https://w3c.github.io/webauthn/#dom-publickeycredential-tojson")]
    public object ToJson()
    {
        var clientExtensionResults = ToJson(_clientExtensionResults);
        return Response switch
        {
            AuthenticatorAttestationResponse attestation => new RegistrationResponseJson
            {
                Id = Id,
                RawId = Id,
                Response = new AuthenticatorAttestationResponseJson
                {
                    ClientDataJson = WebAuthenticationEncoding.Base64UrlEncode(attestation.ClientDataJson.Span),
                    AuthenticatorData = WebAuthenticationEncoding.Base64UrlEncode(attestation.AuthenticatorData),
                    Transports = attestation.GetTransports(),
                    PublicKey = attestation.PublicKey is { } publicKey ? WebAuthenticationEncoding.Base64UrlEncode(publicKey) : null,
                    PublicKeyAlgorithm = attestation.GetPublicKeyAlgorithm(),
                    AttestationObject = WebAuthenticationEncoding.Base64UrlEncode(attestation.AttestationObject.Span),
                },
                AuthenticatorAttachment = AuthenticatorAttachment,
                ClientExtensionResults = clientExtensionResults,
                Type = Type,
            },
            AuthenticatorAssertionResponse assertion => new AuthenticationResponseJson
            {
                Id = Id,
                RawId = Id,
                Response = new AuthenticatorAssertionResponseJson
                {
                    ClientDataJson = WebAuthenticationEncoding.Base64UrlEncode(assertion.ClientDataJson.Span),
                    AuthenticatorData = WebAuthenticationEncoding.Base64UrlEncode(assertion.AuthenticatorData.Span),
                    Signature = WebAuthenticationEncoding.Base64UrlEncode(assertion.Signature.Span),
                    UserHandle = assertion.UserHandle is { } userHandle ? WebAuthenticationEncoding.Base64UrlEncode(userHandle.Span) : null,
                },
                AuthenticatorAttachment = AuthenticatorAttachment,
                ClientExtensionResults = clientExtensionResults,
                Type = Type,
            },
            _ => throw new InvalidOperationException("The response of the credential is not supported"),
        };
    }

    private static AuthenticationExtensionsClientOutputsJson ToJson(AuthenticationExtensionsClientOutputs outputs) => new()
    {
        Appid = outputs.Appid,
        AppidExclude = outputs.AppidExclude,
        CredProps = outputs.CredProps,
        Prf = outputs.Prf is { } prf ? new AuthenticationExtensionsPrfOutputsJson
        {
            Enabled = prf.Enabled,
            Results = prf.Results is { } results ? new AuthenticationExtensionsPrfValuesJson
            {
                First = WebAuthenticationEncoding.Base64UrlEncode(results.First.Span),
                Second = results.Second is { } second ? WebAuthenticationEncoding.Base64UrlEncode(second.Span) : null,
            } : null,
        } : null,
        LargeBlob = outputs.LargeBlob is { } largeBlob ? new AuthenticationExtensionsLargeBlobOutputsJson
        {
            Supported = largeBlob.Supported,
            Blob = largeBlob.Blob is { } blob ? WebAuthenticationEncoding.Base64UrlEncode(blob.Span) : null,
            Written = largeBlob.Written,
        } : null,
        RemoteClientDataJson = outputs.RemoteClientDataJson,
    };

    /// <summary>Whether the conditional mediation of <c>navigator.credentials.get()</c> is available: true.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredential-isconditionalmediationavailable"/>
    [IdlMember("isConditionalMediationAvailable", "https://w3c.github.io/webauthn/#dom-publickeycredential-isconditionalmediationavailable")]
    public static new Task<bool> IsConditionalMediationAvailable() => Task.FromResult(true);

    /// <summary>
    /// Whether a user-verifying platform authenticator is available (https://w3c.github.io/webauthn/#sctn-isUserVerifyingPlatformAuthenticatorAvailable):
    /// an authenticator of <see cref="WebAuthenticationOptions.Authenticators"/> whose transport is <c>internal</c> and that
    /// can perform user verification.
    /// </summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredential-isuserverifyingplatformauthenticatoravailable"/>
    [IdlMember("isUserVerifyingPlatformAuthenticatorAvailable", "https://w3c.github.io/webauthn/#dom-publickeycredential-isuserverifyingplatformauthenticatoravailable")]
    public static Task<bool> IsUserVerifyingPlatformAuthenticatorAvailable() => Task.FromResult(WebAuthenticationClient.HasUserVerifyingPlatformAuthenticator(GlobalScope.Current));

    /// <summary>
    /// Returns the capabilities of the client (https://w3c.github.io/webauthn/#sctn-getClientCapabilities), sorted by name:
    /// the values of <c>ClientCapability</c> and the implemented extensions (<c>extension:</c> followed by their identifier).
    /// </summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredential-getclientcapabilities"/>
    [IdlMember("getClientCapabilities", "https://w3c.github.io/webauthn/#dom-publickeycredential-getclientcapabilities")]
    public static Task<IReadOnlyList<KeyValuePair<string, bool>>> GetClientCapabilities() => Task.FromResult(WebAuthenticationClient.GetClientCapabilities(GlobalScope.Current));

    /// <summary>
    /// Converts the JSON type of the options of a registration (https://w3c.github.io/webauthn/#sctn-parseCreationOptionsFromJSON):
    /// the base64url-encoded strings are decoded. Throws an <c>EncodingError</c> <see cref="DomException"/> when a value is
    /// not a valid base64url encoding.
    /// </summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredential-parsecreationoptionsfromjson"/>
    [IdlMember("parseCreationOptionsFromJSON", "https://w3c.github.io/webauthn/#dom-publickeycredential-parsecreationoptionsfromjson")]
    public static PublicKeyCredentialCreationOptions ParseCreationOptionsFromJson(PublicKeyCredentialCreationOptionsJson options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new PublicKeyCredentialCreationOptions
        {
            Rp = options.Rp,
            User = new PublicKeyCredentialUserEntity
            {
                Id = DecodeJsonValue(options.User.Id, "user.id"),
                Name = options.User.Name,
                DisplayName = options.User.DisplayName,
            },
            Challenge = DecodeJsonValue(options.Challenge, "challenge"),
            PubKeyCredParams = options.PubKeyCredParams,
            Timeout = options.Timeout,
            ExcludeCredentials = [.. options.ExcludeCredentials.Select((descriptor, index) => FromJson(descriptor, $"excludeCredentials[{index}]"))],
            AuthenticatorSelection = options.AuthenticatorSelection,
            Hints = options.Hints,
            Attestation = options.Attestation,
            AttestationFormats = options.AttestationFormats,
            Extensions = FromJson(options.Extensions),
        };
    }

    /// <summary>
    /// Converts the JSON type of the options of an authentication (https://w3c.github.io/webauthn/#sctn-parseRequestOptionsFromJSON):
    /// the base64url-encoded strings are decoded. Throws an <c>EncodingError</c> <see cref="DomException"/> when a value is
    /// not a valid base64url encoding.
    /// </summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredential-parserequestoptionsfromjson"/>
    [IdlMember("parseRequestOptionsFromJSON", "https://w3c.github.io/webauthn/#dom-publickeycredential-parserequestoptionsfromjson")]
    public static PublicKeyCredentialRequestOptions ParseRequestOptionsFromJson(PublicKeyCredentialRequestOptionsJson options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new PublicKeyCredentialRequestOptions
        {
            Challenge = DecodeJsonValue(options.Challenge, "challenge"),
            Timeout = options.Timeout,
            RpId = options.RpId,
            AllowCredentials = [.. options.AllowCredentials.Select((descriptor, index) => FromJson(descriptor, $"allowCredentials[{index}]"))],
            UserVerification = options.UserVerification,
            Hints = options.Hints,
            Extensions = FromJson(options.Extensions),
        };
    }

    private static PublicKeyCredentialDescriptor FromJson(PublicKeyCredentialDescriptorJson descriptor, string member) => new()
    {
        Type = descriptor.Type,
        Id = DecodeJsonValue(descriptor.Id, member + ".id"),
        Transports = descriptor.Transports,
    };

    private static AuthenticationExtensionsClientInputs? FromJson(AuthenticationExtensionsClientInputsJson? extensions)
    {
        if (extensions is null)
            return null;

        AuthenticationExtensionsPrfValues FromJsonValues(AuthenticationExtensionsPrfValuesJson values, string member) => new()
        {
            First = DecodeJsonValue(values.First, member + ".first"),
            Second = values.Second is { } second ? (ReadOnlyMemory<byte>?)DecodeJsonValue(second, member + ".second") : null,
        };

        return new AuthenticationExtensionsClientInputs
        {
            Appid = extensions.Appid,
            AppidExclude = extensions.AppidExclude,
            CredProps = extensions.CredProps,
            Prf = extensions.Prf is { } prf ? new AuthenticationExtensionsPrfInputs
            {
                Eval = prf.Eval is { } eval ? FromJsonValues(eval, "extensions.prf.eval") : null,
                EvalByCredential = prf.EvalByCredential is { } evalByCredential
                    ? [.. evalByCredential.Select(entry => KeyValuePair.Create(entry.Key, FromJsonValues(entry.Value, "extensions.prf.evalByCredential")))]
                    : null,
            } : null,
            LargeBlob = extensions.LargeBlob is { } largeBlob ? new AuthenticationExtensionsLargeBlobInputs
            {
                Support = largeBlob.Support,
                Read = largeBlob.Read,
                Write = largeBlob.Write is { } write ? (ReadOnlyMemory<byte>?)DecodeJsonValue(write, "extensions.largeBlob.write") : null,
            } : null,
            RemoteClientDataJson = extensions.RemoteClientDataJson,
        };
    }

    private static byte[] DecodeJsonValue(string value, string member)
        => WebAuthenticationEncoding.Base64UrlDecode(value) ?? throw new DomException($"The value of '{member}' is not a valid base64url encoding", "EncodingError");

    /// <summary>
    /// Signals that the relying party does not recognize a credential (https://w3c.github.io/webauthn/#sctn-signalUnknownCredential):
    /// the virtual authenticators remove it. The task fails with a <see cref="TypeErrorException"/> when the credential ID is
    /// not a valid base64url encoding, and a <c>SecurityError</c> <see cref="DomException"/> when the RP ID is not valid for
    /// the origin of the caller.
    /// </summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredential-signalunknowncredential"/>
    [IdlMember("signalUnknownCredential", "https://w3c.github.io/webauthn/#dom-publickeycredential-signalunknowncredential")]
    public static Task SignalUnknownCredential(UnknownCredentialOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (WebAuthenticationEncoding.Base64UrlDecode(options.CredentialId) is not { } credentialId)
            return Task.FromException(new TypeErrorException("The credential ID is not a valid base64url encoding"));

        return WebAuthenticationClient.Signal(GlobalScope.Current, options.RpId, authenticator =>
            authenticator.RemoveCredentials(credential => credential.RpId == options.RpId && credential.Id.AsSpan().SequenceEqual(credentialId)));
    }

    /// <summary>
    /// Signals the complete list of the credentials of a user (https://w3c.github.io/webauthn/#sctn-signalAllAcceptedCredentials):
    /// the virtual authenticators remove the discoverable credentials of the user that are not in the list. The task fails
    /// with a <see cref="TypeErrorException"/> when a value is not a valid base64url encoding, and a <c>SecurityError</c>
    /// <see cref="DomException"/> when the RP ID is not valid for the origin of the caller.
    /// </summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredential-signalallacceptedcredentials"/>
    [IdlMember("signalAllAcceptedCredentials", "https://w3c.github.io/webauthn/#dom-publickeycredential-signalallacceptedcredentials")]
    public static Task SignalAllAcceptedCredentials(AllAcceptedCredentialsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (WebAuthenticationEncoding.Base64UrlDecode(options.UserId) is not { } userId)
            return Task.FromException(new TypeErrorException("The user ID is not a valid base64url encoding"));

        foreach (var id in options.AllAcceptedCredentialIds)
        {
            if (WebAuthenticationEncoding.Base64UrlDecode(id) is null)
                return Task.FromException(new TypeErrorException("A credential ID is not a valid base64url encoding"));
        }

        return WebAuthenticationClient.Signal(GlobalScope.Current, options.RpId, authenticator =>
            authenticator.RemoveCredentials(credential => credential.IsResident && credential.RpId == options.RpId && credential.UserHandle.AsSpan().SequenceEqual(userId)
                && !options.AllAcceptedCredentialIds.Contains(WebAuthenticationEncoding.Base64UrlEncode(credential.Id), StringComparer.Ordinal)));
    }

    /// <summary>
    /// Signals the current name and display name of a user (https://w3c.github.io/webauthn/#sctn-signalCurrentUserDetails):
    /// the virtual authenticators update the discoverable credential of the user. The task fails with a
    /// <see cref="TypeErrorException"/> when the user ID is not a valid base64url encoding, and a <c>SecurityError</c>
    /// <see cref="DomException"/> when the RP ID is not valid for the origin of the caller.
    /// </summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredential-signalcurrentuserdetails"/>
    [IdlMember("signalCurrentUserDetails", "https://w3c.github.io/webauthn/#dom-publickeycredential-signalcurrentuserdetails")]
    public static Task SignalCurrentUserDetails(CurrentUserDetailsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (WebAuthenticationEncoding.Base64UrlDecode(options.UserId) is not { } userId)
            return Task.FromException(new TypeErrorException("The user ID is not a valid base64url encoding"));

        return WebAuthenticationClient.Signal(GlobalScope.Current, options.RpId, authenticator => authenticator.UpdateCredentials(credential =>
        {
            if (credential.IsResident && credential.RpId == options.RpId && credential.UserHandle.AsSpan().SequenceEqual(userId))
            {
                credential.UserName = options.Name;
                credential.UserDisplayName = options.DisplayName;
            }
        }));
    }
}

/// <summary>The response of an authenticator (https://w3c.github.io/webauthn/#iface-authenticatorresponse).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#authenticatorresponse"/>
[IdlInterface("AuthenticatorResponse", "https://w3c.github.io/webauthn/#authenticatorresponse")]
public abstract class AuthenticatorResponse
{
    private readonly byte[] _clientDataJson;

    private protected AuthenticatorResponse(byte[] clientDataJson)
    {
        _clientDataJson = clientDataJson;
    }

    /// <summary>Gets the JSON-compatible serialization of the client data, whose hash the authenticator signed (https://w3c.github.io/webauthn/#dom-authenticatorresponse-clientdatajson).</summary>
    [NotInIdl("The bytes for the .NET code: the clientDataJSON attribute is ClientDataJsonBuffer")]
    public ReadOnlyMemory<byte> ClientDataJson => _clientDataJson;

    /// <summary>Gets the <c>ArrayBuffer</c> of the client data for the scripts: use <see cref="ClientDataJson"/> in .NET code.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorresponse-clientdatajson"/>
    [IdlMember("clientDataJSON", "https://w3c.github.io/webauthn/#dom-authenticatorresponse-clientdatajson")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public object ClientDataJsonBuffer => _clientDataJson;
}

/// <summary>The response of an authenticator to a registration (https://w3c.github.io/webauthn/#iface-authenticatorattestationresponse).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#authenticatorattestationresponse"/>
[IdlInterface("AuthenticatorAttestationResponse", "https://w3c.github.io/webauthn/#authenticatorattestationresponse")]
public sealed class AuthenticatorAttestationResponse : AuthenticatorResponse
{
    private readonly byte[] _attestationObject;
    private readonly IReadOnlyList<string> _transports;
    private readonly int _publicKeyAlgorithm;

    internal AuthenticatorAttestationResponse(byte[] clientDataJson, byte[] attestationObject, byte[] authenticatorData, IReadOnlyList<string> transports, byte[]? publicKey, int publicKeyAlgorithm)
        : base(clientDataJson)
    {
        _attestationObject = attestationObject;
        AuthenticatorData = authenticatorData;
        _transports = transports;
        PublicKey = publicKey;
        _publicKeyAlgorithm = publicKeyAlgorithm;
    }

    /// <summary>The authenticator data contained in the attestation object.</summary>
    internal byte[] AuthenticatorData { get; }

    /// <summary>The DER SubjectPublicKeyInfo of the credential public key, or null.</summary>
    internal byte[]? PublicKey { get; }

    /// <summary>Gets the attestation object (https://w3c.github.io/webauthn/#dom-authenticatorattestationresponse-attestationobject): the CBOR map of the attestation statement and of the authenticator data.</summary>
    [NotInIdl("The bytes for the .NET code: the attestationObject attribute is AttestationObjectBuffer")]
    public ReadOnlyMemory<byte> AttestationObject => _attestationObject;

    /// <summary>Gets the <c>ArrayBuffer</c> of the attestation object for the scripts: use <see cref="AttestationObject"/> in .NET code.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorattestationresponse-attestationobject"/>
    [IdlMember("attestationObject", "https://w3c.github.io/webauthn/#dom-authenticatorattestationresponse-attestationobject")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public object AttestationObjectBuffer => _attestationObject;

    /// <summary>Returns the transports of the authenticator, sorted.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorattestationresponse-gettransports"/>
    [IdlMember("getTransports", "https://w3c.github.io/webauthn/#dom-authenticatorattestationresponse-gettransports")]
    public IReadOnlyList<string> GetTransports() => _transports;

    /// <summary>Returns a copy of the authenticator data contained in the attestation object.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorattestationresponse-getauthenticatordata"/>
    [IdlMember("getAuthenticatorData", "https://w3c.github.io/webauthn/#dom-authenticatorattestationresponse-getauthenticatordata")]
    public byte[] GetAuthenticatorData() => [.. AuthenticatorData];

    /// <summary>Returns a copy of the DER SubjectPublicKeyInfo of the credential public key, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorattestationresponse-getpublickey"/>
    [IdlMember("getPublicKey", "https://w3c.github.io/webauthn/#dom-authenticatorattestationresponse-getpublickey")]
    public byte[]? GetPublicKey() => PublicKey is { } publicKey ? [.. publicKey] : null;

    /// <summary>Returns the COSE algorithm identifier of the credential public key.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorattestationresponse-getpublickeyalgorithm"/>
    [IdlMember("getPublicKeyAlgorithm", "https://w3c.github.io/webauthn/#dom-authenticatorattestationresponse-getpublickeyalgorithm")]
    public int GetPublicKeyAlgorithm() => _publicKeyAlgorithm;
}

/// <summary>The response of an authenticator to an authentication (https://w3c.github.io/webauthn/#iface-authenticatorassertionresponse).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#authenticatorassertionresponse"/>
[IdlInterface("AuthenticatorAssertionResponse", "https://w3c.github.io/webauthn/#authenticatorassertionresponse")]
public sealed class AuthenticatorAssertionResponse : AuthenticatorResponse
{
    private readonly byte[] _authenticatorData;
    private readonly byte[] _signature;
    private readonly byte[]? _userHandle;

    internal AuthenticatorAssertionResponse(byte[] clientDataJson, byte[] authenticatorData, byte[] signature, byte[]? userHandle)
        : base(clientDataJson)
    {
        _authenticatorData = authenticatorData;
        _signature = signature;
        _userHandle = userHandle;
    }

    /// <summary>Gets the authenticator data (https://w3c.github.io/webauthn/#dom-authenticatorassertionresponse-authenticatordata).</summary>
    [NotInIdl("The bytes for the .NET code: the authenticatorData attribute is AuthenticatorDataBuffer")]
    public ReadOnlyMemory<byte> AuthenticatorData => _authenticatorData;

    /// <summary>Gets the <c>ArrayBuffer</c> of the authenticator data for the scripts: use <see cref="AuthenticatorData"/> in .NET code.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorassertionresponse-authenticatordata"/>
    [IdlMember("authenticatorData", "https://w3c.github.io/webauthn/#dom-authenticatorassertionresponse-authenticatordata")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public object AuthenticatorDataBuffer => _authenticatorData;

    /// <summary>Gets the signature of the authenticator data and of the hash of the client data (https://w3c.github.io/webauthn/#dom-authenticatorassertionresponse-signature).</summary>
    [NotInIdl("The bytes for the .NET code: the signature attribute is SignatureBuffer")]
    public ReadOnlyMemory<byte> Signature => _signature;

    /// <summary>Gets the <c>ArrayBuffer</c> of the signature for the scripts: use <see cref="Signature"/> in .NET code.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorassertionresponse-signature"/>
    [IdlMember("signature", "https://w3c.github.io/webauthn/#dom-authenticatorassertionresponse-signature")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public object SignatureBuffer => _signature;

    /// <summary>Gets the user handle returned by the authenticator, or null (https://w3c.github.io/webauthn/#dom-authenticatorassertionresponse-userhandle).</summary>
    [NotInIdl("The bytes for the .NET code: the userHandle attribute is UserHandleBuffer")]
    public ReadOnlyMemory<byte>? UserHandle => _userHandle is null ? null : (ReadOnlyMemory<byte>?)_userHandle;

    /// <summary>Gets the <c>ArrayBuffer</c> of the user handle for the scripts, or null: use <see cref="UserHandle"/> in .NET code.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorassertionresponse-userhandle"/>
    [IdlMember("userHandle", "https://w3c.github.io/webauthn/#dom-authenticatorassertionresponse-userhandle")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public object? UserHandleBuffer => _userHandle;
}
