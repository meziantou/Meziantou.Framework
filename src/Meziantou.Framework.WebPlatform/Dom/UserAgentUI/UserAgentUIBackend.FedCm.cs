namespace Meziantou.Framework.WebPlatform.Dom;

// The dialogs of Federated Credential Management (https://w3c-fedid.github.io/FedCM/): the account chooser, the confirmation
// of the login to an identity provider, the error of the identity provider and the notice of the auto reauthentication. The
// login and continuation pages of the identity provider are windows that the library opens itself.
public abstract partial class UserAgentUIBackend
{
    /// <summary>
    /// Asks the user to choose an account of the identity providers of <c>navigator.credentials.get({ identity })</c>
    /// (https://w3c-fedid.github.io/FedCM/#create-an-identitycredential): the accounts returned by the accounts endpoints of the
    /// providers, filtered by the hints of the request. The backend returns the chosen account
    /// (<see cref="IdentityAccountChoice.Select"/>, the user signs in or signs up to the site with it), the request to sign in
    /// to another account (<see cref="IdentityAccountChoice.UseOtherAccount"/>, when <see cref="IdentityAccountChooserPrompt.SupportsUseOtherAccount"/>
    /// is true: the login page of the provider is opened, then the accounts are fetched again and the chooser is shown again),
    /// or null when the user closes the chooser (the request fails with a <c>NetworkError</c>). The default implementation
    /// returns null.
    /// </summary>
    /// <remarks>
    /// A backend can refuse to show the chooser (e.g. during a cooldown after the user dismissed it) by returning null at once.
    /// </remarks>
    /// <param name="prompt">The accounts and the request.</param>
    /// <param name="cancellationToken">Canceled when the request is aborted.</param>
    /// <returns>The choice of the user, or null.</returns>
    public virtual ValueTask<IdentityAccountChoice?> ChooseIdentityAccountAsync(IdentityAccountChooserPrompt prompt, CancellationToken cancellationToken) => new((IdentityAccountChoice?)null);

    /// <summary>
    /// Asks the user whether to sign in to an identity provider (the confirm IDP login dialog,
    /// https://w3c-fedid.github.io/FedCM/#create-an-identitycredential): the user agent expected the user to be signed in to the
    /// provider (its login status is logged in), but its accounts endpoint returned no account matching the request. When the
    /// backend returns true (the user continues), the library opens the login page of the provider in a new window
    /// (<see cref="IdentityProviderLoginPrompt.LoginUrl"/>), waits until the page calls <c>IdentityProvider.close()</c>, then
    /// fetches the accounts again. The default implementation returns false: the request fails with a <c>NetworkError</c>.
    /// </summary>
    /// <param name="prompt">The identity provider.</param>
    /// <param name="cancellationToken">Canceled when the request is aborted.</param>
    /// <returns>Whether the user continues to the login page of the provider.</returns>
    public virtual ValueTask<bool> ConfirmIdentityProviderLoginAsync(IdentityProviderLoginPrompt prompt, CancellationToken cancellationToken) => new(false);

    /// <summary>
    /// Tells the user that the identity provider did not mint a token for the chosen account
    /// (https://w3c-fedid.github.io/FedCM/#create-an-identitycredential): the request fails with an
    /// <see cref="IdentityCredentialError"/> once the backend completes (the user acknowledged or dismissed the error). The
    /// default implementation completes at once.
    /// </summary>
    /// <param name="prompt">The error.</param>
    /// <param name="cancellationToken">Canceled when the request is aborted.</param>
    /// <returns>A task that completes when the user closes the error.</returns>
    public virtual ValueTask ShowIdentityCredentialErrorAsync(IdentityCredentialErrorPrompt prompt, CancellationToken cancellationToken) => default;

    /// <summary>
    /// Tells the user that they are signed in again to the site with a returning account, without choosing it (the auto
    /// reauthentication, https://w3c-fedid.github.io/FedCM/#create-an-identitycredential). The backend returns false when the
    /// user cancels the reauthentication (the request fails with a <c>NetworkError</c>). The default implementation returns
    /// true, as does the library without backend.
    /// </summary>
    /// <param name="prompt">The returning account.</param>
    /// <param name="cancellationToken">Canceled when the request is aborted.</param>
    /// <returns>Whether the reauthentication continues.</returns>
    public virtual ValueTask<bool> ShowIdentityAutoReauthenticationAsync(IdentityAutoReauthenticationPrompt prompt, CancellationToken cancellationToken) => new(true);
}

/// <summary>A dialog of a FedCM request (<c>navigator.credentials.get({ identity })</c>), shown by a method of <see cref="UserAgentUIBackend"/>.</summary>
public abstract class IdentityCredentialPrompt : UserAgentPrompt
{
    private protected IdentityCredentialPrompt(Window window, IdentityCredentialRequestOptionsContext context, IdentityCredentialRequestOptionsMode mode)
        : base(window)
    {
        RequestContext = context;
        Mode = mode;
    }

    /// <summary>Gets the context of the request, which the dialog uses in its title (e.g. "Sign in to ... with ...").</summary>
    public IdentityCredentialRequestOptionsContext RequestContext { get; }

