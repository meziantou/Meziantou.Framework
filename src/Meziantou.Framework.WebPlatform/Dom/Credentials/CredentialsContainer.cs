namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The mediation requirement of a credential request (https://w3c.github.io/webappsec-credential-management/#enumdef-credentialmediationrequirement).
/// </summary>
/// <seealso href="https://w3c.github.io/webappsec-credential-management/#enumdef-credentialmediationrequirement"/>
[IdlInterface("CredentialMediationRequirement", "https://w3c.github.io/webappsec-credential-management/#enumdef-credentialmediationrequirement")]
public enum CredentialMediationRequirement
{
    /// <summary>The user is not mediated: the request returns a credential only if no interaction is needed.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialmediationrequirement-silent"/>
    [IdlMember("silent", "https://w3c.github.io/webappsec-credential-management/#dom-credentialmediationrequirement-silent")]
    Silent,

    /// <summary>The user agent mediates the request when it needs to (the default).</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialmediationrequirement-optional"/>
    [IdlMember("optional", "https://w3c.github.io/webappsec-credential-management/#dom-credentialmediationrequirement-optional")]
    Optional,

    /// <summary>The credentials are offered without a modal dialog (e.g. in the autofill of a form control).</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialmediationrequirement-conditional"/>
    [IdlMember("conditional", "https://w3c.github.io/webappsec-credential-management/#dom-credentialmediationrequirement-conditional")]
    Conditional,

    /// <summary>The user always mediates the request.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialmediationrequirement-required"/>
    [IdlMember("required", "https://w3c.github.io/webappsec-credential-management/#dom-credentialmediationrequirement-required")]
    Required,
}

/// <summary>The options of <see cref="CredentialsContainer.Get"/> (https://w3c.github.io/webappsec-credential-management/#dictdef-credentialrequestoptions).</summary>
/// <seealso href="https://w3c.github.io/webappsec-credential-management/#dictdef-credentialrequestoptions"/>
[IdlInterface("CredentialRequestOptions", "https://w3c.github.io/webappsec-credential-management/#dictdef-credentialrequestoptions")]
public sealed class CredentialRequestOptions
{
    /// <summary>Gets the mediation requirement of the request.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialrequestoptions-mediation"/>
    [IdlMember("mediation", "https://w3c.github.io/webappsec-credential-management/#dom-credentialrequestoptions-mediation")]
    public CredentialMediationRequirement Mediation { get; init; } = CredentialMediationRequirement.Optional;

    /// <summary>Gets the user interface mode of the request (a value of <c>CredentialUiMode</c>), or null.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialrequestoptions-uimode"/>
    [IdlMember("uiMode", "https://w3c.github.io/webappsec-credential-management/#dom-credentialrequestoptions-uimode")]
    public string? UiMode { get; init; }

    /// <summary>Gets the signal that aborts the request.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialrequestoptions-signal"/>
    [IdlMember("signal", "https://w3c.github.io/webappsec-credential-management/#dom-credentialrequestoptions-signal")]
    public AbortSignal? Signal { get; init; }

    /// <summary>Gets the options of a Web Authentication assertion, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-credentialrequestoptions-publickey"/>
    [IdlMember("publicKey", "https://w3c.github.io/webauthn/#dom-credentialrequestoptions-publickey")]
    public PublicKeyCredentialRequestOptions? PublicKey { get; init; }
}

/// <summary>The options of <see cref="CredentialsContainer.Create"/> (https://w3c.github.io/webappsec-credential-management/#dictdef-credentialcreationoptions).</summary>
/// <seealso href="https://w3c.github.io/webappsec-credential-management/#dictdef-credentialcreationoptions"/>
[IdlInterface("CredentialCreationOptions", "https://w3c.github.io/webappsec-credential-management/#dictdef-credentialcreationoptions")]
public sealed class CredentialCreationOptions
{
    /// <summary>Gets the mediation requirement of the request.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialcreationoptions-mediation"/>
    [IdlMember("mediation", "https://w3c.github.io/webappsec-credential-management/#dom-credentialcreationoptions-mediation")]
    public CredentialMediationRequirement Mediation { get; init; } = CredentialMediationRequirement.Optional;

    /// <summary>Gets the signal that aborts the request.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialcreationoptions-signal"/>
    [IdlMember("signal", "https://w3c.github.io/webappsec-credential-management/#dom-credentialcreationoptions-signal")]
    public AbortSignal? Signal { get; init; }

    /// <summary>Gets the options of a Web Authentication registration, or null.</summary>
    /// <seealso href="https://w3c.github.io/webauthn/#dom-credentialcreationoptions-publickey"/>
    [IdlMember("publicKey", "https://w3c.github.io/webauthn/#dom-credentialcreationoptions-publickey")]
    public PublicKeyCredentialCreationOptions? PublicKey { get; init; }
}

/// <summary>
/// A credential (https://w3c.github.io/webappsec-credential-management/#credential): the only type of credential of the
/// library is <see cref="PublicKeyCredential"/> (Web Authentication).
/// </summary>
/// <seealso href="https://w3c.github.io/webappsec-credential-management/#credential"/>
[IdlInterface("Credential", "https://w3c.github.io/webappsec-credential-management/#credential")]
public abstract class Credential
{
    private protected Credential()
    {
    }

    /// <summary>Gets the identifier of the credential.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credential-id"/>
    [IdlMember("id", "https://w3c.github.io/webappsec-credential-management/#dom-credential-id")]
    public abstract string Id { get; }

    /// <summary>Gets the type of the credential (the [[type]] of its interface, e.g. <c>public-key</c>).</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credential-type"/>
    [IdlMember("type", "https://w3c.github.io/webappsec-credential-management/#dom-credential-type")]
    public abstract string Type { get; }

    /// <summary>The default implementation (https://w3c.github.io/webappsec-credential-management/#dom-credential-isconditionalmediationavailable): false.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credential-isconditionalmediationavailable"/>
    [IdlMember("isConditionalMediationAvailable", "https://w3c.github.io/webappsec-credential-management/#dom-credential-isconditionalmediationavailable")]
    public static Task<bool> IsConditionalMediationAvailable() => Task.FromResult(false);
}

/// <summary>
/// The credentials API of a window (https://w3c.github.io/webappsec-credential-management/#credentialscontainer):
/// <c>navigator.credentials</c>. The library implements the Web Authentication credentials (<see cref="PublicKeyCredential"/>),
/// whose authenticators are given by <see cref="BrowsingContextOptions.WebAuthentication"/>.
/// </summary>
/// <seealso href="https://w3c.github.io/webappsec-credential-management/#credentialscontainer"/>
[IdlInterface("CredentialsContainer", "https://w3c.github.io/webappsec-credential-management/#credentialscontainer")]
public sealed class CredentialsContainer
{
    private const string PublicKeyType = "public-key";

    private readonly Window _window;

    /// <summary>https://w3c.github.io/webappsec-credential-management/#environment-settings-object-active-credential-types</summary>
    private readonly HashSet<string> _activeCredentialTypes = new(StringComparer.Ordinal);

    internal CredentialsContainer(Window window)
    {
        _window = window;
    }

    /// <summary>
    /// Requests a credential (https://w3c.github.io/webappsec-credential-management/#algorithm-request): a Web Authentication
    /// assertion when <see cref="CredentialRequestOptions.PublicKey"/> is set. The task fails with an <c>InvalidStateError</c>
    /// <see cref="DomException"/> when the document is not fully active, a <c>NotSupportedError</c> when the options request no
    /// supported type of credential, a <c>NotAllowedError</c> when the document is not allowed to use the
    /// <c>publickey-credentials-get</c> feature or a request is already pending, and the errors of the ceremony.
    /// </summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialscontainer-get"/>
    [IdlMember("get", "https://w3c.github.io/webappsec-credential-management/#dom-credentialscontainer-get")]
    public Task<Credential?> Get(CredentialRequestOptions? options = null)
    {
        options ??= new CredentialRequestOptions();
        var document = _window.Document;
        if (!_window.IsSecureContext)
            return Task.FromException<Credential?>(new DomException("The credentials can only be requested in a secure context", "SecurityError"));

        if (!document.IsFullyActive)
            return Task.FromException<Credential?>(DomException.InvalidState("The document is not fully active"));

        if (options.Signal is { Aborted: true } signal)
            return Task.FromException<Credential?>(WebAuthenticationClient.ToException(signal.Reason));

        if (options.PublicKey is null)
            return Task.FromException<Credential?>(DomException.NotSupported("The options do not request a supported type of credential"));

        if (document.Origin.IsOpaque)
            return Task.FromException<Credential?>(new DomException("The origin of the document is opaque", "SecurityError"));

        // https://w3c.github.io/webauthn/#sctn-conditional-mediation: PublicKeyCredential supports the conditional mediation,
        // not the immediate UI mode (which no specification defines for it)
        if (options.UiMode == "immediate")
            return Task.FromException<Credential?>(new TypeErrorException("PublicKeyCredential does not support the immediate UI mode"));

        if (!_activeCredentialTypes.Add(PublicKeyType))
            return Task.FromException<Credential?>(DomException.NotAllowed("A request for a public key credential is already pending"));

        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.PublicKeyCredentialsGet))
        {
            _activeCredentialTypes.Remove(PublicKeyType);
            return Task.FromException<Credential?>(DomException.NotAllowed("The document is not allowed to use the publickey-credentials-get feature"));
        }

        return WhenSettled(WebAuthenticationClient.DiscoverFromExternalSource(_window, options, IsSameOriginWithAncestors(document)));
    }

    /// <summary>
    /// Stores a credential (https://w3c.github.io/webappsec-credential-management/#algorithm-store). The public key
    /// credentials cannot be stored: the task fails with a <c>NotSupportedError</c> <see cref="DomException"/>.
    /// </summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialscontainer-store"/>
    [IdlMember("store", "https://w3c.github.io/webappsec-credential-management/#dom-credentialscontainer-store")]
    public Task Store(Credential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (!_window.IsSecureContext)
            return Task.FromException(new DomException("The credentials can only be stored in a secure context", "SecurityError"));

        if (!_window.Document.IsFullyActive)
            return Task.FromException(DomException.InvalidState("The document is not fully active"));

        if (_window.Document.Origin.IsOpaque)
            return Task.FromException(new DomException("The origin of the document is opaque", "SecurityError"));

        if (!_activeCredentialTypes.Add(credential.Type))
            return Task.FromException(DomException.NotAllowed("A request for this type of credential is already pending"));

        // https://w3c.github.io/webauthn/#sctn-storeCredential: [[Store]] throws, the promise is rejected by a task
        var result = new TaskCompletionSource();
        _window.QueueGlobalTask(() =>
        {
            _activeCredentialTypes.Remove(credential.Type);
            result.TrySetException(DomException.NotSupported("The public key credentials cannot be stored"));
        });
        return result.Task;
    }

    /// <summary>
    /// Creates a credential (https://w3c.github.io/webappsec-credential-management/#algorithm-create): a Web Authentication
    /// registration when <see cref="CredentialCreationOptions.PublicKey"/> is set. The task fails with an
    /// <c>InvalidStateError</c> <see cref="DomException"/> when the document is not fully active, a <c>NotSupportedError</c>
    /// when the options request no supported type of credential, a <c>NotAllowedError</c> when the document is not allowed to
    /// use the <c>publickey-credentials-create</c> feature or a request is already pending, and the errors of the ceremony.
    /// </summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialscontainer-create"/>
    [IdlMember("create", "https://w3c.github.io/webappsec-credential-management/#dom-credentialscontainer-create")]
    public Task<Credential?> Create(CredentialCreationOptions? options = null)
    {
        options ??= new CredentialCreationOptions();
        var document = _window.Document;
        if (!_window.IsSecureContext)
            return Task.FromException<Credential?>(new DomException("The credentials can only be created in a secure context", "SecurityError"));

        if (!document.IsFullyActive)
            return Task.FromException<Credential?>(DomException.InvalidState("The document is not fully active"));

        var sameOriginWithAncestors = IsSameOriginWithAncestors(document);
        if (options.PublicKey is null)
            return Task.FromException<Credential?>(DomException.NotSupported("The options do not request a supported type of credential"));

        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.PublicKeyCredentialsCreate))
            return Task.FromException<Credential?>(DomException.NotAllowed("The document is not allowed to use the publickey-credentials-create feature"));

        if (options.Signal is { Aborted: true } signal)
            return Task.FromException<Credential?>(WebAuthenticationClient.ToException(signal.Reason));

        if (document.Origin.IsOpaque)
            return Task.FromException<Credential?>(new DomException("The origin of the document is opaque", "SecurityError"));

        if (!_activeCredentialTypes.Add(PublicKeyType))
            return Task.FromException<Credential?>(DomException.NotAllowed("A request for a public key credential is already pending"));

        return WhenSettled(WebAuthenticationClient.Create(_window, options, sameOriginWithAncestors));
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-credential-management/#dom-credentialscontainer-preventsilentaccess: it has no effect
    /// on the public key credentials, which always require an interaction of the user.
    /// </summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialscontainer-preventsilentaccess"/>
    [IdlMember("preventSilentAccess", "https://w3c.github.io/webappsec-credential-management/#dom-credentialscontainer-preventsilentaccess")]
    public Task PreventSilentAccess()
    {
        if (!_window.Document.IsFullyActive)
            return Task.FromException(DomException.InvalidState("The document is not fully active"));

        return Task.CompletedTask;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#same-origin-with-its-ancestors</summary>
    internal static bool IsSameOriginWithAncestors(Document document)
    {
        for (var navigable = document.BrowsingContext?.Parent; navigable is not null; navigable = navigable.Parent)
        {
            if (!OriginRecord.IsSameOrigin(navigable.Document.Origin, document.Origin))
                return false;
        }

        return true;
    }

    /// <summary>The type is removed from the active credential types once the promise is settled.</summary>
    private Task<Credential?> WhenSettled(Task<Credential?> task)
    {
        if (task.IsCompleted)
        {
            _activeCredentialTypes.Remove(PublicKeyType);
            return task;
        }

        var result = new TaskCompletionSource<Credential?>();
        _ = task.ContinueWith(completed =>
        {
            _activeCredentialTypes.Remove(PublicKeyType);
            if (completed.Exception is { } exception)
            {
                result.TrySetException(exception.InnerExceptions);
            }
            else
            {
                result.TrySetResult(completed.Result);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return result.Task;
    }
}

// The Credential Management API (https://w3c.github.io/webappsec-credential-management/#framework-credential-management)
public sealed partial class Navigator
{
    private CredentialsContainer? _credentials;

    /// <summary>Gets the credentials API of the window (https://w3c.github.io/webappsec-credential-management/#dom-navigator-credentials).</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-navigator-credentials"/>
    [IdlMember("credentials", "https://w3c.github.io/webappsec-credential-management/#dom-navigator-credentials")]
    public CredentialsContainer Credentials => _credentials ??= new CredentialsContainer(_window);
}
