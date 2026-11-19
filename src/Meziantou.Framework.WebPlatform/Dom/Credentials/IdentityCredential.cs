namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The context of a FedCM request, which the dialogs use in their title (https://w3c-fedid.github.io/FedCM/#enumdef-identitycredentialrequestoptionscontext).</summary>
/// <seealso href="https://w3c-fedid.github.io/FedCM/#enumdef-identitycredentialrequestoptionscontext"/>
[IdlInterface("IdentityCredentialRequestOptionsContext", "https://w3c-fedid.github.io/FedCM/#enumdef-identitycredentialrequestoptionscontext")]
public enum IdentityCredentialRequestOptionsContext
{
    /// <summary>The user signs in (the default).</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identitycredentialrequestoptionscontext-signin"/>
    [IdlMember("signin", "https://w3c-fedid.github.io/FedCM/#dom-identitycredentialrequestoptionscontext-signin")]
    Signin,

    /// <summary>The user signs up.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identitycredentialrequestoptionscontext-signup"/>
    [IdlMember("signup", "https://w3c-fedid.github.io/FedCM/#dom-identitycredentialrequestoptionscontext-signup")]
    Signup,

    /// <summary>The user uses the site.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identitycredentialrequestoptionscontext-use"/>
    [IdlMember("use", "https://w3c-fedid.github.io/FedCM/#dom-identitycredentialrequestoptionscontext-use")]
    Use,

    /// <summary>The user continues to the site.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identitycredentialrequestoptionscontext-continue"/>
    [IdlMember("continue", "https://w3c-fedid.github.io/FedCM/#dom-identitycredentialrequestoptionscontext-continue")]
    Continue,
}

/// <summary>The mode of a FedCM request (https://w3c-fedid.github.io/FedCM/#enumdef-identitycredentialrequestoptionsmode).</summary>
/// <seealso href="https://w3c-fedid.github.io/FedCM/#enumdef-identitycredentialrequestoptionsmode"/>
[IdlInterface("IdentityCredentialRequestOptionsMode", "https://w3c-fedid.github.io/FedCM/#enumdef-identitycredentialrequestoptionsmode")]
public enum IdentityCredentialRequestOptionsMode
{
    /// <summary>The request is made after a click of the user on a sign-in button: it needs a transient activation, and it can show the login page of the identity provider.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identitycredentialrequestoptionsmode-active"/>
    [IdlMember("active", "https://w3c-fedid.github.io/FedCM/#dom-identitycredentialrequestoptionsmode-active")]
    Active,

    /// <summary>The request is made without a user action (the default).</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identitycredentialrequestoptionsmode-passive"/>
    [IdlMember("passive", "https://w3c-fedid.github.io/FedCM/#dom-identitycredentialrequestoptionsmode-passive")]
    Passive,
}

/// <summary>An identity provider and the client identifier of the site at the provider (https://w3c-fedid.github.io/FedCM/#dictdef-identityproviderconfig).</summary>
/// <seealso href="https://w3c-fedid.github.io/FedCM/#dictdef-identityproviderconfig"/>
[IdlInterface("IdentityProviderConfig", "https://w3c-fedid.github.io/FedCM/#dictdef-identityproviderconfig")]
public class IdentityProviderConfig
{
    /// <summary>Gets the URL of the config file of the identity provider.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identityproviderconfig-configurl"/>
    [IdlMember("configURL", "https://w3c-fedid.github.io/FedCM/#dom-identityproviderconfig-configurl")]
    public required string ConfigUrl { get; init; }

    /// <summary>Gets the identifier of the site (the relying party) at the identity provider.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identityproviderconfig-clientid"/>
    [IdlMember("clientId", "https://w3c-fedid.github.io/FedCM/#dom-identityproviderconfig-clientid")]
    public required string ClientId { get; init; }
}

/// <summary>An identity provider requested by <c>navigator.credentials.get({ identity })</c> (https://w3c-fedid.github.io/FedCM/#dictdef-identityproviderrequestoptions).</summary>
/// <seealso href="https://w3c-fedid.github.io/FedCM/#dictdef-identityproviderrequestoptions"/>
[IdlInterface("IdentityProviderRequestOptions", "https://w3c-fedid.github.io/FedCM/#dictdef-identityproviderrequestoptions")]
public sealed class IdentityProviderRequestOptions : IdentityProviderConfig
{
    /// <summary>Gets the login hint: only the accounts whose <c>login_hints</c> contain it are shown, or null.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identityproviderrequestoptions-loginhint"/>
    [IdlMember("loginHint", "https://w3c-fedid.github.io/FedCM/#dom-identityproviderrequestoptions-loginhint")]
    public string? LoginHint { get; init; }

    /// <summary>Gets the domain hint: only the accounts whose <c>domain_hints</c> contain it (any domain hint for <c>any</c>) are shown, or null.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identityproviderrequestoptions-domainhint"/>
    [IdlMember("domainHint", "https://w3c-fedid.github.io/FedCM/#dom-identityproviderrequestoptions-domainhint")]
    public string? DomainHint { get; init; }

    /// <summary>Gets the fields of the account that the site requests (e.g. <c>name</c>, <c>email</c>, <c>picture</c>), or null for the default fields.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identityproviderrequestoptions-fields"/>
    [IdlMember("fields", "https://w3c-fedid.github.io/FedCM/#dom-identityproviderrequestoptions-fields")]
    public IReadOnlyList<string>? Fields { get; init; }

    /// <summary>Gets the custom parameters sent to the identity assertion endpoint as JSON (a value of the script, serialized with <c>JSON.stringify()</c>), or null.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identityproviderrequestoptions-params"/>
    [IdlMember("params", "https://w3c-fedid.github.io/FedCM/#dom-identityproviderrequestoptions-params")]
    public object? Params { get; init; }

    /// <summary>
    /// Gets the nonce sent to the identity assertion endpoint, or null. The member is deprecated (the specification passes it
    /// in <see cref="Params"/>), but the browsers and the web-platform-tests still use it.
    /// </summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identityproviderrequestoptions-nonce"/>
    [IdlMember("nonce", "https://w3c-fedid.github.io/FedCM/#dom-identityproviderrequestoptions-nonce")]
    public string? Nonce { get; init; }
}

/// <summary>The FedCM member of <c>navigator.credentials.get()</c> (https://w3c-fedid.github.io/FedCM/#dictdef-identitycredentialrequestoptions).</summary>
/// <seealso href="https://w3c-fedid.github.io/FedCM/#dictdef-identitycredentialrequestoptions"/>
[IdlInterface("IdentityCredentialRequestOptions", "https://w3c-fedid.github.io/FedCM/#dictdef-identitycredentialrequestoptions")]
public sealed class IdentityCredentialRequestOptions
{
    /// <summary>Gets the identity providers that the site supports.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identitycredentialrequestoptions-providers"/>
    [IdlMember("providers", "https://w3c-fedid.github.io/FedCM/#dom-identitycredentialrequestoptions-providers")]
    public required IReadOnlyList<IdentityProviderRequestOptions> Providers { get; init; }

    /// <summary>Gets the context of the request, which the dialogs use in their title.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identitycredentialrequestoptions-context"/>
    [IdlMember("context", "https://w3c-fedid.github.io/FedCM/#dom-identitycredentialrequestoptions-context")]
    public IdentityCredentialRequestOptionsContext Context { get; init; } = IdentityCredentialRequestOptionsContext.Signin;

    /// <summary>Gets the mode of the request.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identitycredentialrequestoptions-mode"/>
    [IdlMember("mode", "https://w3c-fedid.github.io/FedCM/#dom-identitycredentialrequestoptions-mode")]
    public IdentityCredentialRequestOptionsMode Mode { get; init; } = IdentityCredentialRequestOptionsMode.Passive;
}

/// <summary>The options of <see cref="IdentityCredential.Disconnect"/> (https://w3c-fedid.github.io/FedCM/#dictdef-identitycredentialdisconnectoptions).</summary>
/// <seealso href="https://w3c-fedid.github.io/FedCM/#dictdef-identitycredentialdisconnectoptions"/>
[IdlInterface("IdentityCredentialDisconnectOptions", "https://w3c-fedid.github.io/FedCM/#dictdef-identitycredentialdisconnectoptions")]
public sealed class IdentityCredentialDisconnectOptions : IdentityProviderConfig
{
    /// <summary>Gets the hint of the account to disconnect, sent to the disconnect endpoint of the identity provider.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identitycredentialdisconnectoptions-accounthint"/>
    [IdlMember("accountHint", "https://w3c-fedid.github.io/FedCM/#dom-identitycredentialdisconnectoptions-accounthint")]
    public required string AccountHint { get; init; }
}

/// <summary>
/// A credential of Federated Credential Management (https://w3c-fedid.github.io/FedCM/#browser-api-identity-credential-interface):
/// the token that an identity provider minted for the site, returned by <c>navigator.credentials.get({ identity })</c> once
/// the user chose an account (see <see cref="UserAgentUIBackend.ChooseIdentityAccountAsync"/>).
/// </summary>
/// <seealso href="https://w3c-fedid.github.io/FedCM/#identitycredential"/>
[IdlInterface("IdentityCredential", "https://w3c-fedid.github.io/FedCM/#identitycredential")]
public sealed class IdentityCredential : Credential
{
    internal IdentityCredential(object? token, bool isAutoSelected, string configUrl)
    {
        Token = token;
        IsAutoSelected = isAutoSelected;
        ConfigUrl = configUrl;
    }

    /// <summary>Gets the identifier of the credential: the empty string.</summary>
    public override string Id => "";

    /// <summary>Gets the type of the credential: <c>identity</c>.</summary>
    public override string Type => "identity";

    /// <summary>
    /// Gets the token minted by the identity provider: the value of the <c>token</c> member of the response of its identity
    /// assertion endpoint, or the token given to <c>IdentityProvider.resolve()</c> (a value of the script, or a JSON node
    /// without script engine).
    /// </summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identitycredential-token"/>
    [IdlMember("token", "https://w3c-fedid.github.io/FedCM/#dom-identitycredential-token")]
    public object? Token { get; }

    /// <summary>Gets whether the account was selected without asking the user (the auto reauthentication of a returning account).</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identitycredential-isautoselected"/>
    [IdlMember("isAutoSelected", "https://w3c-fedid.github.io/FedCM/#dom-identitycredential-isautoselected")]
    public bool IsAutoSelected { get; }

    /// <summary>Gets the URL of the config file of the identity provider that issued the credential.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identitycredential-configurl"/>
    [IdlMember("configURL", "https://w3c-fedid.github.io/FedCM/#dom-identitycredential-configurl")]
    public string ConfigUrl { get; }

    /// <summary>
    /// Disconnects an account of an identity provider from the site (https://w3c-fedid.github.io/FedCM/#dom-identitycredential-disconnect):
    /// the disconnect endpoint of the provider is called, and the account is removed from the connected accounts. The task
    /// fails with a <c>NotAllowedError</c> <see cref="DomException"/> when the document is not allowed to use the
    /// <c>identity-credentials-get</c> feature, an <c>InvalidStateError</c> when the config URL is not valid, and a
    /// <c>NetworkError</c> when no account of the provider is connected to the site, a disconnection is pending, or the
    /// provider fails.
    /// </summary>
    /// <param name="options">The identity provider and the hint of the account.</param>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identitycredential-disconnect"/>
    [IdlMember("disconnect", "https://w3c-fedid.github.io/FedCM/#dom-identitycredential-disconnect")]
    public static Task Disconnect(IdentityCredentialDisconnectOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Document.CurrentGlobalWindow is { } window ? IdentityCredentialRequest.Disconnect(window, options) : Task.FromException(DomException.InvalidState("There is no current window"));
    }
}

/// <summary>The details of an <see cref="IdentityCredentialError"/> (https://w3c-fedid.github.io/FedCM/#dictdef-identitycredentialerrorinit).</summary>
/// <seealso href="https://w3c-fedid.github.io/FedCM/#dictdef-identitycredentialerrorinit"/>
[IdlInterface("IdentityCredentialErrorInit", "https://w3c-fedid.github.io/FedCM/#dictdef-identitycredentialerrorinit")]
public sealed class IdentityCredentialErrorInit
{
    /// <summary>Gets the type of the error (e.g. <c>invalid_request</c>, <c>unauthorized_client</c>), or null.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identitycredentialerrorinit-error"/>
    [IdlMember("error", "https://w3c-fedid.github.io/FedCM/#dom-identitycredentialerrorinit-error")]
    public string? Error { get; init; }

    /// <summary>Gets the URL of a page that describes the error, or null.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identitycredentialerrorinit-url"/>
    [IdlMember("url", "https://w3c-fedid.github.io/FedCM/#dom-identitycredentialerrorinit-url")]
    public string? Url { get; init; }
}

/// <summary>
/// The exception of a FedCM request whose identity provider did not mint a token after the user chose an account
/// (https://w3c-fedid.github.io/FedCM/#browser-api-identity-credential-error-interface): a <see cref="DomException"/> named
/// <c>IdentityCredentialError</c>, with the error and the URL that the identity assertion endpoint returned.
/// </summary>
/// <seealso href="https://w3c-fedid.github.io/FedCM/#identitycredentialerror"/>
[IdlInterface("IdentityCredentialError", "https://w3c-fedid.github.io/FedCM/#identitycredentialerror")]
public sealed class IdentityCredentialError : DomException
{
    /// <summary>Creates an error without details.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identitycredentialerror-identitycredentialerror"/>
    [IdlMember("constructor", "https://w3c-fedid.github.io/FedCM/#dom-identitycredentialerror-identitycredentialerror")]
    public IdentityCredentialError()
        : this("", options: null)
    {
    }

    /// <summary>Creates an error with a message.</summary>
    /// <param name="message">The message of the error.</param>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identitycredentialerror-identitycredentialerror"/>
    [IdlMember("constructor", "https://w3c-fedid.github.io/FedCM/#dom-identitycredentialerror-identitycredentialerror")]
    public IdentityCredentialError(string? message)
        : this(message ?? "", options: null)
    {
    }

    /// <summary>Creates an error with a message and its details (https://w3c-fedid.github.io/FedCM/#dom-identitycredentialerror-identitycredentialerror).</summary>
    /// <param name="message">The message of the error.</param>
    /// <param name="options">The type of the error and the URL that describes it.</param>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identitycredentialerror-identitycredentialerror"/>
    [IdlMember("constructor", "https://w3c-fedid.github.io/FedCM/#dom-identitycredentialerror-identitycredentialerror")]
    public IdentityCredentialError(string message, IdentityCredentialErrorInit? options)
        : base(message ?? "", "IdentityCredentialError")
    {
        Error = options?.Error ?? "";
        Url = options?.Url ?? "";
    }

    /// <summary>Creates an error with a message and an inner exception.</summary>
    /// <param name="message">The message of the error.</param>
    /// <param name="innerException">The exception that caused the error.</param>
    [NotInIdl]
    public IdentityCredentialError(string? message, Exception? innerException)
        : base(message, innerException)
    {
        Name = "IdentityCredentialError";
        Error = "";
        Url = "";
    }

    /// <summary>Gets the type of the error (e.g. <c>unauthorized_client</c>), or the empty string.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identitycredentialerror-error"/>
    [IdlMember("error", "https://w3c-fedid.github.io/FedCM/#dom-identitycredentialerror-error")]
    public string Error { get; }

    /// <summary>Gets the URL of a page that describes the error, or the empty string.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identitycredentialerror-url"/>
    [IdlMember("url", "https://w3c-fedid.github.io/FedCM/#dom-identitycredentialerror-url")]
    public string Url { get; }
}

/// <summary>The information of an account of an identity provider, returned by <see cref="IdentityProvider.GetUserInfo"/> (https://w3c-fedid.github.io/FedCM/#dictdef-identityuserinfo).</summary>
/// <seealso href="https://w3c-fedid.github.io/FedCM/#dictdef-identityuserinfo"/>
[IdlInterface("IdentityUserInfo", "https://w3c-fedid.github.io/FedCM/#dictdef-identityuserinfo")]
public sealed class IdentityUserInfo
{
    /// <summary>Gets the email address of the account, or null.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identityuserinfo-email"/>
    [IdlMember("email", "https://w3c-fedid.github.io/FedCM/#dom-identityuserinfo-email")]
    public string? Email { get; init; }

    /// <summary>Gets the full name of the user, or null.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identityuserinfo-name"/>
    [IdlMember("name", "https://w3c-fedid.github.io/FedCM/#dom-identityuserinfo-name")]
    public string? Name { get; init; }

    /// <summary>Gets the given name of the user, or null.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identityuserinfo-givenname"/>
    [IdlMember("givenName", "https://w3c-fedid.github.io/FedCM/#dom-identityuserinfo-givenname")]
    public string? GivenName { get; init; }

    /// <summary>Gets the URL of the picture of the account, or null.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identityuserinfo-picture"/>
    [IdlMember("picture", "https://w3c-fedid.github.io/FedCM/#dom-identityuserinfo-picture")]
    public string? Picture { get; init; }
}

/// <summary>The options of <see cref="IdentityProvider.Resolve"/> (https://w3c-fedid.github.io/FedCM/#dictdef-identityresolveoptions).</summary>
/// <seealso href="https://w3c-fedid.github.io/FedCM/#dictdef-identityresolveoptions"/>
[IdlInterface("IdentityResolveOptions", "https://w3c-fedid.github.io/FedCM/#dictdef-identityresolveoptions")]
public sealed class IdentityResolveOptions
{
    /// <summary>Gets the identifier of the account that the token is for, when it is not the account chosen by the user, or null.</summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identityresolveoptions-accountid"/>
    [IdlMember("accountId", "https://w3c-fedid.github.io/FedCM/#dom-identityresolveoptions-accountid")]
    public string? AccountId { get; init; }
}

/// <summary>
/// The API of the pages of an identity provider (https://w3c-fedid.github.io/FedCM/#browser-api-identity-provider-interface):
/// the login page and the continuation page that the user agent opens in a popup window during a FedCM request tell when
/// they are done (<see cref="Close"/>, <see cref="Resolve"/>), and an iframe of the provider can read the accounts connected
/// to its embedder (<see cref="GetUserInfo"/>).
/// </summary>
/// <seealso href="https://w3c-fedid.github.io/FedCM/#identityprovider"/>
[IdlInterface("IdentityProvider", "https://w3c-fedid.github.io/FedCM/#identityprovider")]
public sealed class IdentityProvider
{
    private IdentityProvider()
    {
    }

    /// <summary>
    /// Tells that the login flow of the identity provider is done (https://w3c-fedid.github.io/FedCM/#dom-identityprovider-close):
    /// the login page (or the continuation page) opened by a FedCM request is closed. It does nothing in another window.
    /// </summary>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identityprovider-close"/>
    [IdlMember("close", "https://w3c-fedid.github.io/FedCM/#dom-identityprovider-close")]
    public static void Close()
    {
        if (Document.CurrentGlobalWindow is { } window)
        {
            IdentityProviderDialogs.Close(window);
        }
    }

    /// <summary>
    /// Returns a token to the site from the continuation page opened by a FedCM request (https://w3c-fedid.github.io/FedCM/#dom-identityprovider-resolve):
    /// the page is closed and the request resolves with an <see cref="IdentityCredential"/> whose token is <paramref name="token"/>.
    /// The task fails with a <c>NotAllowedError</c> <see cref="DomException"/> in another window.
    /// </summary>
    /// <param name="token">The token (a value of the script, serialized with <c>JSON.stringify()</c>).</param>
    /// <param name="options">The account that the token is for, when it is not the account chosen by the user.</param>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identityprovider-resolve"/>
    [IdlMember("resolve", "https://w3c-fedid.github.io/FedCM/#dom-identityprovider-resolve")]
    public static Task Resolve(object? token, IdentityResolveOptions? options = null)
        => Document.CurrentGlobalWindow is { } window ? IdentityProviderDialogs.Resolve(window, token, options?.AccountId) : Task.FromException(DomException.NotAllowed("There is no current window"));

    /// <summary>
    /// Returns the accounts of the identity provider that are connected to the embedder of the iframe of the provider that
    /// calls it (https://w3c-fedid.github.io/FedCM/#dom-identityprovider-getuserinfo). The task fails with a
    /// <c>NotAllowedError</c> <see cref="DomException"/> when the document is not allowed to use the
    /// <c>identity-credentials-get</c> feature, an <c>InvalidStateError</c> when the config URL is not valid or is not same
    /// origin with the document, and a <c>NetworkError</c> when no account is connected, the document is not in an iframe,
    /// or the provider fails.
    /// </summary>
    /// <param name="config">The identity provider.</param>
    /// <seealso href="https://w3c-fedid.github.io/FedCM/#dom-identityprovider-getuserinfo"/>
    [IdlMember("getUserInfo", "https://w3c-fedid.github.io/FedCM/#dom-identityprovider-getuserinfo")]
    public static Task<IReadOnlyList<IdentityUserInfo>> GetUserInfo(IdentityProviderConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return Document.CurrentGlobalWindow is { } window ? IdentityCredentialRequest.GetUserInfo(window, config) : Task.FromException<IReadOnlyList<IdentityUserInfo>>(DomException.InvalidState("There is no current window"));
    }
}
