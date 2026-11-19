namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The data of a <see cref="FederatedCredential"/> (https://w3c.github.io/webappsec-credential-management/#dictdef-federatedcredentialinit).</summary>
/// <seealso href="https://w3c.github.io/webappsec-credential-management/#dictdef-federatedcredentialinit"/>
[IdlInterface("FederatedCredentialInit", "https://w3c.github.io/webappsec-credential-management/#dictdef-federatedcredentialinit")]
public sealed class FederatedCredentialInit : CredentialData
{
    /// <summary>Gets the name of the account, displayed by the credential chooser.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-federatedcredentialinit-name"/>
    [IdlMember("name", "https://w3c.github.io/webappsec-credential-management/#dom-federatedcredentialinit-name")]
    public string? Name { get; init; }

    /// <summary>Gets the URL of the image of the account, displayed by the credential chooser.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-federatedcredentialinit-iconurl"/>
    [IdlMember("iconURL", "https://w3c.github.io/webappsec-credential-management/#dom-federatedcredentialinit-iconurl")]
    public string? IconUrl { get; init; }

    /// <summary>Gets the serialization of the origin of the credential (e.g. <c>https://example.com</c>).</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-federatedcredentialinit-origin"/>
    [IdlMember("origin", "https://w3c.github.io/webappsec-credential-management/#dom-federatedcredentialinit-origin")]
    public required string Origin { get; init; }

    /// <summary>Gets the serialization of the origin of the identity provider (e.g. <c>https://accounts.example</c>).</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-federatedcredentialinit-provider"/>
    [IdlMember("provider", "https://w3c.github.io/webappsec-credential-management/#dom-federatedcredentialinit-provider")]
    public required string Provider { get; init; }

    /// <summary>Gets the protocol of the identity provider (e.g. <c>openidconnect</c>), or null.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-federatedcredentialinit-protocol"/>
    [IdlMember("protocol", "https://w3c.github.io/webappsec-credential-management/#dom-federatedcredentialinit-protocol")]
    public string? Protocol { get; init; }
}

/// <summary>The federated credentials requested by <c>navigator.credentials.get()</c> (https://w3c.github.io/webappsec-credential-management/#dictdef-federatedcredentialrequestoptions).</summary>
/// <seealso href="https://w3c.github.io/webappsec-credential-management/#dictdef-federatedcredentialrequestoptions"/>
[IdlInterface("FederatedCredentialRequestOptions", "https://w3c.github.io/webappsec-credential-management/#dictdef-federatedcredentialrequestoptions")]
public sealed class FederatedCredentialRequestOptions
{
    /// <summary>Gets the identity providers whose credentials are requested (the serializations of their origins), or null for any provider.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-federatedcredentialrequestoptions-providers"/>
    [IdlMember("providers", "https://w3c.github.io/webappsec-credential-management/#dom-federatedcredentialrequestoptions-providers")]
    public IReadOnlyList<string>? Providers { get; init; }

    /// <summary>Gets the protocols of the credentials that are requested, or null for any protocol.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-federatedcredentialrequestoptions-protocols"/>
    [IdlMember("protocols", "https://w3c.github.io/webappsec-credential-management/#dom-federatedcredentialrequestoptions-protocols")]
    public IReadOnlyList<string>? Protocols { get; init; }
}

/// <summary>
/// A credential of a federated identity provider (https://w3c.github.io/webappsec-credential-management/#federatedcredential-interface):
/// the user signs in to the site with an account of the provider, and the site authenticates the user with the protocol of
/// the provider. The library stores no credential: see <see cref="UserAgentUIBackend.ChooseCredentialAsync"/> and
/// <see cref="UserAgentUIBackend.StoreCredentialAsync"/>.
/// </summary>
/// <seealso href="https://w3c.github.io/webappsec-credential-management/#federatedcredential"/>
[IdlInterface("FederatedCredential", "https://w3c.github.io/webappsec-credential-management/#federatedcredential")]
public sealed class FederatedCredential : Credential
{
    /// <summary>
    /// Creates a credential from its data (https://w3c.github.io/webappsec-credential-management/#construct-federatedcredential-data).
    /// Throws a <see cref="TypeErrorException"/> when the identifier or the provider is empty.
    /// </summary>
    /// <param name="data">The data of the credential.</param>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-federatedcredential-federatedcredential"/>
    [IdlMember("constructor", "https://w3c.github.io/webappsec-credential-management/#dom-federatedcredential-federatedcredential")]
    public FederatedCredential(FederatedCredentialInit data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Id.Length == 0 || data.Provider.Length == 0)
            throw new TypeErrorException("The id and the provider of a FederatedCredential cannot be empty");

        Id = data.Id;
        Provider = data.Provider;
        Protocol = data.Protocol;
        Name = data.Name ?? "";
        IconUrl = data.IconUrl ?? "";
        Origin = data.Origin;
    }

    /// <inheritdoc/>
    public override string Id { get; }

    /// <inheritdoc/>
    public override string Type => "federated";

    /// <summary>Gets the serialization of the origin of the identity provider.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-federatedcredential-provider"/>
    [IdlMember("provider", "https://w3c.github.io/webappsec-credential-management/#dom-federatedcredential-provider")]
    public string Provider { get; }

    /// <summary>Gets the protocol of the identity provider, or null.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-federatedcredential-protocol"/>
    [IdlMember("protocol", "https://w3c.github.io/webappsec-credential-management/#dom-federatedcredential-protocol")]
    public string? Protocol { get; }

    /// <summary>Gets the name of the account (the empty string when there is none).</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialuserdata-name"/>
    [IdlMember("name", "https://w3c.github.io/webappsec-credential-management/#dom-credentialuserdata-name")]
    public string Name { get; }

    /// <summary>Gets the URL of the image of the account (the empty string when there is none).</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialuserdata-iconurl"/>
    [IdlMember("iconURL", "https://w3c.github.io/webappsec-credential-management/#dom-credentialuserdata-iconurl")]
    public string IconUrl { get; }

    /// <summary>Gets the serialization of the origin of the credential ([[origin]]): the site that the credential signs in to.</summary>
    [NotInIdl("The [[origin]] internal slot of the credential, which the scripts cannot read")]
    public string Origin { get; }
}
