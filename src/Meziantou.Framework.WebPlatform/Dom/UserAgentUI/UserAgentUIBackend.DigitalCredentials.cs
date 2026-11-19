namespace Meziantou.Framework.WebPlatform.Dom;

// The digital credential chooser of the Digital Credentials API (https://w3c-fedid.github.io/digital-credentials/)
public abstract partial class UserAgentUIBackend
{
    /// <summary>
    /// Shows the digital credential chooser (https://w3c-fedid.github.io/digital-credentials/#dfn-digital-credential-chooser):
    /// the user selects a wallet and a credential that answers one of the requests of <c>navigator.credentials.get({ digital })</c>
    /// (a presentation) or <c>navigator.credentials.create({ digital })</c> (an issuance), and the wallet returns its response.
    /// The default implementation returns null: the user cancels, and the promise of the page is rejected with a
    /// <c>NotAllowedError</c>.
    /// </summary>
    /// <param name="prompt">The requests.</param>
    /// <param name="cancellationToken">Canceled when the request is aborted.</param>
    /// <returns>The response of the wallet, or null when the user cancels.</returns>
    public virtual ValueTask<DigitalCredentialResponse?> RequestDigitalCredentialAsync(DigitalCredentialPrompt prompt, CancellationToken cancellationToken) => new((DigitalCredentialResponse?)null);
}

/// <summary>The kind of a digital credential request.</summary>
public enum DigitalCredentialRequestKind
{
    /// <summary>A presentation: <c>navigator.credentials.get({ digital })</c> asks a wallet to present a credential.</summary>
    Presentation,

    /// <summary>An issuance: <c>navigator.credentials.create({ digital })</c> asks a wallet to store a new credential.</summary>
    Issuance,
}

/// <summary>A request of a digital credential: its protocol, and its data serialized to JSON.</summary>
/// <param name="Protocol">The protocol of the request (e.g. <c>openid4vp-v1-unsigned</c>, <c>org-iso-mdoc</c> or <c>openid4vci-v1</c>).</param>
/// <param name="Data">The data of the request, specific to the protocol, serialized to JSON.</param>
public sealed record DigitalCredentialRequest(string Protocol, string Data);

/// <summary>The response of a wallet to a digital credential request: the protocol of the request it answered, and its response serialized to JSON.</summary>
/// <param name="Protocol">The protocol of the request that the wallet answered.</param>
/// <param name="Data">The response, specific to the protocol: a JSON object (the <see cref="DigitalCredential.Data"/> of the credential).</param>
public sealed record DigitalCredentialResponse(string Protocol, string Data);

/// <summary>The requests shown in the digital credential chooser, given to <see cref="UserAgentUIBackend.RequestDigitalCredentialAsync"/>.</summary>
public sealed class DigitalCredentialPrompt : UserAgentPrompt
{
    internal DigitalCredentialPrompt(Window window, DigitalCredentialRequestKind kind, IReadOnlyList<DigitalCredentialRequest> requests)
        : base(window)
    {
        Kind = kind;
        Requests = requests;
    }

    /// <summary>Gets whether the page asks for a presentation or an issuance.</summary>
    public DigitalCredentialRequestKind Kind { get; }

    /// <summary>Gets the requests of the protocols that the user agent allows, in the order of the page: the wallet answers one of them.</summary>
    public IReadOnlyList<DigitalCredentialRequest> Requests { get; }
}
