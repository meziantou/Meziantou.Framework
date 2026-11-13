namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The name of an entity of a registration (https://w3c.github.io/webauthn/#dictdef-publickeycredentialentity).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-publickeycredentialentity"/>
[IdlInterface("PublicKeyCredentialEntity", "https://w3c.github.io/webauthn/#dictdef-publickeycredentialentity")]
public abstract class PublicKeyCredentialEntity
{
    /// <summary>Gets the name of the entity.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialentity-name"/>
    [IdlMember("name", "https://w3c.github.io/webauthn/#dom-publickeycredentialentity-name")]
    public required string Name { get; init; }
}

/// <summary>The relying party of a registration (https://w3c.github.io/webauthn/#dictdef-publickeycredentialrpentity).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-publickeycredentialrpentity"/>
[IdlInterface("PublicKeyCredentialRpEntity", "https://w3c.github.io/webauthn/#dictdef-publickeycredentialrpentity")]
public sealed class PublicKeyCredentialRpEntity : PublicKeyCredentialEntity
{
    /// <summary>Gets the RP ID of the credential, or null for the effective domain of the origin of the caller.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialrpentity-id"/>
    [IdlMember("id", "https://w3c.github.io/webauthn/#dom-publickeycredentialrpentity-id")]
    public string? Id { get; init; }
}

/// <summary>The user account of a registration (https://w3c.github.io/webauthn/#dictdef-publickeycredentialuserentity).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-publickeycredentialuserentity"/>
[IdlInterface("PublicKeyCredentialUserEntity", "https://w3c.github.io/webauthn/#dictdef-publickeycredentialuserentity")]
public sealed class PublicKeyCredentialUserEntity : PublicKeyCredentialEntity
{
    /// <summary>Gets the user handle of the account (1 to 64 bytes).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialuserentity-id"/>
    [IdlMember("id", "https://w3c.github.io/webauthn/#dom-publickeycredentialuserentity-id")]
    public required ReadOnlyMemory<byte> Id { get; init; }

    /// <summary>Gets the display name of the user.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialuserentity-displayname"/>
    [IdlMember("displayName", "https://w3c.github.io/webauthn/#dom-publickeycredentialuserentity-displayname")]
    public required string DisplayName { get; init; }
}

/// <summary>A type of credential and a signature algorithm accepted by a registration (https://w3c.github.io/webauthn/#dictdef-publickeycredentialparameters).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-publickeycredentialparameters"/>
[IdlInterface("PublicKeyCredentialParameters", "https://w3c.github.io/webauthn/#dictdef-publickeycredentialparameters")]
public sealed class PublicKeyCredentialParameters
{
    /// <summary>Gets the type of the credential (<c>public-key</c>).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialparameters-type"/>
    [IdlMember("type", "https://w3c.github.io/webauthn/#dom-publickeycredentialparameters-type")]
    public required string Type { get; init; }

    /// <summary>Gets the COSE algorithm identifier of the signature algorithm (e.g. -7 for ES256).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialparameters-alg"/>
    [IdlMember("alg", "https://w3c.github.io/webauthn/#dom-publickeycredentialparameters-alg")]
    public required int Alg { get; init; }
}

/// <summary>Identifies a credential (https://w3c.github.io/webauthn/#dictdef-publickeycredentialdescriptor).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-publickeycredentialdescriptor"/>
[IdlInterface("PublicKeyCredentialDescriptor", "https://w3c.github.io/webauthn/#dictdef-publickeycredentialdescriptor")]
public sealed class PublicKeyCredentialDescriptor
{
    /// <summary>Gets the type of the credential (<c>public-key</c>).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialdescriptor-type"/>
    [IdlMember("type", "https://w3c.github.io/webauthn/#dom-publickeycredentialdescriptor-type")]
    public required string Type { get; init; }

    /// <summary>Gets the credential ID.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialdescriptor-id"/>
    [IdlMember("id", "https://w3c.github.io/webauthn/#dom-publickeycredentialdescriptor-id")]
    public required ReadOnlyMemory<byte> Id { get; init; }

    /// <summary>Gets the transports of the authenticator of the credential, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialdescriptor-transports"/>
    [IdlMember("transports", "https://w3c.github.io/webauthn/#dom-publickeycredentialdescriptor-transports")]
    public IReadOnlyList<string>? Transports { get; init; }
}

/// <summary>The requirements on the authenticator of a registration (https://w3c.github.io/webauthn/#dictdef-authenticatorselectioncriteria).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-authenticatorselectioncriteria"/>
[IdlInterface("AuthenticatorSelectionCriteria", "https://w3c.github.io/webauthn/#dictdef-authenticatorselectioncriteria")]
public sealed class AuthenticatorSelectionCriteria
{
    /// <summary>Gets the required attachment of the authenticator (<c>platform</c> or <c>cross-platform</c>), or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorselectioncriteria-authenticatorattachment"/>
    [IdlMember("authenticatorAttachment", "https://w3c.github.io/webauthn/#dom-authenticatorselectioncriteria-authenticatorattachment")]
    public string? AuthenticatorAttachment { get; init; }

    /// <summary>Gets the requirement of a client-side discoverable credential (<c>discouraged</c>, <c>preferred</c> or <c>required</c>), or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorselectioncriteria-residentkey"/>
    [IdlMember("residentKey", "https://w3c.github.io/webauthn/#dom-authenticatorselectioncriteria-residentkey")]
    public string? ResidentKey { get; init; }

    /// <summary>Gets whether a client-side discoverable credential is required, when <see cref="ResidentKey"/> is null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorselectioncriteria-requireresidentkey"/>
    [IdlMember("requireResidentKey", "https://w3c.github.io/webauthn/#dom-authenticatorselectioncriteria-requireresidentkey")]
    public bool RequireResidentKey { get; init; } = false;

    /// <summary>Gets the user verification requirement (<c>required</c>, <c>preferred</c> or <c>discouraged</c>).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorselectioncriteria-userverification"/>
    [IdlMember("userVerification", "https://w3c.github.io/webauthn/#dom-authenticatorselectioncriteria-userverification")]
    public string UserVerification { get; init; } = "preferred";
}

/// <summary>The options of a registration (https://w3c.github.io/webauthn/#dictdef-publickeycredentialcreationoptions).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-publickeycredentialcreationoptions"/>
[IdlInterface("PublicKeyCredentialCreationOptions", "https://w3c.github.io/webauthn/#dictdef-publickeycredentialcreationoptions")]
public sealed class PublicKeyCredentialCreationOptions
{
    /// <summary>Gets the relying party.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-rp"/>
    [IdlMember("rp", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-rp")]
    public required PublicKeyCredentialRpEntity Rp { get; init; }

    /// <summary>Gets the user account.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-user"/>
    [IdlMember("user", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-user")]
    public required PublicKeyCredentialUserEntity User { get; init; }

    /// <summary>Gets the challenge, signed by the authenticator in the client data.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-challenge"/>
    [IdlMember("challenge", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-challenge")]
    public required ReadOnlyMemory<byte> Challenge { get; init; }

    /// <summary>Gets the accepted types of credentials and algorithms, by preference.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-pubkeycredparams"/>
    [IdlMember("pubKeyCredParams", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-pubkeycredparams")]
    public required IReadOnlyList<PublicKeyCredentialParameters> PubKeyCredParams { get; init; }

    /// <summary>Gets the time the caller is willing to wait, in milliseconds, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-timeout"/>
    [IdlMember("timeout", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-timeout")]
    public uint? Timeout { get; init; }

    /// <summary>Gets the existing credentials of the account, which must not be on the authenticator.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-excludecredentials"/>
    [IdlMember("excludeCredentials", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-excludecredentials")]
    public IReadOnlyList<PublicKeyCredentialDescriptor> ExcludeCredentials { get; init; } = [];

    /// <summary>Gets the requirements on the authenticator, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-authenticatorselection"/>
    [IdlMember("authenticatorSelection", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-authenticatorselection")]
    public AuthenticatorSelectionCriteria? AuthenticatorSelection { get; init; }

    /// <summary>Gets the hints of the user interface (<c>security-key</c>, <c>client-device</c>, <c>hybrid</c>).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-hints"/>
    [IdlMember("hints", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-hints")]
    public IReadOnlyList<string> Hints { get; init; } = [];

    /// <summary>Gets the attestation conveyance preference (<c>none</c>, <c>indirect</c>, <c>direct</c> or <c>enterprise</c>).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-attestation"/>
    [IdlMember("attestation", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-attestation")]
    public string Attestation { get; init; } = "none";

    /// <summary>Gets the preferred attestation statement formats.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-attestationformats"/>
    [IdlMember("attestationFormats", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-attestationformats")]
    public IReadOnlyList<string> AttestationFormats { get; init; } = [];

    /// <summary>Gets the inputs of the extensions, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-extensions"/>
    [IdlMember("extensions", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptions-extensions")]
    public AuthenticationExtensionsClientInputs? Extensions { get; init; }
}

/// <summary>The options of an authentication (https://w3c.github.io/webauthn/#dictdef-publickeycredentialrequestoptions).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-publickeycredentialrequestoptions"/>
[IdlInterface("PublicKeyCredentialRequestOptions", "https://w3c.github.io/webauthn/#dictdef-publickeycredentialrequestoptions")]
public sealed class PublicKeyCredentialRequestOptions
{
    /// <summary>Gets the challenge, signed by the authenticator.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptions-challenge"/>
    [IdlMember("challenge", "https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptions-challenge")]
    public required ReadOnlyMemory<byte> Challenge { get; init; }

    /// <summary>Gets the time the caller is willing to wait, in milliseconds, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptions-timeout"/>
    [IdlMember("timeout", "https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptions-timeout")]
    public uint? Timeout { get; init; }

    /// <summary>Gets the RP ID, or null for the effective domain of the origin of the caller.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptions-rpid"/>
    [IdlMember("rpId", "https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptions-rpid")]
    public string? RpId { get; init; }

    /// <summary>Gets the acceptable credentials; empty for the discoverable credentials of the RP ID.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptions-allowcredentials"/>
    [IdlMember("allowCredentials", "https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptions-allowcredentials")]
    public IReadOnlyList<PublicKeyCredentialDescriptor> AllowCredentials { get; init; } = [];

    /// <summary>Gets the user verification requirement (<c>required</c>, <c>preferred</c> or <c>discouraged</c>).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptions-userverification"/>
    [IdlMember("userVerification", "https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptions-userverification")]
    public string UserVerification { get; init; } = "preferred";

    /// <summary>Gets the hints of the user interface.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptions-hints"/>
    [IdlMember("hints", "https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptions-hints")]
    public IReadOnlyList<string> Hints { get; init; } = [];

    /// <summary>Gets the inputs of the extensions, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptions-extensions"/>
    [IdlMember("extensions", "https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptions-extensions")]
    public AuthenticationExtensionsClientInputs? Extensions { get; init; }
}

/// <summary>The inputs of the client extensions (https://w3c.github.io/webauthn/#dictdef-authenticationextensionsclientinputs). The library implements <c>credProps</c>, <c>prf</c> and <c>largeBlob</c>.</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-authenticationextensionsclientinputs"/>
[IdlInterface("AuthenticationExtensionsClientInputs", "https://w3c.github.io/webauthn/#dictdef-authenticationextensionsclientinputs")]
public sealed class AuthenticationExtensionsClientInputs
{
    /// <summary>Gets the FIDO AppID (the <c>appid</c> extension, not supported: it is ignored).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputs-appid"/>
    [IdlMember("appid", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputs-appid")]
    public string? Appid { get; init; }

    /// <summary>Gets the FIDO AppID of the excluded credentials (the <c>appidExclude</c> extension, not supported: it is ignored).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputs-appidexclude"/>
    [IdlMember("appidExclude", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputs-appidexclude")]
    public string? AppidExclude { get; init; }

    /// <summary>Gets whether the credential properties are requested (the <c>credProps</c> extension).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputs-credprops"/>
    [IdlMember("credProps", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputs-credprops")]
    public bool? CredProps { get; init; }

    /// <summary>Gets the inputs of the pseudo-random function (the <c>prf</c> extension).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputs-prf"/>
    [IdlMember("prf", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputs-prf")]
    public AuthenticationExtensionsPrfInputs? Prf { get; init; }

    /// <summary>Gets the inputs of the large blob storage (the <c>largeBlob</c> extension).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputs-largeblob"/>
    [IdlMember("largeBlob", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputs-largeblob")]
    public AuthenticationExtensionsLargeBlobInputs? LargeBlob { get; init; }

    /// <summary>Gets the client data of a remote desktop web client (the <c>remoteClientDataJSON</c> extension, not supported: it is ignored).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputs-remoteclientdatajson"/>
    [IdlMember("remoteClientDataJSON", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputs-remoteclientdatajson")]
    public string? RemoteClientDataJson { get; init; }
}

/// <summary>The outputs of the client extensions (https://w3c.github.io/webauthn/#dictdef-authenticationextensionsclientoutputs).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-authenticationextensionsclientoutputs"/>
[IdlInterface("AuthenticationExtensionsClientOutputs", "https://w3c.github.io/webauthn/#dictdef-authenticationextensionsclientoutputs")]
public sealed class AuthenticationExtensionsClientOutputs
{
    /// <summary>Gets the output of the <c>appid</c> extension.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputs-appid"/>
    [IdlMember("appid", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputs-appid")]
    public bool? Appid { get; init; }

    /// <summary>Gets the output of the <c>appidExclude</c> extension.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputs-appidexclude"/>
    [IdlMember("appidExclude", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputs-appidexclude")]
    public bool? AppidExclude { get; init; }

    /// <summary>Gets the credential properties (the <c>credProps</c> extension).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputs-credprops"/>
    [IdlMember("credProps", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputs-credprops")]
    public CredentialPropertiesOutput? CredProps { get; init; }

    /// <summary>Gets the outputs of the pseudo-random function (the <c>prf</c> extension).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputs-prf"/>
    [IdlMember("prf", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputs-prf")]
    public AuthenticationExtensionsPrfOutputs? Prf { get; init; }

    /// <summary>Gets the outputs of the large blob storage (the <c>largeBlob</c> extension).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputs-largeblob"/>
    [IdlMember("largeBlob", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputs-largeblob")]
    public AuthenticationExtensionsLargeBlobOutputs? LargeBlob { get; init; }

    /// <summary>Gets the output of the <c>remoteClientDataJSON</c> extension.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputs-remoteclientdatajson"/>
    [IdlMember("remoteClientDataJSON", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputs-remoteclientdatajson")]
    public bool? RemoteClientDataJson { get; init; }
}

/// <summary>The credential properties (https://w3c.github.io/webauthn/#dictdef-credentialpropertiesoutput).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-credentialpropertiesoutput"/>
[IdlInterface("CredentialPropertiesOutput", "https://w3c.github.io/webauthn/#dictdef-credentialpropertiesoutput")]
public sealed class CredentialPropertiesOutput
{
    /// <summary>Gets whether the credential is a client-side discoverable credential, or null when it is not known.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-credentialpropertiesoutput-rk"/>
    [IdlMember("rk", "https://w3c.github.io/webauthn/#dom-credentialpropertiesoutput-rk")]
    public bool? Rk { get; init; }
}

/// <summary>The inputs or the outputs of the pseudo-random function (https://w3c.github.io/webauthn/#dictdef-authenticationextensionsprfvalues).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-authenticationextensionsprfvalues"/>
[IdlInterface("AuthenticationExtensionsPRFValues", "https://w3c.github.io/webauthn/#dictdef-authenticationextensionsprfvalues")]
public sealed class AuthenticationExtensionsPrfValues
{
    /// <summary>Gets the first value.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsprfvalues-first"/>
    [IdlMember("first", "https://w3c.github.io/webauthn/#dom-authenticationextensionsprfvalues-first")]
    public required ReadOnlyMemory<byte> First { get; init; }

    /// <summary>Gets the second value, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsprfvalues-second"/>
    [IdlMember("second", "https://w3c.github.io/webauthn/#dom-authenticationextensionsprfvalues-second")]
    public ReadOnlyMemory<byte>? Second { get; init; }
}

/// <summary>The inputs of the <c>prf</c> extension (https://w3c.github.io/webauthn/#dictdef-authenticationextensionsprfinputs).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-authenticationextensionsprfinputs"/>
[IdlInterface("AuthenticationExtensionsPRFInputs", "https://w3c.github.io/webauthn/#dictdef-authenticationextensionsprfinputs")]
public sealed class AuthenticationExtensionsPrfInputs
{
    /// <summary>Gets the inputs on which the pseudo-random function is evaluated, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsprfinputs-eval"/>
    [IdlMember("eval", "https://w3c.github.io/webauthn/#dom-authenticationextensionsprfinputs-eval")]
    public AuthenticationExtensionsPrfValues? Eval { get; init; }

    /// <summary>Gets the inputs by base64url-encoded credential ID, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsprfinputs-evalbycredential"/>
    [IdlMember("evalByCredential", "https://w3c.github.io/webauthn/#dom-authenticationextensionsprfinputs-evalbycredential")]
    public IReadOnlyList<KeyValuePair<string, AuthenticationExtensionsPrfValues>>? EvalByCredential { get; init; }
}

/// <summary>The outputs of the <c>prf</c> extension (https://w3c.github.io/webauthn/#dictdef-authenticationextensionsprfoutputs).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-authenticationextensionsprfoutputs"/>
[IdlInterface("AuthenticationExtensionsPRFOutputs", "https://w3c.github.io/webauthn/#dictdef-authenticationextensionsprfoutputs")]
public sealed class AuthenticationExtensionsPrfOutputs
{
    /// <summary>Gets whether the pseudo-random function is available for the created credential (registration only).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsprfoutputs-enabled"/>
    [IdlMember("enabled", "https://w3c.github.io/webauthn/#dom-authenticationextensionsprfoutputs-enabled")]
    public bool? Enabled { get; init; }

    /// <summary>Gets the results of the evaluations, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsprfoutputs-results"/>
    [IdlMember("results", "https://w3c.github.io/webauthn/#dom-authenticationextensionsprfoutputs-results")]
    public AuthenticationExtensionsPrfValues? Results { get; init; }
}

/// <summary>The inputs of the <c>largeBlob</c> extension (https://w3c.github.io/webauthn/#dictdef-authenticationextensionslargeblobinputs).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-authenticationextensionslargeblobinputs"/>
[IdlInterface("AuthenticationExtensionsLargeBlobInputs", "https://w3c.github.io/webauthn/#dictdef-authenticationextensionslargeblobinputs")]
public sealed class AuthenticationExtensionsLargeBlobInputs
{
    /// <summary>Gets the requirement of the large blob storage (<c>required</c> or <c>preferred</c>, registration only).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionslargeblobinputs-support"/>
    [IdlMember("support", "https://w3c.github.io/webauthn/#dom-authenticationextensionslargeblobinputs-support")]
    public string? Support { get; init; }

    /// <summary>Gets whether the blob is read (authentication only).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionslargeblobinputs-read"/>
    [IdlMember("read", "https://w3c.github.io/webauthn/#dom-authenticationextensionslargeblobinputs-read")]
    public bool? Read { get; init; }

    /// <summary>Gets the blob to write (authentication only).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionslargeblobinputs-write"/>
    [IdlMember("write", "https://w3c.github.io/webauthn/#dom-authenticationextensionslargeblobinputs-write")]
    public ReadOnlyMemory<byte>? Write { get; init; }
}

/// <summary>The outputs of the <c>largeBlob</c> extension (https://w3c.github.io/webauthn/#dictdef-authenticationextensionslargebloboutputs).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-authenticationextensionslargebloboutputs"/>
[IdlInterface("AuthenticationExtensionsLargeBlobOutputs", "https://w3c.github.io/webauthn/#dictdef-authenticationextensionslargebloboutputs")]
public sealed class AuthenticationExtensionsLargeBlobOutputs
{
    /// <summary>Gets whether the created credential supports the large blob storage (registration only).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionslargebloboutputs-supported"/>
    [IdlMember("supported", "https://w3c.github.io/webauthn/#dom-authenticationextensionslargebloboutputs-supported")]
    public bool? Supported { get; init; }

    /// <summary>Gets the blob that was read, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionslargebloboutputs-blob"/>
    [IdlMember("blob", "https://w3c.github.io/webauthn/#dom-authenticationextensionslargebloboutputs-blob")]
    public ReadOnlyMemory<byte>? Blob { get; init; }

    /// <summary>Gets whether the blob was written.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionslargebloboutputs-written"/>
    [IdlMember("written", "https://w3c.github.io/webauthn/#dom-authenticationextensionslargebloboutputs-written")]
    public bool? Written { get; init; }
}

/// <summary>The JSON type of the options of a registration (https://w3c.github.io/webauthn/#dictdef-publickeycredentialcreationoptionsjson).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-publickeycredentialcreationoptionsjson"/>
[IdlInterface("PublicKeyCredentialCreationOptionsJSON", "https://w3c.github.io/webauthn/#dictdef-publickeycredentialcreationoptionsjson")]
public sealed class PublicKeyCredentialCreationOptionsJson
{
    /// <summary>Gets the relying party.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-rp"/>
    [IdlMember("rp", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-rp")]
    public required PublicKeyCredentialRpEntity Rp { get; init; }

    /// <summary>Gets the user account.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-user"/>
    [IdlMember("user", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-user")]
    public required PublicKeyCredentialUserEntityJson User { get; init; }

    /// <summary>Gets the base64url encoding of the challenge.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-challenge"/>
    [IdlMember("challenge", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-challenge")]
    public required string Challenge { get; init; }

    /// <summary>Gets the accepted types of credentials and algorithms.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-pubkeycredparams"/>
    [IdlMember("pubKeyCredParams", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-pubkeycredparams")]
    public required IReadOnlyList<PublicKeyCredentialParameters> PubKeyCredParams { get; init; }

    /// <summary>Gets the timeout, in milliseconds, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-timeout"/>
    [IdlMember("timeout", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-timeout")]
    public uint? Timeout { get; init; }

    /// <summary>Gets the excluded credentials.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-excludecredentials"/>
    [IdlMember("excludeCredentials", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-excludecredentials")]
    public IReadOnlyList<PublicKeyCredentialDescriptorJson> ExcludeCredentials { get; init; } = [];

    /// <summary>Gets the requirements on the authenticator, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-authenticatorselection"/>
    [IdlMember("authenticatorSelection", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-authenticatorselection")]
    public AuthenticatorSelectionCriteria? AuthenticatorSelection { get; init; }

    /// <summary>Gets the hints of the user interface.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-hints"/>
    [IdlMember("hints", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-hints")]
    public IReadOnlyList<string> Hints { get; init; } = [];

    /// <summary>Gets the attestation conveyance preference.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-attestation"/>
    [IdlMember("attestation", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-attestation")]
    public string Attestation { get; init; } = "none";

    /// <summary>Gets the preferred attestation statement formats.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-attestationformats"/>
    [IdlMember("attestationFormats", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-attestationformats")]
    public IReadOnlyList<string> AttestationFormats { get; init; } = [];

    /// <summary>Gets the inputs of the extensions, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-extensions"/>
    [IdlMember("extensions", "https://w3c.github.io/webauthn/#dom-publickeycredentialcreationoptionsjson-extensions")]
    public AuthenticationExtensionsClientInputsJson? Extensions { get; init; }
}

/// <summary>The JSON type of a user account (https://w3c.github.io/webauthn/#dictdef-publickeycredentialuserentityjson).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-publickeycredentialuserentityjson"/>
[IdlInterface("PublicKeyCredentialUserEntityJSON", "https://w3c.github.io/webauthn/#dictdef-publickeycredentialuserentityjson")]
public sealed class PublicKeyCredentialUserEntityJson
{
    /// <summary>Gets the base64url encoding of the user handle.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialuserentityjson-id"/>
    [IdlMember("id", "https://w3c.github.io/webauthn/#dom-publickeycredentialuserentityjson-id")]
    public required string Id { get; init; }

    /// <summary>Gets the name of the user.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialuserentityjson-name"/>
    [IdlMember("name", "https://w3c.github.io/webauthn/#dom-publickeycredentialuserentityjson-name")]
    public required string Name { get; init; }

    /// <summary>Gets the display name of the user.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialuserentityjson-displayname"/>
    [IdlMember("displayName", "https://w3c.github.io/webauthn/#dom-publickeycredentialuserentityjson-displayname")]
    public required string DisplayName { get; init; }
}

/// <summary>The JSON type of a credential descriptor (https://w3c.github.io/webauthn/#dictdef-publickeycredentialdescriptorjson).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-publickeycredentialdescriptorjson"/>
[IdlInterface("PublicKeyCredentialDescriptorJSON", "https://w3c.github.io/webauthn/#dictdef-publickeycredentialdescriptorjson")]
public sealed class PublicKeyCredentialDescriptorJson
{
    /// <summary>Gets the type of the credential.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialdescriptorjson-type"/>
    [IdlMember("type", "https://w3c.github.io/webauthn/#dom-publickeycredentialdescriptorjson-type")]
    public required string Type { get; init; }

    /// <summary>Gets the base64url encoding of the credential ID.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialdescriptorjson-id"/>
    [IdlMember("id", "https://w3c.github.io/webauthn/#dom-publickeycredentialdescriptorjson-id")]
    public required string Id { get; init; }

    /// <summary>Gets the transports, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialdescriptorjson-transports"/>
    [IdlMember("transports", "https://w3c.github.io/webauthn/#dom-publickeycredentialdescriptorjson-transports")]
    public IReadOnlyList<string>? Transports { get; init; }
}

/// <summary>The JSON type of the inputs of the client extensions (https://w3c.github.io/webauthn/#dictdef-authenticationextensionsclientinputsjson).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-authenticationextensionsclientinputsjson"/>
[IdlInterface("AuthenticationExtensionsClientInputsJSON", "https://w3c.github.io/webauthn/#dictdef-authenticationextensionsclientinputsjson")]
public sealed class AuthenticationExtensionsClientInputsJson
{
    /// <summary>Gets the input of the <c>appid</c> extension.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputsjson-appid"/>
    [IdlMember("appid", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputsjson-appid")]
    public string? Appid { get; init; }

    /// <summary>Gets the input of the <c>appidExclude</c> extension.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputsjson-appidexclude"/>
    [IdlMember("appidExclude", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputsjson-appidexclude")]
    public string? AppidExclude { get; init; }

    /// <summary>Gets the input of the <c>credProps</c> extension.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputsjson-credprops"/>
    [IdlMember("credProps", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputsjson-credprops")]
    public bool? CredProps { get; init; }

    /// <summary>Gets the input of the <c>prf</c> extension.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputsjson-prf"/>
    [IdlMember("prf", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputsjson-prf")]
    public AuthenticationExtensionsPrfInputsJson? Prf { get; init; }

    /// <summary>Gets the input of the <c>largeBlob</c> extension.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputsjson-largeblob"/>
    [IdlMember("largeBlob", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputsjson-largeblob")]
    public AuthenticationExtensionsLargeBlobInputsJson? LargeBlob { get; init; }

    /// <summary>Gets the input of the <c>remoteClientDataJSON</c> extension.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputsjson-remoteclientdatajson"/>
    [IdlMember("remoteClientDataJSON", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientinputsjson-remoteclientdatajson")]
    public string? RemoteClientDataJson { get; init; }
}

/// <summary>The JSON type of the values of the pseudo-random function (https://w3c.github.io/webauthn/#dictdef-authenticationextensionsprfvaluesjson).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-authenticationextensionsprfvaluesjson"/>
[IdlInterface("AuthenticationExtensionsPRFValuesJSON", "https://w3c.github.io/webauthn/#dictdef-authenticationextensionsprfvaluesjson")]
public sealed class AuthenticationExtensionsPrfValuesJson
{
    /// <summary>Gets the base64url encoding of the first value.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsprfvaluesjson-first"/>
    [IdlMember("first", "https://w3c.github.io/webauthn/#dom-authenticationextensionsprfvaluesjson-first")]
    public required string First { get; init; }

    /// <summary>Gets the base64url encoding of the second value, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsprfvaluesjson-second"/>
    [IdlMember("second", "https://w3c.github.io/webauthn/#dom-authenticationextensionsprfvaluesjson-second")]
    public string? Second { get; init; }
}

/// <summary>The JSON type of the inputs of the <c>prf</c> extension (https://w3c.github.io/webauthn/#dictdef-authenticationextensionsprfinputsjson).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-authenticationextensionsprfinputsjson"/>
[IdlInterface("AuthenticationExtensionsPRFInputsJSON", "https://w3c.github.io/webauthn/#dictdef-authenticationextensionsprfinputsjson")]
public sealed class AuthenticationExtensionsPrfInputsJson
{
    /// <summary>Gets the inputs of the evaluation, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsprfinputsjson-eval"/>
    [IdlMember("eval", "https://w3c.github.io/webauthn/#dom-authenticationextensionsprfinputsjson-eval")]
    public AuthenticationExtensionsPrfValuesJson? Eval { get; init; }

    /// <summary>Gets the inputs by credential ID, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsprfinputsjson-evalbycredential"/>
    [IdlMember("evalByCredential", "https://w3c.github.io/webauthn/#dom-authenticationextensionsprfinputsjson-evalbycredential")]
    public IReadOnlyList<KeyValuePair<string, AuthenticationExtensionsPrfValuesJson>>? EvalByCredential { get; init; }
}

/// <summary>The JSON type of the outputs of the <c>prf</c> extension (https://w3c.github.io/webauthn/#dictdef-authenticationextensionsprfoutputsjson).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-authenticationextensionsprfoutputsjson"/>
[IdlInterface("AuthenticationExtensionsPRFOutputsJSON", "https://w3c.github.io/webauthn/#dictdef-authenticationextensionsprfoutputsjson")]
public sealed class AuthenticationExtensionsPrfOutputsJson
{
    /// <summary>Gets whether the pseudo-random function is available.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsprfoutputsjson-enabled"/>
    [IdlMember("enabled", "https://w3c.github.io/webauthn/#dom-authenticationextensionsprfoutputsjson-enabled")]
    public bool? Enabled { get; init; }

    /// <summary>Gets the results, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsprfoutputsjson-results"/>
    [IdlMember("results", "https://w3c.github.io/webauthn/#dom-authenticationextensionsprfoutputsjson-results")]
    public AuthenticationExtensionsPrfValuesJson? Results { get; init; }
}

/// <summary>The JSON type of the inputs of the <c>largeBlob</c> extension (https://w3c.github.io/webauthn/#dictdef-authenticationextensionslargeblobinputsjson).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-authenticationextensionslargeblobinputsjson"/>
[IdlInterface("AuthenticationExtensionsLargeBlobInputsJSON", "https://w3c.github.io/webauthn/#dictdef-authenticationextensionslargeblobinputsjson")]
public sealed class AuthenticationExtensionsLargeBlobInputsJson
{
    /// <summary>Gets the requirement of the large blob storage.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionslargeblobinputsjson-support"/>
    [IdlMember("support", "https://w3c.github.io/webauthn/#dom-authenticationextensionslargeblobinputsjson-support")]
    public string? Support { get; init; }

    /// <summary>Gets whether the blob is read.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionslargeblobinputsjson-read"/>
    [IdlMember("read", "https://w3c.github.io/webauthn/#dom-authenticationextensionslargeblobinputsjson-read")]
    public bool? Read { get; init; }

    /// <summary>Gets the base64url encoding of the blob to write.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionslargeblobinputsjson-write"/>
    [IdlMember("write", "https://w3c.github.io/webauthn/#dom-authenticationextensionslargeblobinputsjson-write")]
    public string? Write { get; init; }
}

/// <summary>The JSON type of the outputs of the <c>largeBlob</c> extension (https://w3c.github.io/webauthn/#dictdef-authenticationextensionslargebloboutputsjson).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-authenticationextensionslargebloboutputsjson"/>
[IdlInterface("AuthenticationExtensionsLargeBlobOutputsJSON", "https://w3c.github.io/webauthn/#dictdef-authenticationextensionslargebloboutputsjson")]
public sealed class AuthenticationExtensionsLargeBlobOutputsJson
{
    /// <summary>Gets whether the large blob storage is supported.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionslargebloboutputsjson-supported"/>
    [IdlMember("supported", "https://w3c.github.io/webauthn/#dom-authenticationextensionslargebloboutputsjson-supported")]
    public bool? Supported { get; init; }

    /// <summary>Gets the base64url encoding of the blob that was read.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionslargebloboutputsjson-blob"/>
    [IdlMember("blob", "https://w3c.github.io/webauthn/#dom-authenticationextensionslargebloboutputsjson-blob")]
    public string? Blob { get; init; }

    /// <summary>Gets whether the blob was written.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionslargebloboutputsjson-written"/>
    [IdlMember("written", "https://w3c.github.io/webauthn/#dom-authenticationextensionslargebloboutputsjson-written")]
    public bool? Written { get; init; }
}

/// <summary>The JSON type of the options of an authentication (https://w3c.github.io/webauthn/#dictdef-publickeycredentialrequestoptionsjson).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-publickeycredentialrequestoptionsjson"/>
[IdlInterface("PublicKeyCredentialRequestOptionsJSON", "https://w3c.github.io/webauthn/#dictdef-publickeycredentialrequestoptionsjson")]
public sealed class PublicKeyCredentialRequestOptionsJson
{
    /// <summary>Gets the base64url encoding of the challenge.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptionsjson-challenge"/>
    [IdlMember("challenge", "https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptionsjson-challenge")]
    public required string Challenge { get; init; }

    /// <summary>Gets the timeout, in milliseconds, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptionsjson-timeout"/>
    [IdlMember("timeout", "https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptionsjson-timeout")]
    public uint? Timeout { get; init; }

    /// <summary>Gets the RP ID, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptionsjson-rpid"/>
    [IdlMember("rpId", "https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptionsjson-rpid")]
    public string? RpId { get; init; }

    /// <summary>Gets the acceptable credentials.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptionsjson-allowcredentials"/>
    [IdlMember("allowCredentials", "https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptionsjson-allowcredentials")]
    public IReadOnlyList<PublicKeyCredentialDescriptorJson> AllowCredentials { get; init; } = [];

    /// <summary>Gets the user verification requirement.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptionsjson-userverification"/>
    [IdlMember("userVerification", "https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptionsjson-userverification")]
    public string UserVerification { get; init; } = "preferred";

    /// <summary>Gets the hints of the user interface.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptionsjson-hints"/>
    [IdlMember("hints", "https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptionsjson-hints")]
    public IReadOnlyList<string> Hints { get; init; } = [];

    /// <summary>Gets the inputs of the extensions, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptionsjson-extensions"/>
    [IdlMember("extensions", "https://w3c.github.io/webauthn/#dom-publickeycredentialrequestoptionsjson-extensions")]
    public AuthenticationExtensionsClientInputsJson? Extensions { get; init; }
}

/// <summary>The JSON type of the outputs of the client extensions (https://w3c.github.io/webauthn/#dictdef-authenticationextensionsclientoutputsjson).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-authenticationextensionsclientoutputsjson"/>
[IdlInterface("AuthenticationExtensionsClientOutputsJSON", "https://w3c.github.io/webauthn/#dictdef-authenticationextensionsclientoutputsjson")]
public sealed class AuthenticationExtensionsClientOutputsJson
{
    /// <summary>Gets the output of the <c>appid</c> extension.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputsjson-appid"/>
    [IdlMember("appid", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputsjson-appid")]
    public bool? Appid { get; init; }

    /// <summary>Gets the output of the <c>appidExclude</c> extension.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputsjson-appidexclude"/>
    [IdlMember("appidExclude", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputsjson-appidexclude")]
    public bool? AppidExclude { get; init; }

    /// <summary>Gets the credential properties.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputsjson-credprops"/>
    [IdlMember("credProps", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputsjson-credprops")]
    public CredentialPropertiesOutput? CredProps { get; init; }

    /// <summary>Gets the outputs of the <c>prf</c> extension.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputsjson-prf"/>
    [IdlMember("prf", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputsjson-prf")]
    public AuthenticationExtensionsPrfOutputsJson? Prf { get; init; }

    /// <summary>Gets the outputs of the <c>largeBlob</c> extension.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputsjson-largeblob"/>
    [IdlMember("largeBlob", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputsjson-largeblob")]
    public AuthenticationExtensionsLargeBlobOutputsJson? LargeBlob { get; init; }

    /// <summary>Gets the output of the <c>remoteClientDataJSON</c> extension.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputsjson-remoteclientdatajson"/>
    [IdlMember("remoteClientDataJSON", "https://w3c.github.io/webauthn/#dom-authenticationextensionsclientoutputsjson-remoteclientdatajson")]
    public bool? RemoteClientDataJson { get; init; }
}

/// <summary>The JSON type of a credential created by a registration (https://w3c.github.io/webauthn/#dictdef-registrationresponsejson).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-registrationresponsejson"/>
[IdlInterface("RegistrationResponseJSON", "https://w3c.github.io/webauthn/#dictdef-registrationresponsejson")]
public sealed class RegistrationResponseJson
{
    /// <summary>Gets the base64url encoding of the credential ID.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-registrationresponsejson-id"/>
    [IdlMember("id", "https://w3c.github.io/webauthn/#dom-registrationresponsejson-id")]
    public required string Id { get; init; }

    /// <summary>Gets the base64url encoding of the credential ID.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-registrationresponsejson-rawid"/>
    [IdlMember("rawId", "https://w3c.github.io/webauthn/#dom-registrationresponsejson-rawid")]
    public required string RawId { get; init; }

    /// <summary>Gets the response of the authenticator.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-registrationresponsejson-response"/>
    [IdlMember("response", "https://w3c.github.io/webauthn/#dom-registrationresponsejson-response")]
    public required AuthenticatorAttestationResponseJson Response { get; init; }

    /// <summary>Gets the attachment of the authenticator, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-registrationresponsejson-authenticatorattachment"/>
    [IdlMember("authenticatorAttachment", "https://w3c.github.io/webauthn/#dom-registrationresponsejson-authenticatorattachment")]
    public string? AuthenticatorAttachment { get; init; }

    /// <summary>Gets the outputs of the client extensions.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-registrationresponsejson-clientextensionresults"/>
    [IdlMember("clientExtensionResults", "https://w3c.github.io/webauthn/#dom-registrationresponsejson-clientextensionresults")]
    public required AuthenticationExtensionsClientOutputsJson ClientExtensionResults { get; init; }

    /// <summary>Gets the type of the credential (<c>public-key</c>).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-registrationresponsejson-type"/>
    [IdlMember("type", "https://w3c.github.io/webauthn/#dom-registrationresponsejson-type")]
    public required string Type { get; init; }
}

/// <summary>The JSON type of the response of a registration (https://w3c.github.io/webauthn/#dictdef-authenticatorattestationresponsejson).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-authenticatorattestationresponsejson"/>
[IdlInterface("AuthenticatorAttestationResponseJSON", "https://w3c.github.io/webauthn/#dictdef-authenticatorattestationresponsejson")]
public sealed class AuthenticatorAttestationResponseJson
{
    /// <summary>Gets the base64url encoding of the client data.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorattestationresponsejson-clientdatajson"/>
    [IdlMember("clientDataJSON", "https://w3c.github.io/webauthn/#dom-authenticatorattestationresponsejson-clientdatajson")]
    public required string ClientDataJson { get; init; }

    /// <summary>Gets the base64url encoding of the authenticator data.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorattestationresponsejson-authenticatordata"/>
    [IdlMember("authenticatorData", "https://w3c.github.io/webauthn/#dom-authenticatorattestationresponsejson-authenticatordata")]
    public required string AuthenticatorData { get; init; }

    /// <summary>Gets the transports of the authenticator.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorattestationresponsejson-transports"/>
    [IdlMember("transports", "https://w3c.github.io/webauthn/#dom-authenticatorattestationresponsejson-transports")]
    public required IReadOnlyList<string> Transports { get; init; }

    /// <summary>Gets the base64url encoding of the SubjectPublicKeyInfo of the credential, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorattestationresponsejson-publickey"/>
    [IdlMember("publicKey", "https://w3c.github.io/webauthn/#dom-authenticatorattestationresponsejson-publickey")]
    public string? PublicKey { get; init; }

    /// <summary>Gets the COSE algorithm identifier of the credential.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorattestationresponsejson-publickeyalgorithm"/>
    [IdlMember("publicKeyAlgorithm", "https://w3c.github.io/webauthn/#dom-authenticatorattestationresponsejson-publickeyalgorithm")]
    public required int PublicKeyAlgorithm { get; init; }

    /// <summary>Gets the base64url encoding of the attestation object.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorattestationresponsejson-attestationobject"/>
    [IdlMember("attestationObject", "https://w3c.github.io/webauthn/#dom-authenticatorattestationresponsejson-attestationobject")]
    public required string AttestationObject { get; init; }
}

/// <summary>The JSON type of a credential of an authentication (https://w3c.github.io/webauthn/#dictdef-authenticationresponsejson).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-authenticationresponsejson"/>
[IdlInterface("AuthenticationResponseJSON", "https://w3c.github.io/webauthn/#dictdef-authenticationresponsejson")]
public sealed class AuthenticationResponseJson
{
    /// <summary>Gets the base64url encoding of the credential ID.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationresponsejson-id"/>
    [IdlMember("id", "https://w3c.github.io/webauthn/#dom-authenticationresponsejson-id")]
    public required string Id { get; init; }

    /// <summary>Gets the base64url encoding of the credential ID.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationresponsejson-rawid"/>
    [IdlMember("rawId", "https://w3c.github.io/webauthn/#dom-authenticationresponsejson-rawid")]
    public required string RawId { get; init; }

    /// <summary>Gets the response of the authenticator.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationresponsejson-response"/>
    [IdlMember("response", "https://w3c.github.io/webauthn/#dom-authenticationresponsejson-response")]
    public required AuthenticatorAssertionResponseJson Response { get; init; }

    /// <summary>Gets the attachment of the authenticator, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationresponsejson-authenticatorattachment"/>
    [IdlMember("authenticatorAttachment", "https://w3c.github.io/webauthn/#dom-authenticationresponsejson-authenticatorattachment")]
    public string? AuthenticatorAttachment { get; init; }

    /// <summary>Gets the outputs of the client extensions.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationresponsejson-clientextensionresults"/>
    [IdlMember("clientExtensionResults", "https://w3c.github.io/webauthn/#dom-authenticationresponsejson-clientextensionresults")]
    public required AuthenticationExtensionsClientOutputsJson ClientExtensionResults { get; init; }

    /// <summary>Gets the type of the credential (<c>public-key</c>).</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticationresponsejson-type"/>
    [IdlMember("type", "https://w3c.github.io/webauthn/#dom-authenticationresponsejson-type")]
    public required string Type { get; init; }
}

/// <summary>The JSON type of the response of an authentication (https://w3c.github.io/webauthn/#dictdef-authenticatorassertionresponsejson).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-authenticatorassertionresponsejson"/>
[IdlInterface("AuthenticatorAssertionResponseJSON", "https://w3c.github.io/webauthn/#dictdef-authenticatorassertionresponsejson")]
public sealed class AuthenticatorAssertionResponseJson
{
    /// <summary>Gets the base64url encoding of the client data.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorassertionresponsejson-clientdatajson"/>
    [IdlMember("clientDataJSON", "https://w3c.github.io/webauthn/#dom-authenticatorassertionresponsejson-clientdatajson")]
    public required string ClientDataJson { get; init; }

    /// <summary>Gets the base64url encoding of the authenticator data.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorassertionresponsejson-authenticatordata"/>
    [IdlMember("authenticatorData", "https://w3c.github.io/webauthn/#dom-authenticatorassertionresponsejson-authenticatordata")]
    public required string AuthenticatorData { get; init; }

    /// <summary>Gets the base64url encoding of the signature.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorassertionresponsejson-signature"/>
    [IdlMember("signature", "https://w3c.github.io/webauthn/#dom-authenticatorassertionresponsejson-signature")]
    public required string Signature { get; init; }

    /// <summary>Gets the base64url encoding of the user handle, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-authenticatorassertionresponsejson-userhandle"/>
    [IdlMember("userHandle", "https://w3c.github.io/webauthn/#dom-authenticatorassertionresponsejson-userhandle")]
    public string? UserHandle { get; init; }
}

/// <summary>The options of <see cref="PublicKeyCredential.SignalUnknownCredential"/> (https://w3c.github.io/webauthn/#dictdef-unknowncredentialoptions).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-unknowncredentialoptions"/>
[IdlInterface("UnknownCredentialOptions", "https://w3c.github.io/webauthn/#dictdef-unknowncredentialoptions")]
public sealed class UnknownCredentialOptions
{
    /// <summary>Gets the RP ID.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-unknowncredentialoptions-rpid"/>
    [IdlMember("rpId", "https://w3c.github.io/webauthn/#dom-unknowncredentialoptions-rpid")]
    public required string RpId { get; init; }

    /// <summary>Gets the base64url encoding of the credential ID that the relying party does not recognize.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-unknowncredentialoptions-credentialid"/>
    [IdlMember("credentialId", "https://w3c.github.io/webauthn/#dom-unknowncredentialoptions-credentialid")]
    public required string CredentialId { get; init; }
}

/// <summary>The options of <see cref="PublicKeyCredential.SignalAllAcceptedCredentials"/> (https://w3c.github.io/webauthn/#dictdef-allacceptedcredentialsoptions).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-allacceptedcredentialsoptions"/>
[IdlInterface("AllAcceptedCredentialsOptions", "https://w3c.github.io/webauthn/#dictdef-allacceptedcredentialsoptions")]
public sealed class AllAcceptedCredentialsOptions
{
    /// <summary>Gets the RP ID.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-allacceptedcredentialsoptions-rpid"/>
    [IdlMember("rpId", "https://w3c.github.io/webauthn/#dom-allacceptedcredentialsoptions-rpid")]
    public required string RpId { get; init; }

    /// <summary>Gets the base64url encoding of the user handle.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-allacceptedcredentialsoptions-userid"/>
    [IdlMember("userId", "https://w3c.github.io/webauthn/#dom-allacceptedcredentialsoptions-userid")]
    public required string UserId { get; init; }

    /// <summary>Gets the base64url encodings of the credential IDs of the user.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-allacceptedcredentialsoptions-allacceptedcredentialids"/>
    [IdlMember("allAcceptedCredentialIds", "https://w3c.github.io/webauthn/#dom-allacceptedcredentialsoptions-allacceptedcredentialids")]
    public required IReadOnlyList<string> AllAcceptedCredentialIds { get; init; }
}

/// <summary>The options of <see cref="PublicKeyCredential.SignalCurrentUserDetails"/> (https://w3c.github.io/webauthn/#dictdef-currentuserdetailsoptions).</summary>
/// <seealso href="https://w3c.github.io/webauthn/#dictdef-currentuserdetailsoptions"/>
[IdlInterface("CurrentUserDetailsOptions", "https://w3c.github.io/webauthn/#dictdef-currentuserdetailsoptions")]
public sealed class CurrentUserDetailsOptions
{
    /// <summary>Gets the RP ID.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-currentuserdetailsoptions-rpid"/>
    [IdlMember("rpId", "https://w3c.github.io/webauthn/#dom-currentuserdetailsoptions-rpid")]
    public required string RpId { get; init; }

    /// <summary>Gets the base64url encoding of the user handle.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-currentuserdetailsoptions-userid"/>
    [IdlMember("userId", "https://w3c.github.io/webauthn/#dom-currentuserdetailsoptions-userid")]
    public required string UserId { get; init; }

    /// <summary>Gets the name of the user.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-currentuserdetailsoptions-name"/>
    [IdlMember("name", "https://w3c.github.io/webauthn/#dom-currentuserdetailsoptions-name")]
    public required string Name { get; init; }

    /// <summary>Gets the display name of the user.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-currentuserdetailsoptions-displayname"/>
    [IdlMember("displayName", "https://w3c.github.io/webauthn/#dom-currentuserdetailsoptions-displayname")]
    public required string DisplayName { get; init; }
}
