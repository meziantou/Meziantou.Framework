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

    /// <summary>Gets whether the stored <see cref="PasswordCredential"/> of the origin are requested.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialrequestoptions-password"/>
    [IdlMember("password", "https://w3c.github.io/webappsec-credential-management/#dom-credentialrequestoptions-password")]
    public bool Password { get; init; }

    /// <summary>Gets the federated credentials that are requested, or null.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialrequestoptions-federated"/>
    [IdlMember("federated", "https://w3c.github.io/webappsec-credential-management/#dom-credentialrequestoptions-federated")]
    public FederatedCredentialRequestOptions? Federated { get; init; }

    /// <summary>Gets the digital credential presentation requests, or null.</summary>
    /// <seealso href="https://w3c-fedid.github.io/digital-credentials/#dom-credentialrequestoptions-digital"/>
    [IdlMember("digital", "https://w3c-fedid.github.io/digital-credentials/#dom-credentialrequestoptions-digital")]
    public DigitalCredentialRequestOptions? Digital { get; init; }

    /// <summary>Gets the FedCM request (the identity providers of the site), or null.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-credentialrequestoptions-identity"/>
    [IdlMember("identity", "https://w3c-fedid.github.io/FedCM/#dom-credentialrequestoptions-identity")]
    public IdentityCredentialRequestOptions? Identity { get; init; }
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

    /// <summary>
    /// Gets the data of the <see cref="PasswordCredential"/> to create: a <see cref="PasswordCredentialData"/> or an
    /// <see cref="HtmlFormElement"/> (see <see cref="PasswordCredential(HtmlFormElement)"/>), or null.
    /// </summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialcreationoptions-password"/>
    [IdlMember("password", "https://w3c.github.io/webappsec-credential-management/#dom-credentialcreationoptions-password")]
    public object? Password { get; init; }

    /// <summary>Gets the data of the <see cref="FederatedCredential"/> to create (its origin is the origin of the document), or null.</summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialcreationoptions-federated"/>
    [IdlMember("federated", "https://w3c.github.io/webappsec-credential-management/#dom-credentialcreationoptions-federated")]
    public FederatedCredentialInit? Federated { get; init; }

    /// <summary>Gets the digital credential issuance requests, or null.</summary>
    /// <seealso href="https://w3c-fedid.github.io/digital-credentials/#dom-credentialcreationoptions-digital"/>
    [IdlMember("digital", "https://w3c-fedid.github.io/digital-credentials/#dom-credentialcreationoptions-digital")]
    public DigitalCredentialCreationOptions? Digital { get; init; }
}

/// <summary>
/// A credential (https://w3c.github.io/webappsec-credential-management/#credential): a <see cref="PasswordCredential"/>, a
/// <see cref="FederatedCredential"/> or a <see cref="PublicKeyCredential"/> (Web Authentication).
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

    /// <summary>Gets the type of the credential (the [[type]] of its interface, e.g. <c>password</c> or <c>public-key</c>).</summary>
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
/// <c>navigator.credentials</c>. The library implements the password and federated credentials (<see cref="PasswordCredential"/>,
/// <see cref="FederatedCredential"/>), whose credential store and chooser are the ones of <see cref="BrowsingContextOptions.UserAgentUI"/>,
/// and the Web Authentication credentials (<see cref="PublicKeyCredential"/>), whose authenticators are given by
/// <see cref="BrowsingContextOptions.WebAuthentication"/>.
/// </summary>
/// <seealso href="https://w3c.github.io/webappsec-credential-management/#credentialscontainer"/>
[IdlInterface("CredentialsContainer", "https://w3c.github.io/webappsec-credential-management/#credentialscontainer")]
public sealed class CredentialsContainer
{
    private const string PasswordType = "password";
    private const string FederatedType = "federated";
    private const string PublicKeyType = "public-key";
    private const string DigitalType = "digital-credential";
    private const string IdentityType = "identity";

    private readonly Window _window;

    /// <summary>https://w3c.github.io/webappsec-credential-management/#environment-settings-object-active-credential-types</summary>
    private readonly HashSet<string> _activeCredentialTypes = new(StringComparer.Ordinal);

