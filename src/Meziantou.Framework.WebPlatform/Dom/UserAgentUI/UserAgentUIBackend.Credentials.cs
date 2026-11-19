namespace Meziantou.Framework.WebPlatform.Dom;

// The prompts of the Credential Management API (https://w3c.github.io/webappsec-credential-management/): the credential
// store of the user agent is the one of the backend
public abstract partial class UserAgentUIBackend
{
    /// <summary>
    /// Asks the user to choose a credential of the credential store of the user agent
    /// (https://w3c.github.io/webappsec-credential-management/#abstract-opdef-ask-the-user-to-choose-a-credential), for
    /// <c>navigator.credentials.get()</c> with the <c>password</c> or <c>federated</c> member: the stored
    /// <see cref="PasswordCredential"/> and <see cref="FederatedCredential"/> of <see cref="UserAgentPrompt.Origin"/> that match
    /// the request. When <see cref="CredentialChooserPrompt.Mediation"/> is <see cref="CredentialMediationRequirement.Silent"/>,
    /// the backend must not show anything: it returns the only matching credential or null. The default implementation returns
    /// null: there is no stored credential (or the user dismissed the chooser).
    /// </summary>
    /// <remarks>
    /// A credential that does not match the request (another origin, a type or a provider that was not requested) is ignored.
    /// </remarks>
    /// <param name="prompt">The request.</param>
    /// <param name="cancellationToken">Canceled when the request is aborted.</param>
    /// <returns>The chosen credential, or null.</returns>
    public virtual ValueTask<Credential?> ChooseCredentialAsync(CredentialChooserPrompt prompt, CancellationToken cancellationToken) => new((Credential?)null);

    /// <summary>
    /// Asks the user whether to save a credential in the credential store of the user agent (<c>navigator.credentials.store()</c>
    /// of a <see cref="PasswordCredential"/> or a <see cref="FederatedCredential"/>, https://w3c.github.io/webappsec-credential-management/#algorithm-store):
    /// the backend saves it (or updates the stored credential that has the same identifier) when the user accepts. The page
    /// is not told whether the credential was saved. The default implementation saves nothing.
    /// </summary>
    /// <param name="prompt">The credential to save.</param>
    /// <param name="cancellationToken">Canceled when the page no longer waits for the answer.</param>
    /// <returns>Whether the credential was saved.</returns>
    public virtual ValueTask<bool> StoreCredentialAsync(CredentialStorePrompt prompt, CancellationToken cancellationToken) => new(false);
}

/// <summary>A request for a stored credential, given to <see cref="UserAgentUIBackend.ChooseCredentialAsync"/>.</summary>
public sealed class CredentialChooserPrompt : UserAgentPrompt
{
    internal CredentialChooserPrompt(Window window, CredentialRequestOptions options, bool requiresUserMediation)
        : base(window)
    {
        Mediation = options.Mediation;
        Password = options.Password;
        Federated = options.Federated;
        RequiresUserMediation = requiresUserMediation;
    }

    /// <summary>Gets the mediation requirement of the request.</summary>
    public CredentialMediationRequirement Mediation { get; }

    /// <summary>Gets whether the stored <see cref="PasswordCredential"/> of the origin are requested.</summary>
    public bool Password { get; }

    /// <summary>Gets the federated credentials that are requested (the stored <see cref="FederatedCredential"/> of the origin whose provider and protocol match), or null.</summary>
    public FederatedCredentialRequestOptions? Federated { get; }

    /// <summary>
    /// Gets whether the origin requires user mediation (https://w3c.github.io/webappsec-credential-management/#origin-requires-user-mediation):
    /// <c>navigator.credentials.preventSilentAccess()</c> was called, so the backend must not return a credential without
    /// asking the user.
    /// </summary>
    public bool RequiresUserMediation { get; }

    /// <summary>Returns whether a credential matches the request (its type, origin, provider and protocol).</summary>
    /// <param name="credential">The credential.</param>
    public bool Matches(Credential credential) => credential switch
    {
        PasswordCredential password => Password && password.Origin == Origin,
        FederatedCredential federated => Federated is { } options && federated.Origin == Origin
            && (options.Providers is null || options.Providers.Contains(federated.Provider, StringComparer.Ordinal))
            && (options.Protocols is null || (federated.Protocol is { } protocol && options.Protocols.Contains(protocol, StringComparer.Ordinal))),
        _ => false,
    };
}

/// <summary>A credential to save, given to <see cref="UserAgentUIBackend.StoreCredentialAsync"/>.</summary>
public sealed class CredentialStorePrompt : UserAgentPrompt
{
    internal CredentialStorePrompt(Window window, Credential credential)
        : base(window)
    {
        Credential = credential;
    }

    /// <summary>Gets the credential to save: a <see cref="PasswordCredential"/> or a <see cref="FederatedCredential"/>.</summary>
    public Credential Credential { get; }
}
