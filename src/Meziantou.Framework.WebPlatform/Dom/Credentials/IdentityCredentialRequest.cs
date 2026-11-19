using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A FedCM request (https://w3c-fedid.github.io/FedCM/#browser-api-rp-sign-in): <c>navigator.credentials.get({ identity })</c>
/// fetches the config files and the accounts of the identity providers, asks the user to choose an account with the dialogs
/// of the <see cref="UserAgentUIBackend"/> (or reauthenticates a returning account), and fetches a token for the account from
/// the identity assertion endpoint of its provider. The static methods implement <c>IdentityCredential.disconnect()</c> and
/// <c>IdentityProvider.getUserInfo()</c>.
/// </summary>
#pragma warning disable CA1001 // The cancellation source is canceled when the promise is settled, and is not disposed: the prompts and the fetches may still use its token
internal sealed class IdentityCredentialRequest
#pragma warning restore CA1001
{
    /// <summary>The maximum number of identity providers of a request, as in the browsers.</summary>
    private const int MaxProviders = 10;

    /// <summary>The pending request of each top-level browsing context (one request at a time in a page and its frames).</summary>
    private static readonly ConditionalWeakTable<BrowsingContext, IdentityCredentialRequest> PendingRequests = [];

    /// <summary>The windows that have a pending <c>IdentityCredential.disconnect()</c>.</summary>
    private static readonly ConditionalWeakTable<Window, object> PendingDisconnects = [];

    private readonly IdentityProviderFetcher _fetcher;
    private readonly List<Provider> _providers;
    private readonly CredentialMediationRequirement _mediation;
    private readonly IdentityCredentialRequestOptionsMode _mode;
    private readonly IdentityCredentialRequestOptionsContext _context;
    private readonly bool _requiresUserMediation;
    private readonly BrowsingContext _top;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource<Credential?> _result = new();

    private IdentityCredentialRequest(IdentityProviderFetcher fetcher, List<Provider> providers, CredentialRequestOptions options, bool requiresUserMediation, BrowsingContext top)
    {
        _fetcher = fetcher;
        _providers = providers;
        _mediation = options.Mediation;
        _mode = options.Identity!.Mode;
        _context = options.Identity.Context;
        _requiresUserMediation = requiresUserMediation;
        _top = top;
    }

    private Window Window => _fetcher.Window;

    /// <summary>
    /// https://w3c-fedid.github.io/FedCM/#dom-identitycredential-discoverfromexternalsource-slot: the request of
    /// <c>navigator.credentials.get({ identity })</c>. The checks that the browsers make before the request starts reject the
    /// promise at once: a <c>NotAllowedError</c> when the document is not allowed to use the <c>identity-credentials-get</c>
    /// feature or another request is pending, a <see cref="TypeErrorException"/> for an empty or too long list of providers,
    /// an <c>InvalidStateError</c> for an invalid config URL or an empty client identifier, a <c>NotSupportedError</c> for a
    /// silent active request, and a <c>NetworkError</c> for an opaque origin, an active request without transient activation
    /// or a silent request of a site that requires user mediation.
    /// </summary>
    public static Task<Credential?> Get(Window window, CredentialRequestOptions options)
    {
        var document = window.Document;
        var identity = options.Identity!;
        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.IdentityCredentialsGet))
            return Task.FromException<Credential?>(DomException.NotAllowed("The document is not allowed to use the identity-credentials-get feature"));

        if (options.Mediation == CredentialMediationRequirement.Conditional)
            return Task.FromException<Credential?>(new TypeErrorException("IdentityCredential does not support the conditional mediation"));

        if (identity.Providers.Count == 0)
            return Task.FromException<Credential?>(new TypeErrorException("The list of identity providers is empty"));

        if (identity.Providers.Count > MaxProviders)
            return Task.FromException<Credential?>(new TypeErrorException("Too many identity providers are requested"));

        var providers = new List<Provider>(identity.Providers.Count);
        foreach (var provider in identity.Providers)
        {
            if (UrlParser.Parse(provider.ConfigUrl, baseUrl: null) is not { } configUrl)
                return Task.FromException<Credential?>(DomException.InvalidState("The config URL is not a valid URL"));

            if (provider.ClientId.Length == 0)
                return Task.FromException<Credential?>(DomException.InvalidState("The client identifier is empty"));

            providers.Add(new Provider(provider, configUrl));
        }

        if (document.Origin.IsOpaque)
            return Task.FromException<Credential?>(NetworkError("The origin of the document is opaque"));

        if (identity.Mode == IdentityCredentialRequestOptionsMode.Active)
        {
            if (options.Mediation == CredentialMediationRequirement.Silent)
                return Task.FromException<Credential?>(DomException.NotSupported("An active request cannot be silent"));

            if (providers.Count != 1)
                return Task.FromException<Credential?>(NetworkError("An active request has a single identity provider"));

            if (!window.HasTransientActivation)
                return Task.FromException<Credential?>(NetworkError("An active request needs a transient activation"));
        }

        // The custom parameters are serialized at once: the exceptions of their getters are thrown to the caller
        foreach (var provider in providers)
        {
            if (provider.Options.Params is { } parameters)
            {
                provider.ParamsJson = ModelContext.SerializeJson(window, parameters);
            }
        }

        if (window.BrowsingContext is not { } browsingContext)
            return Task.FromException<Credential?>(DomException.InvalidState("The document is not fully active"));

        // A pending passive request of the page is replaced by an active request; another request is rejected
        var top = browsingContext.Top;
        if (PendingRequests.TryGetValue(top, out var pending))
        {
            if (identity.Mode != IdentityCredentialRequestOptionsMode.Active || pending._mode != IdentityCredentialRequestOptionsMode.Passive)
                return Task.FromException<Credential?>(DomException.NotAllowed("A FedCM request is already pending"));

            pending.Fail(NetworkError("The request was replaced by an active request"));
        }

        var fetcher = new IdentityProviderFetcher(window);
        var origins = window.Options.OriginsRequiringUserMediation;
        bool requiresUserMediation;
        lock (origins)
        {
            requiresUserMediation = origins.Contains(fetcher.RpOrigin);
        }

        // A silent request of a site that requires user mediation, or without a connected account, cannot succeed
        if (options.Mediation == CredentialMediationRequirement.Silent
            && (requiresUserMediation || !providers.Exists(provider => fetcher.State.HasConnection(fetcher.EmbedderOrigin, fetcher.RpOrigin, provider.Origin, accountId: null))))
        {
            return Task.FromException<Credential?>(NetworkError("The silent mediation is not allowed"));
        }

        var request = new IdentityCredentialRequest(fetcher, providers, options, requiresUserMediation, top);
        PendingRequests.AddOrUpdate(top, request);
        CredentialsContainer.Abort(options.Signal, request._result, out var abortToken);
        if (abortToken.CanBeCanceled)
        {
            abortToken.Register(request._cancellation.Cancel);
        }

        // The request is aborted when the document is no longer fully active (destroyed, or in the back/forward cache)
        window.Document.AddPromptCloser(request.OnDocumentClosed);
        _ = request._result.Task.ContinueWith(_ => request.OnSettled(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _ = request.RunAsync();
        return request._result.Task;
    }

    /// <summary>Rejects the request (e.g. replaced by an active request): its dialogs are closed.</summary>
    private void Fail(Exception exception)
    {
        _result.TrySetException(exception);
        _cancellation.Cancel();
    }

    private void OnDocumentClosed() => Fail(new DomException("The document is no longer fully active", "AbortError"));

    private void OnSettled()
    {
        Window.Document.RemovePromptCloser(OnDocumentClosed);
        if (PendingRequests.TryGetValue(_top, out var pending) && ReferenceEquals(pending, this))
        {
            PendingRequests.Remove(_top);
        }

        // The prompts and the windows that are still open are closed
        _cancellation.Cancel();
    }

    private async Task RunAsync()
    {
        try
        {
            var credential = await CreateCredentialAsync().ConfigureAwait(true);
            _result.TrySetResult(credential);
        }
        catch (OperationCanceledException)
        {
            _result.TrySetException(NetworkError("The request was canceled"));
        }
#pragma warning disable CA1031 // The exceptions (e.g. of the script engine) reject the promise
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _result.TrySetException(ex);
        }
    }

    /// <summary>https://w3c-fedid.github.io/FedCM/#create-an-identitycredential</summary>
    private async Task<IdentityCredential> CreateCredentialAsync()
    {
        var cancellationToken = _cancellation.Token;
        var origins = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in _providers)
        {
            if (!origins.Add(provider.Origin))
                throw NetworkError("Two identity providers have the same origin");

            if (_mediation == CredentialMediationRequirement.Silent && !_fetcher.State.HasConnection(_fetcher.EmbedderOrigin, _fetcher.RpOrigin, provider.Origin, accountId: null))
                continue;

            var loginStatus = _fetcher.State.GetLoginStatus(provider.Origin);
            if (loginStatus == false)
            {
                // https://w3c-fedid.github.io/FedCM/#handle-a-logged-out-idp: an active request shows the login page of the provider
                if (_mode != IdentityCredentialRequestOptionsMode.Active)
                {
                    provider.Status = ProviderStatus.LoggedOut;
                    continue;
                }

                if (await _fetcher.FetchConfigAsync(provider.ConfigUrl, cancellationToken).ConfigureAwait(true) is not { } loginConfig || !await ShowLoginDialogAsync(provider, loginConfig).ConfigureAwait(true))
                    throw NetworkError("The user did not sign in to the identity provider");

                loginStatus = true;
            }

            if (_requiresUserMediation && _mediation == CredentialMediationRequirement.Silent)
                continue;

            provider.Config = await _fetcher.FetchConfigAsync(provider.ConfigUrl, cancellationToken).ConfigureAwait(true);
            if (provider.Config is null)
                continue;

            var accounts = await _fetcher.FetchAccountsAsync(provider.Config, provider.Options, cancellationToken).ConfigureAwait(true);
            if (accounts.Count > 0)
            {
                provider.Accounts = accounts;
                provider.Status = ProviderStatus.HasAccounts;
            }
            else
            {
                provider.Status = loginStatus == true && _mediation != CredentialMediationRequirement.Silent ? ProviderStatus.Mismatch : ProviderStatus.LoggedOut;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!_providers.Exists(provider => provider.Status != ProviderStatus.None))
            throw NetworkError("No identity provider can be used");

        // The auto reauthentication of the only returning account
        var hasMismatch = _providers.Exists(provider => provider.Status == ProviderStatus.Mismatch);
        var registered = new List<(IdentityProviderAccount Account, Provider Provider)>();
        foreach (var provider in _providers)
        {
            if (provider.Status != ProviderStatus.HasAccounts)
                continue;

            foreach (var account in provider.Accounts)
            {
                if (_fetcher.IsEligibleForAutoReauthentication(account, provider.Options, provider.Origin))
                {
                    registered.Add((account, provider));
                }
            }
        }

        if (_mediation != CredentialMediationRequirement.Required && !_requiresUserMediation && registered.Count == 1 && !hasMismatch)
        {
            var (account, provider) = registered[0];
            PrepareAccounts(provider);
            var prompt = new IdentityAutoReauthenticationPrompt(Window, _context, _mode, account);
            if (!await PromptAsync((backend, token) => backend.ShowIdentityAutoReauthenticationAsync(prompt, token), dismissed: true).ConfigureAwait(true))
                throw NetworkError("The user canceled the auto reauthentication");

            return await FetchIdentityAssertionAsync(account, provider, isAutoSelected: true).ConfigureAwait(true);
        }

        if (_mediation == CredentialMediationRequirement.Silent)
            throw NetworkError("The silent mediation needs a single returning account");

        var (selectedAccount, selectedProvider) = await ChooseAccountAsync().ConfigureAwait(true);
        return await FetchIdentityAssertionAsync(selectedAccount, selectedProvider, isAutoSelected: false).ConfigureAwait(true);
    }

    /// <summary>
    /// The dialogs of the request (https://w3c-fedid.github.io/FedCM/#create-an-identitycredential): the confirm IDP login dialog
    /// of a provider that returned no account while its login status is logged-in, then the account chooser.
    /// </summary>
    private async Task<(IdentityProviderAccount Account, Provider Provider)> ChooseAccountAsync()
    {
        var cancellationToken = _cancellation.Token;
        if (!_providers.Exists(provider => provider.Status == ProviderStatus.HasAccounts))
        {
            // The providers whose user is logged out show nothing
            if (_providers.Find(provider => provider.Status == ProviderStatus.Mismatch) is not { } mismatch)
                throw NetworkError("The user is not signed in to the identity providers");

            await SignInAsync(mismatch, confirm: true).ConfigureAwait(true);
        }

        // The client metadata of the providers: the links of the accounts that sign up, and whether the site is a third party
        var showsSiteOrigin = false;
        foreach (var provider in _providers)
        {
            if (provider.Status != ProviderStatus.HasAccounts || provider.Config is not { } config || config.Config.ClientMetadataEndpoint is null)
                continue;

            if (provider.Options.Fields is { Count: 0 } && OriginRecord.IsSameSite(_fetcher.Document.Origin, _top.Document.Origin))
                continue;

            provider.ClientMetadata = await _fetcher.FetchClientMetadataAsync(config, provider.Options, cancellationToken).ConfigureAwait(true);
            showsSiteOrigin |= provider.ClientMetadata?.ClientIsThirdPartyToTopFrameOrigin == true;
        }

        // The specification offers to use another account to the active requests; as in the browsers, the passive requests
        // to a single provider offer it too
        var singleProvider = _providers.Count(provider => provider.Status == ProviderStatus.HasAccounts) == 1 ? _providers.Find(provider => provider.Status == ProviderStatus.HasAccounts) : null;
        var supportsUseOtherAccount = singleProvider?.Config?.Config.SupportsUseOtherAccount == true;
        while (true)
        {
            var accounts = new List<IdentityProviderAccount>();
            foreach (var provider in _providers)
            {
                if (provider.Status == ProviderStatus.HasAccounts)
                {
                    PrepareAccounts(provider);
                    accounts.AddRange(provider.Accounts);
                }
            }

            var prompt = new IdentityAccountChooserPrompt(Window, _context, _mode, accounts, supportsUseOtherAccount, showsSiteOrigin);
            var choice = await PromptAsync((backend, token) => backend.ChooseIdentityAccountAsync(prompt, token), dismissed: null).ConfigureAwait(true);
            if (choice?.Account is { } account && _providers.Find(provider => provider.Status == ProviderStatus.HasAccounts && provider.Accounts.Contains(account)) is { } accountProvider)
                return (account, accountProvider);

            if (!ReferenceEquals(choice, IdentityAccountChoice.UseOtherAccount) || !supportsUseOtherAccount || singleProvider is null)
                throw NetworkError("The user closed the account chooser");

            // The user signs in to another account: the accounts that were not shown come first
            var previous = singleProvider.Accounts.ConvertAll(previousAccount => previousAccount.Id);
            await SignInAsync(singleProvider, confirm: false).ConfigureAwait(true);
            singleProvider.Accounts = [.. singleProvider.Accounts.Where(newAccount => !previous.Contains(newAccount.Id, StringComparer.Ordinal)), .. singleProvider.Accounts.Where(newAccount => previous.Contains(newAccount.Id, StringComparer.Ordinal))];
        }
    }

    /// <summary>
    /// Asks the user to sign in to a provider (after the confirm IDP login dialog when <paramref name="confirm"/> is true): the
    /// login page is shown until the provider returns accounts. Throws a <c>NetworkError</c> when the user cancels or the
    /// login fails.
    /// </summary>
    private async Task SignInAsync(Provider provider, bool confirm)
    {
        var config = provider.Config!;
        while (true)
        {
            if (confirm)
            {
                var prompt = new IdentityProviderLoginPrompt(Window, _context, _mode, provider.ConfigUrl.Serialize(), config.LoginUrl.Serialize());
                if (!await PromptAsync((backend, token) => backend.ConfirmIdentityProviderLoginAsync(prompt, token), dismissed: false).ConfigureAwait(true))
                    throw NetworkError("The user did not sign in to the identity provider");
            }

            if (!await ShowLoginDialogAsync(provider, config).ConfigureAwait(true))
                throw NetworkError("The user did not sign in to the identity provider");

            var accounts = await _fetcher.FetchAccountsAsync(config, provider.Options, _cancellation.Token).ConfigureAwait(true);
            if (accounts.Count > 0)
            {
                provider.Accounts = accounts;
                provider.Status = ProviderStatus.HasAccounts;
                return;
            }

            confirm = true;
        }
    }

    /// <summary>https://w3c-fedid.github.io/FedCM/#show-an-idp-login-dialog: true when the page of the provider closed the window and the user is logged in.</summary>
    private async Task<bool> ShowLoginDialogAsync(Provider provider, IdentityProviderConfigFile config)
    {
        var loginUrl = UrlParser.Parse(config.LoginUrl.Serialize(), baseUrl: null)!;
        var query = new List<KeyValuePair<string, string>>();
        if (provider.Options.LoginHint is { Length: > 0 } loginHint)
        {
            query.Add(new("login_hint", loginHint));
        }

        if (provider.Options.DomainHint is { Length: > 0 } domainHint)
        {
            query.Add(new("domain_hint", domainHint));
        }

        if (query.Count > 0)
        {
            var parameters = FormUrlEncoded.Serialize(query);
            loginUrl.Query = loginUrl.Query is { Length: > 0 } existing ? existing + "&" + parameters : parameters;
        }

        var result = await IdentityProviderDialogs.ShowAsync(Window, loginUrl, isContinuation: false, _cancellation.Token).ConfigureAwait(true);
        _cancellation.Token.ThrowIfCancellationRequested();
        return result is not null && _fetcher.State.GetLoginStatus(loginUrl.SerializeOrigin()) == true;
    }

    /// <summary>The connection status and the links of the client metadata of the accounts of a provider.</summary>
    private void PrepareAccounts(Provider provider)
    {
        foreach (var account in provider.Accounts)
        {
            account.IsConnected = _fetcher.IsConnected(account, provider.Options, provider.Origin);
            account.PrivacyPolicyUrl = account.IsConnected ? null : provider.ClientMetadata?.PrivacyPolicyUrl;
            account.TermsOfServiceUrl = account.IsConnected ? null : provider.ClientMetadata?.TermsOfServiceUrl;
        }
    }

    /// <summary>https://w3c-fedid.github.io/FedCM/#fetch-an-identity-assertion</summary>
    private async Task<IdentityCredential> FetchIdentityAssertionAsync(IdentityProviderAccount account, Provider provider, bool isAutoSelected)
    {
        var cancellationToken = _cancellation.Token;
        var config = provider.Config!;
        if (IdentityProviderFetcher.ComputeManifestUrl(provider.ConfigUrl, config.Config.IdAssertionEndpoint) is not { } tokenUrl)
            throw await ShowErrorAsync(provider, new IdentityCredentialError("IdentityCredentialError", options: null)).ConfigureAwait(true);

        // The request permission to sign-up extension: the fields that the user was prompted for
        var permissionRequested = !isAutoSelected && !account.IsConnected;
        var (fields, disclosureShownFor) = IdentityProviderFetcher.GetDisclosedFields(provider.Options, permissionRequested);
        var list = new List<KeyValuePair<string, string>> { new("client_id", provider.Options.ClientId) };
        if (provider.Options.Nonce is { } nonce)
        {
            list.Add(new("nonce", nonce));
        }

        list.Add(new("account_id", account.Id));
        list.Add(new("disclosure_text_shown", disclosureShownFor.Contains("name") && disclosureShownFor.Contains("email") && disclosureShownFor.Contains("picture") ? "true" : "false"));
        list.Add(new("is_auto_selected", isAutoSelected ? "true" : "false"));
        if (provider.ParamsJson is { } parameters)
        {
            list.Add(new("params", parameters));
        }

        if (fields.Count > 0)
        {
            list.Add(new("fields", string.Join(',', fields)));
        }

        if (disclosureShownFor.Count > 0)
        {
            list.Add(new("disclosure_shown_for", string.Join(',', disclosureShownFor)));
        }

        if (_mode == IdentityCredentialRequestOptionsMode.Active)
        {
            list.Add(new("mode", "active"));
        }

        var response = await _fetcher.PostAsync(tokenUrl, list, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();

        // The error of the response is read whatever its status (the browsers report it with a 4xx or 5xx status)
        var assertion = IdentityProviderFetcher.ExtractJson(response, requireOkStatus: false) is { } body ? IdentityProviderResponses.ParseAssertion(body) : null;
        if (assertion is null || (assertion.Error is null && !HeaderList.IsOkStatus(response.Internal.Status)))
            throw await ShowErrorAsync(provider, new IdentityCredentialError("IdentityCredentialError", options: null)).ConfigureAwait(true);

        if (assertion.Error is { } error)
            throw await ShowErrorAsync(provider, new IdentityCredentialError("IdentityCredentialError", new IdentityCredentialErrorInit { Error = error.Error, Url = GetErrorUrl(error.Url, tokenUrl) })).ConfigureAwait(true);

        var accountId = account.Id;
        string tokenJson;
        if (assertion.TokenJson is { } token)
        {
            tokenJson = token;
        }
        else if (assertion.ContinueOn is { } continueOn)
        {
            // The continuation page of the provider returns the token with IdentityProvider.resolve()
            if (_mediation == CredentialMediationRequirement.Silent)
                throw NetworkError("The continuation page cannot be shown to a silent request");

            if (UrlParser.Parse(continueOn, tokenUrl) is not { } continueOnUrl || continueOnUrl.SerializeOrigin() != tokenUrl.SerializeOrigin())
                throw NetworkError("The continuation URL is not same origin with the identity assertion endpoint");

            var result = await IdentityProviderDialogs.ShowAsync(Window, continueOnUrl, isContinuation: true, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            if (result?.TokenJson is not { } resolvedToken)
                throw NetworkError("The continuation page was closed");

            tokenJson = resolvedToken;
            accountId = result.AccountId ?? accountId;
        }
        else
        {
            throw await ShowErrorAsync(provider, new IdentityCredentialError("IdentityCredentialError", options: null)).ConfigureAwait(true);
        }

        if (!isAutoSelected)
        {
            _fetcher.State.Connect(_fetcher.EmbedderOrigin, _fetcher.RpOrigin, provider.Origin, accountId);

            // The user chose an account: the site no longer requires user mediation
            var origins = Window.Options.OriginsRequiringUserMediation;
            lock (origins)
            {
                origins.Remove(_fetcher.RpOrigin);
            }
        }

        var global = (GlobalScope)Window;
        var value = global.ScriptEngine is { } engine ? engine.ParseJson(global, tokenJson, out _) : DotNetJson.Parse(tokenJson, out _);
        return new IdentityCredential(value, isAutoSelected, provider.ConfigUrl.Serialize());
    }

    /// <summary>The URL of an error of the identity assertion endpoint: relative to the endpoint, of its site and potentially trustworthy, else the empty string.</summary>
    private static string GetErrorUrl(string? url, UrlRecord tokenUrl)
    {
        if (url is null || UrlParser.Parse(url, tokenUrl) is not { Host: { } host } errorUrl || tokenUrl.Host is not { } tokenHost)
            return "";

        if (PublicSuffixList.GetRegistrableDomain(host) != PublicSuffixList.GetRegistrableDomain(tokenHost) || (PublicSuffixList.GetRegistrableDomain(host) is null && host.Serialized != tokenHost.Serialized))
            return "";

        return ReferrerPolicies.IsPotentiallyTrustworthy(errorUrl) ? errorUrl.Serialize() : "";
    }

    /// <summary>Shows the error of the identity provider to the user, and returns it once the user closed it.</summary>
    private async Task<IdentityCredentialError> ShowErrorAsync(Provider provider, IdentityCredentialError error)
    {
        var prompt = new IdentityCredentialErrorPrompt(Window, _context, _mode, provider.ConfigUrl.Serialize(), error);
        await PromptAsync((backend, token) => Acknowledge(backend.ShowIdentityCredentialErrorAsync(prompt, token)), dismissed: true).ConfigureAwait(true);
        return error;

        static async ValueTask<bool> Acknowledge(ValueTask task)
        {
            await task.ConfigureAwait(false);
            return true;
        }
    }

    /// <summary>Shows a prompt of the backend: the task completes in a task of the event loop with the answer, or is canceled with the request.</summary>
    private Task<T> PromptAsync<T>(Func<UserAgentUIBackend, CancellationToken, ValueTask<T>> prompt, T dismissed)
    {
        var cancellationToken = _cancellation.Token;
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<T>();
        var registration = cancellationToken.Register(() => Window.QueueGlobalTask(() => completion.TrySetCanceled(cancellationToken)));
        UserAgentUI.Show(Window, prompt, dismissed, answer =>
        {
            registration.Dispose();
            completion.TrySetResult(answer);
        }, cancellationToken);
        return completion.Task;
    }

    private static DomException NetworkError(string message) => new(message, "NetworkError");

    /// <summary>
    /// https://w3c-fedid.github.io/FedCM/#dom-identitycredential-disconnect: the account is disconnected by the provider and
    /// removed from the connected accounts set.
    /// </summary>
    public static Task Disconnect(Window window, IdentityCredentialDisconnectOptions options)
    {
        var document = window.Document;
        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.IdentityCredentialsGet))
            return Task.FromException(DomException.NotAllowed("The document is not allowed to use the identity-credentials-get feature"));

        var result = new TaskCompletionSource();
        if (UrlParser.Parse(options.ConfigUrl, baseUrl: null) is not { } configUrl)
        {
            window.QueueGlobalTask(() => result.TrySetException(DomException.InvalidState("The config URL is not a valid URL")));
            return result.Task;
        }

        var fetcher = new IdentityProviderFetcher(window);
        var idpOrigin = configUrl.SerializeOrigin();

        // As in the browsers, the hint of the account is not checked: any account of the provider must be connected to the site
        if (PendingDisconnects.TryGetValue(window, out _) || document.Origin.IsOpaque || !ReferrerPolicies.IsPotentiallyTrustworthy(configUrl)
            || !fetcher.State.HasConnection(fetcher.EmbedderOrigin, fetcher.RpOrigin, idpOrigin, accountId: null))
        {
            window.QueueGlobalTask(() => result.TrySetException(NetworkError("There is no account of the identity provider to disconnect")));
            return result.Task;
        }

        var marker = new object();
        PendingDisconnects.AddOrUpdate(window, marker);
        _ = DisconnectAsync(fetcher, configUrl, options, result, marker);
        return result.Task;
    }

    private static async Task DisconnectAsync(IdentityProviderFetcher fetcher, UrlRecord configUrl, IdentityCredentialDisconnectOptions options, TaskCompletionSource result, object marker)
    {
        try
        {
            var idpOrigin = configUrl.SerializeOrigin();
            var (embedder, rp) = (fetcher.EmbedderOrigin, fetcher.RpOrigin);
            if (await fetcher.FetchConfigAsync(configUrl, CancellationToken.None).ConfigureAwait(true) is not { } config
                || IdentityProviderFetcher.ComputeManifestUrl(configUrl, config.Config.DisconnectEndpoint) is not { } disconnectUrl)
            {
                result.TrySetException(NetworkError("The identity provider has no disconnect endpoint"));
                return;
            }

            // https://w3c-fedid.github.io/FedCM/#send-a-disconnect-request
            var response = await fetcher.PostAsync(disconnectUrl, [new("client_id", options.ClientId), new("account_hint", options.AccountHint)], CancellationToken.None).ConfigureAwait(true);
            if (IdentityProviderFetcher.ExtractJson(response, requireOkStatus: true) is not { } body || IdentityProviderResponses.ParseDisconnect(body) is not { } accountId)
            {
                fetcher.State.DisconnectAll(embedder, rp, idpOrigin);
                result.TrySetException(NetworkError("The identity provider failed to disconnect the account"));
                return;
            }

            if (!fetcher.State.Disconnect(embedder, rp, idpOrigin, accountId))
            {
                fetcher.State.DisconnectAll(embedder, rp, idpOrigin);
            }

            result.TrySetResult();
        }
        finally
        {
            if (PendingDisconnects.TryGetValue(fetcher.Window, out var pending) && ReferenceEquals(pending, marker))
            {
                PendingDisconnects.Remove(fetcher.Window);
            }
        }
    }

    /// <summary>https://w3c-fedid.github.io/FedCM/#dom-identityprovider-getuserinfo</summary>
    public static Task<IReadOnlyList<IdentityUserInfo>> GetUserInfo(Window window, IdentityProviderConfig provider)
    {
        var result = new TaskCompletionSource<IReadOnlyList<IdentityUserInfo>>();
        var document = window.Document;
        void Reject(DomException exception) => window.QueueGlobalTask(() => result.TrySetException(exception));

        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.IdentityCredentialsGet))
        {
            Reject(DomException.NotAllowed("The document is not allowed to use the identity-credentials-get feature"));
            return result.Task;
        }

        if (UrlParser.Parse(provider.ConfigUrl, baseUrl: null) is not { } configUrl)
        {
            Reject(DomException.InvalidState("The config URL is not a valid URL"));
            return result.Task;
        }

        var fetcher = new IdentityProviderFetcher(window);
        var idpOrigin = configUrl.SerializeOrigin();
        if (!fetcher.State.HasConnectionInEmbedder(fetcher.EmbedderOrigin, idpOrigin, accountId: null) && !fetcher.AreThirdPartyCookiesAvailable(idpOrigin))
        {
            Reject(NetworkError("No account of the identity provider is connected"));
            return result.Task;
        }

        if (fetcher.RpOrigin != idpOrigin)
        {
            Reject(DomException.InvalidState("getUserInfo() can only be called by a document of the identity provider"));
            return result.Task;
        }

        // The frame of the provider is embedded by the site; a provider whose user is logged out has no account
        if (window.BrowsingContext is not { Parent: not null } || fetcher.State.GetLoginStatus(idpOrigin) == false)
        {
            Reject(NetworkError("getUserInfo() can only be called in an iframe of the identity provider whose user is logged in"));
            return result.Task;
        }

        _ = GetUserInfoAsync(fetcher, configUrl, provider, result);
        return result.Task;
    }

    private static async Task GetUserInfoAsync(IdentityProviderFetcher fetcher, UrlRecord configUrl, IdentityProviderConfig provider, TaskCompletionSource<IReadOnlyList<IdentityUserInfo>> result)
    {
        if (await fetcher.FetchConfigAsync(configUrl, CancellationToken.None).ConfigureAwait(true) is not { } config)
        {
            result.TrySetException(NetworkError("The config file of the identity provider cannot be fetched"));
            return;
        }

        var idpOrigin = configUrl.SerializeOrigin();
        var accounts = await fetcher.FetchAccountsAsync(config, provider: null, CancellationToken.None).ConfigureAwait(true);
        var hasEligibleAccount = false;
        foreach (var account in accounts)
        {
            if (account.ApprovedClients is { Count: > 0 } approvedClients && !approvedClients.Contains(provider.ClientId, StringComparer.Ordinal))
                continue;

            // The account is connected to the embedder of the frame of the provider
            if (fetcher.State.HasConnectionInEmbedder(fetcher.EmbedderOrigin, idpOrigin, account.Id) || (account.ApprovedClients is not null && fetcher.AreThirdPartyCookiesAvailable(idpOrigin)))
            {
                hasEligibleAccount = true;
            }
        }

        if (!hasEligibleAccount)
        {
            result.TrySetException(NetworkError("No account of the identity provider is connected"));
            return;
        }

        result.TrySetResult(accounts.ConvertAll(account => new IdentityUserInfo { Email = account.Email, Name = account.Name, GivenName = account.GivenName, Picture = account.Picture }));
    }

    private enum ProviderStatus
    {
        None,
        LoggedOut,
        Mismatch,
        HasAccounts,
    }

    /// <summary>A provider of the request, and what the request learned about it.</summary>
    private sealed class Provider(IdentityProviderRequestOptions options, UrlRecord configUrl)
    {
        public IdentityProviderRequestOptions Options { get; } = options;

        public UrlRecord ConfigUrl { get; } = configUrl;

        public string Origin { get; } = configUrl.SerializeOrigin();

        /// <summary>The JSON of the custom parameters, or null.</summary>
        public string? ParamsJson { get; set; }

        public ProviderStatus Status { get; set; }

        public IdentityProviderConfigFile? Config { get; set; }

        public List<IdentityProviderAccount> Accounts { get; set; } = [];

        public IdentityProviderClientMetadata? ClientMetadata { get; set; }
    }
}