    /// <summary>https://w3c-fedid.github.io/digital-credentials/#dfn-request-coordinator</summary>
    private readonly DigitalCredential.Coordinator _digitalCredentials = new();

    internal CredentialsContainer(Window window)
    {
        _window = window;
    }

    /// <summary>
    /// Requests a credential (https://w3c.github.io/webappsec-credential-management/#algorithm-request): the stored password
    /// or federated credentials chosen by the user (<see cref="UserAgentUIBackend.ChooseCredentialAsync"/>) when
    /// <see cref="CredentialRequestOptions.Password"/> or <see cref="CredentialRequestOptions.Federated"/> is set, or a Web
    /// Authentication assertion when <see cref="CredentialRequestOptions.PublicKey"/> is set. The task fails with an
    /// <c>InvalidStateError</c> <see cref="DomException"/> when the document is not fully active, a <c>NotSupportedError</c>
    /// when the options request no supported type of credential or a combination of types that cannot be requested together,
    /// a <c>NotAllowedError</c> when the document is not allowed to use the feature of the type, a request is already pending
    /// or the document is in a frame that is not same origin with its ancestors (password and federated credentials), and the
    /// errors of the ceremony.
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

        // https://w3c.github.io/webappsec-credential-management/#credentialrequestoptions-relevant-credential-interface-objects:
        // the boolean password member is relevant when it is true. The password and federated credentials are the only types
        // that can be requested together.
        var types = GetRequestedTypes(options);
        if (types.Count == 0)
            return Task.FromException<Credential?>(DomException.NotSupported("The options do not request a supported type of credential"));

        if (types.Count > 1 && types.Exists(type => type is not (PasswordType or FederatedType)))
            return Task.FromException<Credential?>(DomException.NotSupported("These types of credentials cannot be requested together"));

        // FedCM (https://w3c-fedid.github.io/FedCM/#browser-api-credential-management-api): an opaque origin is a NetworkError
        if (types[0] is IdentityType)
            return IdentityCredentialRequest.Get(_window, options);

        if (document.Origin.IsOpaque)
            return Task.FromException<Credential?>(new DomException("The origin of the document is opaque", "SecurityError"));

        if (types[0] is PasswordType or FederatedType)
            return GetStoredCredential(options);

        if (types[0] is DigitalType)
            return GetDigitalCredential(options);

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