    /// <summary>Gets the mode of the request: <see cref="IdentityCredentialRequestOptionsMode.Active"/> after a click of the user on a sign-in button.</summary>
    public IdentityCredentialRequestOptionsMode Mode { get; }
}

/// <summary>The account chooser of a FedCM request, given to <see cref="UserAgentUIBackend.ChooseIdentityAccountAsync"/>.</summary>
public sealed class IdentityAccountChooserPrompt : IdentityCredentialPrompt
{
    internal IdentityAccountChooserPrompt(Window window, IdentityCredentialRequestOptionsContext context, IdentityCredentialRequestOptionsMode mode, IReadOnlyList<IdentityProviderAccount> accounts, bool supportsUseOtherAccount, bool showsSiteOrigin)
        : base(window, context, mode)
    {
        Accounts = accounts;
        SupportsUseOtherAccount = supportsUseOtherAccount;
        ShowsSiteOrigin = showsSiteOrigin;
    }

    /// <summary>Gets the accounts the user can choose from, in the order of the providers of the request (<see cref="IdentityProviderAccount.ConfigUrl"/> is the provider of an account).</summary>
    public IReadOnlyList<IdentityProviderAccount> Accounts { get; }

    /// <summary>Gets whether the chooser offers to sign in to another account of the provider (<see cref="IdentityAccountChoice.UseOtherAccount"/>): a request to a single provider whose config supports it (the specification only offers it to the active requests, the browsers to every request).</summary>
    public bool SupportsUseOtherAccount { get; }

    /// <summary>
    /// Gets whether the dialog must show the site of <see cref="UserAgentPrompt.Origin"/> (the requesting frame), in addition to
    /// the site of <see cref="UserAgentPrompt.TopLevelOrigin"/>: the client metadata of the provider tells that the site is a
    /// third party to the top-level site.
    /// </summary>
    public bool ShowsSiteOrigin { get; }
}

/// <summary>The answer of the account chooser of a FedCM request (<see cref="UserAgentUIBackend.ChooseIdentityAccountAsync"/>).</summary>
public sealed class IdentityAccountChoice
{
    private IdentityAccountChoice(IdentityProviderAccount? account)
    {
        Account = account;
    }

    /// <summary>Gets the answer of a user who asks to sign in to another account of the identity provider.</summary>
    public static IdentityAccountChoice UseOtherAccount { get; } = new(account: null);

    /// <summary>Gets the chosen account, or null for <see cref="UseOtherAccount"/>.</summary>
    public IdentityProviderAccount? Account { get; }

    /// <summary>Returns the answer of a user who chooses an account.</summary>
    /// <param name="account">An account of <see cref="IdentityAccountChooserPrompt.Accounts"/>.</param>
    public static IdentityAccountChoice Select(IdentityProviderAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new IdentityAccountChoice(account);
    }
}

/// <summary>The confirmation of the login to an identity provider, given to <see cref="UserAgentUIBackend.ConfirmIdentityProviderLoginAsync"/>.</summary>
public sealed class IdentityProviderLoginPrompt : IdentityCredentialPrompt
{
    internal IdentityProviderLoginPrompt(Window window, IdentityCredentialRequestOptionsContext context, IdentityCredentialRequestOptionsMode mode, string configUrl, string loginUrl)
        : base(window, context, mode)
    {
        ConfigUrl = configUrl;
        LoginUrl = loginUrl;
    }

    /// <summary>Gets the URL of the config file of the identity provider.</summary>
    public string ConfigUrl { get; }

    /// <summary>Gets the URL of the login page of the identity provider, which the library opens when the user continues.</summary>
    public string LoginUrl { get; }
}

/// <summary>An error of the identity assertion endpoint of an identity provider, given to <see cref="UserAgentUIBackend.ShowIdentityCredentialErrorAsync"/>.</summary>
public sealed class IdentityCredentialErrorPrompt : IdentityCredentialPrompt
{
    internal IdentityCredentialErrorPrompt(Window window, IdentityCredentialRequestOptionsContext context, IdentityCredentialRequestOptionsMode mode, string configUrl, IdentityCredentialError error)
        : base(window, context, mode)
    {
        ConfigUrl = configUrl;
        Error = error;
    }

    /// <summary>Gets the URL of the config file of the identity provider.</summary>
    public string ConfigUrl { get; }

    /// <summary>Gets the error that the request is rejected with: its type (e.g. <c>unauthorized_client</c>) and the URL of a page that describes it, which the dialog can show.</summary>
    public IdentityCredentialError Error { get; }
}

/// <summary>The auto reauthentication of a returning account, given to <see cref="UserAgentUIBackend.ShowIdentityAutoReauthenticationAsync"/>.</summary>
public sealed class IdentityAutoReauthenticationPrompt : IdentityCredentialPrompt
{
    internal IdentityAutoReauthenticationPrompt(Window window, IdentityCredentialRequestOptionsContext context, IdentityCredentialRequestOptionsMode mode, IdentityProviderAccount account)
        : base(window, context, mode)
    {
        Account = account;
    }

    /// <summary>Gets the returning account that the user signs in with.</summary>
    public IdentityProviderAccount Account { get; }
}