        return WhenSettled(WebAuthenticationClient.DiscoverFromExternalSource(_window, options, IsSameOriginWithAncestors(document)), PublicKeyType);
    }

    /// <summary>
    /// The digital credentials (https://w3c-fedid.github.io/digital-credentials/#sctn-discoverfromexternalsource): the mediation is
    /// implicitly <c>required</c>, the document must be allowed to use the <c>digital-credentials-get</c> feature.
    /// </summary>
    private Task<Credential?> GetDigitalCredential(CredentialRequestOptions options)
    {
        // The concurrent requests are rejected by the request coordinator, from a task (https://w3c-fedid.github.io/digital-credentials/#dfn-prepare-credential-requests):
        // the type is not added to the active credential types
        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(_window.Document, PolicyControlledFeature.DigitalCredentialsGet))
            return Task.FromException<Credential?>(DomException.NotAllowed("The document is not allowed to use the digital-credentials-get feature"));

        var requests = options.Digital!.Requests.Select(request => (request.Protocol, request.Data));
        return DigitalCredential.Request(_window, _digitalCredentials, DigitalCredentialRequestKind.Presentation, requests, options.Signal);
    }

    /// <summary>The [[type]] of the relevant credential interface objects of the options, in the order of the options.</summary>
    private static List<string> GetRequestedTypes(CredentialRequestOptions options)
    {
        var types = new List<string>(1);
        if (options.Password)
        {
            types.Add(PasswordType);
        }

        if (options.Federated is not null)
        {
            types.Add(FederatedType);
        }

        if (options.PublicKey is not null)
        {
            types.Add(PublicKeyType);
        }

        if (options.Digital is not null)
        {
            types.Add(DigitalType);
        }

        if (options.Identity is not null)
        {
            types.Add(IdentityType);
        }

        return types;
    }

    /// <summary>
    /// The password and federated credentials (https://w3c.github.io/webappsec-credential-management/#algorithm-collect-known):
    /// [[CollectFromCredentialStore]] and the choice of the user are the ones of the user agent UI backend.
    /// </summary>
    private Task<Credential?> GetStoredCredential(CredentialRequestOptions options)
    {
        var document = _window.Document;
        if (options.Mediation == CredentialMediationRequirement.Conditional)
            return Task.FromException<Credential?>(new TypeErrorException("The password and federated credentials do not support the conditional mediation"));

        // https://w3c.github.io/webappsec-credential-management/#algorithm-request, step 11: the types are active until the
        // promise is settled
        var types = GetRequestedTypes(options);
        foreach (var type in types)
        {
            if (_activeCredentialTypes.Contains(type))
                return Task.FromException<Credential?>(DomException.NotAllowed("A request for this type of credential is already pending"));
        }

        _activeCredentialTypes.UnionWith(types);
        var result = new TaskCompletionSource<Credential?>();
        _ = result.Task.ContinueWith(_ => _activeCredentialTypes.ExceptWith(types), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        // [[CollectFromCredentialStore]] of PasswordCredential and FederatedCredential, step 2: it runs in parallel
        if (!IsSameOriginWithAncestors(document))
        {
            _window.QueueGlobalTask(() => result.TrySetException(DomException.NotAllowed("The password and federated credentials cannot be requested in a frame that is not same origin with its ancestors")));
            return result.Task;
        }

        var origins = _window.Options.OriginsRequiringUserMediation;
        var origin = document.Origin.Serialize();
        bool requiresUserMediation;
        lock (origins)
        {
            requiresUserMediation = origins.Contains(origin);
        }

        // Step 15.4: a silent request of an origin that requires user mediation returns null
        if (options.Mediation == CredentialMediationRequirement.Silent && requiresUserMediation)
        {
            _window.QueueGlobalTask(() => result.TrySetResult(null));
            return result.Task;
        }

        var prompt = new CredentialChooserPrompt(_window, options, requiresUserMediation);
        Abort(options.Signal, result, out var cancellationToken);
        UserAgentUI.Show(
            _window,
            (backend, token) => backend.ChooseCredentialAsync(prompt, token),
            dismissed: null,
            credential =>
            {
                if (credential is not null && !prompt.Matches(credential))
                {
                    credential = null;
                }

                // The user chose a credential: the origin no longer requires user mediation
                if (credential is not null && requiresUserMediation && options.Mediation != CredentialMediationRequirement.Silent)
                {
                    lock (origins)
                    {
                        origins.Remove(origin);
                    }
                }

                result.TrySetResult(credential);
            },
            cancellationToken);
        return result.Task;
    }

    /// <summary>
    /// Rejects the promise with the abort reason of the signal when it is aborted, and returns a token canceled at the same
    /// time (the prompt is closed). The algorithm is removed from the signal once the promise is settled.
    /// </summary>
    internal static void Abort<T>(AbortSignal? signal, TaskCompletionSource<T> result, out CancellationToken cancellationToken)
    {
        if (signal is null)
        {
            cancellationToken = default;
            return;
        }

#pragma warning disable CA2000 // Disposed once the promise is settled
        var source = new CancellationTokenSource();
#pragma warning restore CA2000
        cancellationToken = source.Token;
        void OnAbort()
        {
            source.Cancel();
            result.TrySetException(WebAuthenticationClient.ToException(signal.Reason));
        }

        signal.AddAbortAlgorithm(OnAbort);
        _ = result.Task.ContinueWith(_ =>
        {
            // The abort steps of the signal are running when the promise is rejected by the signal
            if (!signal.Aborted)
            {
                signal.RemoveAbortAlgorithm(OnAbort);
            }

            source.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>
    /// Stores a credential (https://w3c.github.io/webappsec-credential-management/#algorithm-store): the user is asked whether
    /// to save a password or federated credential (<see cref="UserAgentUIBackend.StoreCredentialAsync"/>), and the task completes
    /// whatever the answer. The task fails with a <c>NotAllowedError</c> <see cref="DomException"/> when the document is in a
    /// frame that is not same origin with its ancestors, and a <c>NotSupportedError</c> for the other types of credentials
    /// (the public key credentials cannot be stored).
    /// </summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialscontainer-store"/>
    [IdlMember("store", "https://w3c.github.io/webappsec-credential-management/#dom-credentialscontainer-store")]
    public Task Store(Credential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (!_window.IsSecureContext)
            return Task.FromException(new DomException("The credentials can only be stored in a secure context", "SecurityError"));

        var document = _window.Document;
        if (!document.IsFullyActive)
            return Task.FromException(DomException.InvalidState("The document is not fully active"));

        if (document.Origin.IsOpaque)
            return Task.FromException(new DomException("The origin of the document is opaque", "SecurityError"));

        var type = credential.Type;
        if (!_activeCredentialTypes.Add(type))
            return Task.FromException(DomException.NotAllowed("A request for this type of credential is already pending"));

        var result = new TaskCompletionSource();
        if (credential is not (PasswordCredential or FederatedCredential))
        {
            // https://w3c.github.io/webauthn/#sctn-storeCredential: [[Store]] throws, the promise is rejected by a task
            _window.QueueGlobalTask(() =>
            {
                _activeCredentialTypes.Remove(type);
                result.TrySetException(DomException.NotSupported("This type of credential cannot be stored"));
            });
            return result.Task;
        }

        // [[Store]] of PasswordCredential and FederatedCredential, step 1
        if (!IsSameOriginWithAncestors(document))
        {
            _window.QueueGlobalTask(() =>
            {
                _activeCredentialTypes.Remove(type);
                result.TrySetException(DomException.NotAllowed("The credentials cannot be stored in a frame that is not same origin with its ancestors"));
            });
            return result.Task;
        }

        var prompt = new CredentialStorePrompt(_window, credential);
        UserAgentUI.Show(
            _window,
            (backend, token) => backend.StoreCredentialAsync(prompt, token),
            dismissed: false,
            _ =>
            {
                _activeCredentialTypes.Remove(type);
                result.TrySetResult();
            });
        return result.Task;
    }

    /// <summary>
    /// Creates a credential (https://w3c.github.io/webappsec-credential-management/#algorithm-create): a
    /// <see cref="PasswordCredential"/> or a <see cref="FederatedCredential"/> from their data (they are not stored), or a Web
    /// Authentication registration when <see cref="CredentialCreationOptions.PublicKey"/> is set. The task fails with an
    /// <c>InvalidStateError</c> <see cref="DomException"/> when the document is not fully active, a <c>NotSupportedError</c>
    /// when the options request no type of credential or several types, a <see cref="TypeErrorException"/> when the data is not
    /// valid, a <c>NotAllowedError</c> when the document is not allowed to use the <c>publickey-credentials-create</c> feature (nor the <c>payment</c> feature)
    /// or a request is already pending, and the errors of the ceremony.
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
        var count = (options.Password is not null ? 1 : 0) + (options.Federated is not null ? 1 : 0) + (options.PublicKey is not null ? 1 : 0) + (options.Digital is not null ? 1 : 0);
        if (count > 1)
            return Task.FromException<Credential?>(DomException.NotSupported("Only one type of credential can be created"));

        // Secure Payment Confirmation adds payment as an alternative create permissions policy of public-key
        // (https://w3c.github.io/secure-payment-confirmation/#sctn-permissions-policy)
        if (options.PublicKey is not null && !PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.PublicKeyCredentialsCreate) && !PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.Payment))
            return Task.FromException<Credential?>(DomException.NotAllowed("The document is not allowed to use the publickey-credentials-create feature (nor the payment feature)"));

        if (options.Digital is not null && !PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.DigitalCredentialsCreate))
            return Task.FromException<Credential?>(DomException.NotAllowed("The document is not allowed to use the digital-credentials-create feature"));

        if (options.Signal is { Aborted: true } signal)
            return Task.FromException<Credential?>(WebAuthenticationClient.ToException(signal.Reason));

        // The algorithm does not define the creation without type of credential (its interfaces are empty): it is rejected
        // after the signal is checked
        if (count == 0)
            return Task.FromException<Credential?>(DomException.NotSupported("The options do not request a supported type of credential"));

        if (document.Origin.IsOpaque)
            return Task.FromException<Credential?>(new DomException("The origin of the document is opaque", "SecurityError"));

        if (options.Digital is { } digital)
        {
            var requests = digital.Requests.Select(request => (request.Protocol, request.Data));
            return DigitalCredential.Request(_window, _digitalCredentials, DigitalCredentialRequestKind.Issuance, requests, options.Signal);
        }

        if (options.PublicKey is null)
            return CreateLocalCredential(options);

        if (!_activeCredentialTypes.Add(PublicKeyType))
            return Task.FromException<Credential?>(DomException.NotAllowed("A request for a public key credential is already pending"));

        return WhenSettled(WebAuthenticationClient.Create(_window, options, sameOriginWithAncestors), PublicKeyType);
    }

    /// <summary>[[Create]] of PasswordCredential and FederatedCredential: the credential is created in parallel, and the promise resolved by a task.</summary>
    private Task<Credential?> CreateLocalCredential(CredentialCreationOptions options)
    {
        var origin = _window.Document.Origin.Serialize();
        Credential credential;
        try
        {
            credential = options switch
            {
                { Password: HtmlFormElement form } => new PasswordCredential(PasswordCredential.FromForm(form, origin)),
                { Password: PasswordCredentialData data } => new PasswordCredential(data),
                { Password: not null } => throw new TypeErrorException("The password member is not a PasswordCredentialData or a form"),
                { Federated: { } data } => new FederatedCredential(new FederatedCredentialInit
                {
                    Id = data.Id,
                    Name = data.Name,
                    IconUrl = data.IconUrl,
                    Origin = origin,
                    Provider = data.Provider,
                    Protocol = data.Protocol,
                }),
                _ => throw new InvalidOperationException("No credential to create"),
            };
        }
        catch (Exception ex) when (ex is TypeErrorException or DomException)
        {
            return Task.FromException<Credential?>(ex);
        }

        var result = new TaskCompletionSource<Credential?>();
        _window.QueueGlobalTask(() => result.TrySetResult(credential));
        return result.Task;
    }

    /// <summary>
    /// Prevents the silent access to the credentials of the origin of the document
    /// (https://w3c.github.io/webappsec-credential-management/#dom-credentialscontainer-preventsilentaccess): the origin
    /// requires user mediation (<see cref="CredentialChooserPrompt.RequiresUserMediation"/>) until the user chooses a credential.
    /// The task fails with an <c>InvalidStateError</c> <see cref="DomException"/> when the document is not fully active.
    /// </summary>
    /// <seealso href="https://w3c.github.io/webappsec-credential-management/#dom-credentialscontainer-preventsilentaccess"/>
    [IdlMember("preventSilentAccess", "https://w3c.github.io/webappsec-credential-management/#dom-credentialscontainer-preventsilentaccess")]
    public Task PreventSilentAccess()
    {
        var document = _window.Document;
        if (!document.IsFullyActive)
            return Task.FromException(DomException.InvalidState("The document is not fully active"));

        if (!document.Origin.IsOpaque)
        {
            var origins = _window.Options.OriginsRequiringUserMediation;
            lock (origins)
            {
                origins.Add(document.Origin.Serialize());
            }
        }

        var result = new TaskCompletionSource();
        _window.QueueGlobalTask(result.SetResult);
        return result.Task;
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
    private Task<Credential?> WhenSettled(Task<Credential?> task, string type)
    {
        if (task.IsCompleted)
        {
            _activeCredentialTypes.Remove(type);
            return task;
        }

        var result = new TaskCompletionSource<Credential?>();
        _ = task.ContinueWith(completed =>
        {
            _activeCredentialTypes.Remove(type);
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
