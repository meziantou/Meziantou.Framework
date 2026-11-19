using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The FedCM state of the user agent, shared by the browsing contexts of a <see cref="BrowsingContextOptions"/>: the connected
/// accounts set (https://w3c-fedid.github.io/FedCM/#browser-connected-accounts-set) and the login status map of the
/// identity providers (https://w3c-fedid.github.io/login-status/#login-status-map).
/// </summary>
internal sealed class IdentityCredentialState
{
    private readonly Lock _lock = new();
    private readonly List<ConnectedAccount> _connectedAccounts = [];
    private readonly Dictionary<string, bool> _loginStatus = new(StringComparer.Ordinal);

    /// <summary>https://w3c-fedid.github.io/login-status/#get-the-login-status: true for logged-in, false for logged-out, null for unknown.</summary>
    public bool? GetLoginStatus(string origin)
    {
        lock (_lock)
        {
            return _loginStatus.TryGetValue(origin, out var loggedIn) ? loggedIn : null;
        }
    }

    /// <summary>https://w3c-fedid.github.io/login-status/#set-the-login-status</summary>
    public void SetLoginStatus(string origin, bool loggedIn)
    {
        lock (_lock)
        {
            _loginStatus[origin] = loggedIn;
        }
    }

    /// <summary>Whether the set contains a connection of the site to an account of the provider (any account when <paramref name="accountId"/> is null).</summary>
    public bool HasConnection(string embedder, string rp, string idp, string? accountId)
    {
        lock (_lock)
        {
            return _connectedAccounts.Exists(account => account.Embedder == embedder && account.Rp == rp && account.Idp == idp && (accountId is null || account.AccountId == accountId));
        }
    }

    /// <summary>Whether an account of the provider is connected to a site embedded in <paramref name="embedder"/> (<c>IdentityProvider.getUserInfo()</c>).</summary>
    public bool HasConnectionInEmbedder(string embedder, string idp, string? accountId)
    {
        lock (_lock)
        {
            return _connectedAccounts.Exists(account => account.Embedder == embedder && account.Idp == idp && (accountId is null || account.AccountId == accountId));
        }
    }

    /// <summary>https://w3c-fedid.github.io/FedCM/#create-a-connection-between-the-rp-and-the-idp-account</summary>
    public void Connect(string embedder, string rp, string idp, string accountId)
    {
        var account = new ConnectedAccount(embedder, rp, idp, accountId);
        lock (_lock)
        {
            if (!_connectedAccounts.Contains(account))
            {
                _connectedAccounts.Add(account);
            }
        }
    }

    /// <summary>https://w3c-fedid.github.io/FedCM/#remove-a-connection: returns whether the connection was removed.</summary>
    public bool Disconnect(string embedder, string rp, string idp, string accountId)
    {
        lock (_lock)
        {
            return _connectedAccounts.Remove(new ConnectedAccount(embedder, rp, idp, accountId));
        }
    }

    /// <summary>https://w3c-fedid.github.io/FedCM/#remove-all-connections</summary>
    public void DisconnectAll(string embedder, string rp, string idp)
    {
        lock (_lock)
        {
            _connectedAccounts.RemoveAll(account => account.Embedder == embedder && account.Rp == rp && account.Idp == idp);
        }
    }

    private readonly record struct ConnectedAccount(string Embedder, string Rp, string Idp, string AccountId);
}

/// <summary>The config file of an identity provider, with the URLs of its accounts endpoint and of its login page.</summary>
internal sealed record IdentityProviderConfigFile(UrlRecord ConfigUrl, IdentityProviderApiConfig Config, UrlRecord AccountsUrl, UrlRecord LoginUrl);

/// <summary>
/// The fetches of FedCM (https://w3c-fedid.github.io/FedCM/#idp-api) for a site: the requests of the user agent, whose client
/// is the document of the site, have the <c>webidentity</c> destination, no referrer and no service worker, follow no
/// redirect, and send only the cookies of the cross-site requests (<c>SameSite=None</c>). The requests without CORS (the
/// well-known file, the config file, the accounts and the client metadata) are made from an opaque origin, and the user
/// agent reads their response (the "user-agent-no-cors" mode, https://github.com/whatwg/fetch/pull/1533).
/// </summary>
internal sealed class IdentityProviderFetcher(Window window)
{
    private static readonly string[] RecognizedFields = ["name", "email", "tel", "username", "picture"];

    public Window Window { get; } = window;

    public Document Document => Window.Document;

    public IdentityCredentialState State => Window.Options.IdentityCredentials;

    /// <summary>The serialization of the origin of the site (the document).</summary>
    public string RpOrigin => Document.Origin.Serialize();

    /// <summary>The serialization of the origin of the top-level document (the embedder).</summary>
    public string EmbedderOrigin => (Document.BrowsingContext?.Top.Document ?? Document).Origin.Serialize();

    /// <summary>https://w3c-fedid.github.io/FedCM/#manifest-url: a URL of a member of the config file, relative to the config URL, same origin with it and potentially trustworthy, or null.</summary>
    public static UrlRecord? ComputeManifestUrl(UrlRecord configUrl, string? value)
    {
        if (value is null || UrlParser.Parse(value, configUrl) is not { } url)
            return null;

        return url.SerializeOrigin() == configUrl.SerializeOrigin() && ReferrerPolicies.IsPotentiallyTrustworthy(url) ? url : null;
    }

    /// <summary>
    /// https://w3c-fedid.github.io/FedCM/#fetch-the-config-file: the config file, checked against the well-known file of the
    /// site of the provider (unless the provider is same site with the site), or null on failure.
    /// </summary>
    public async Task<IdentityProviderConfigFile?> FetchConfigAsync(UrlRecord configUrl, CancellationToken cancellationToken)
    {
        if (!ReferrerPolicies.IsPotentiallyTrustworthy(configUrl) || configUrl.Host is not { } host)
            return null;

        // The well-known file of the registrable domain of the provider, which is not fetched for a provider of the site of the
        // document (the cookies of the domain are shared by the site: the check would not protect the user)
        var rootHost = PublicSuffixList.GetRegistrableDomain(host) ?? host.Serialized;
        var rootUrl = UrlParser.Parse(configUrl.Scheme + "://" + rootHost + "/.well-known/web-identity", baseUrl: null);
        var rpOrigin = Document.Origin;
        var skipWellKnown = !rpOrigin.IsOpaque && rpOrigin.Scheme == configUrl.Scheme && (PublicSuffixList.GetRegistrableDomain(rpOrigin.Host) ?? rpOrigin.Host.Serialized) == rootHost;
        var wellKnownTask = skipWellKnown || rootUrl is null ? null : FetchJsonAsync(rootUrl, cancellationToken);

        var configBody = await FetchJsonAsync(configUrl, cancellationToken).ConfigureAwait(true);
        if (configBody is null || IdentityProviderResponses.ParseConfig(configBody) is not { } config)
            return null;

        if (ComputeManifestUrl(configUrl, config.LoginUrl) is not { } loginUrl || ComputeManifestUrl(configUrl, config.AccountsEndpoint) is not { } accountsUrl)
            return null;

        var result = new IdentityProviderConfigFile(configUrl, config, accountsUrl, loginUrl);
        if (skipWellKnown)
            return result;

        if (wellKnownTask is null || await wellKnownTask.ConfigureAwait(true) is not { } wellKnownBody || IdentityProviderResponses.ParseWellKnown(wellKnownBody) is not { } wellKnown)
            return null;

        // As in the browsers, the extension that requires the accounts endpoint and the login URL in the well-known file of a
        // provider that has a client metadata endpoint is not applied
        if (wellKnown.AccountsEndpoint is not null && wellKnown.LoginUrl is not null)
        {
            return ComputeManifestUrl(configUrl, wellKnown.AccountsEndpoint)?.Serialize() == accountsUrl.Serialize() && ComputeManifestUrl(configUrl, wellKnown.LoginUrl)?.Serialize() == loginUrl.Serialize() ? result : null;
        }

        return wellKnown.ProviderUrls is [var providerUrl, ..] && ComputeManifestUrl(configUrl, providerUrl)?.Serialize() == configUrl.Serialize() ? result : null;
    }

    /// <summary>
    /// https://w3c-fedid.github.io/FedCM/#fetch-accounts: the accounts of the provider (an empty list on failure), whose
    /// pictures are fetched, filtered by the hints of the request and the account label of the config file. The login status
    /// of the provider is set to logged-out when it returns no account, else to logged-in.
    /// </summary>
    public async Task<List<IdentityProviderAccount>> FetchAccountsAsync(IdentityProviderConfigFile config, IdentityProviderRequestOptions? provider, CancellationToken cancellationToken)
    {
        var body = await FetchJsonAsync(config.AccountsUrl, cancellationToken, credentials: true).ConfigureAwait(true);
        var providerOrigin = config.ConfigUrl.SerializeOrigin();
        if (body is null || IdentityProviderResponses.ParseAccounts(body) is not { Count: > 0 } accounts)
        {
            State.SetLoginStatus(providerOrigin, loggedIn: false);
            return [];
        }

        State.SetLoginStatus(providerOrigin, loggedIn: true);

        // The pictures are fetched before the accounts are filtered: the provider cannot know which hints were given
        await Task.WhenAll(accounts.Select(account => FetchPictureAsync(account, cancellationToken))).ConfigureAwait(true);

        var result = new List<IdentityProviderAccount>(accounts.Count);
        foreach (var account in accounts)
        {
            account.ConfigUrl = config.ConfigUrl.Serialize();
            if (provider?.LoginHint is { Length: > 0 } loginHint && account.LoginHints?.Contains(loginHint, StringComparer.Ordinal) != true)
                continue;

            if (provider?.DomainHint is { Length: > 0 } domainHint && (domainHint == "any" ? account.DomainHints is not { Count: > 0 } : account.DomainHints?.Contains(domainHint, StringComparer.Ordinal) != true))
                continue;

            if (config.Config.AccountLabel is { } label && account.LabelHints?.Contains(label, StringComparer.Ordinal) != true)
                continue;

            result.Add(account);
        }

        return result;
    }

    /// <summary>https://w3c-fedid.github.io/FedCM/#fetch-the-account-picture: the content of the picture, kept with the account.</summary>
    private async Task FetchPictureAsync(IdentityProviderAccount account, CancellationToken cancellationToken)
    {
        if (account.Picture is null || UrlParser.Parse(account.Picture, Document.DocumentBaseUrl) is not { Scheme: "http" or "https" } url)
            return;

        var response = await FetchAsync(CreateRequest(url, cors: false, credentials: false, accept: null), cancellationToken).ConfigureAwait(true);
        if (!response.IsNetworkError && HeaderList.IsOkStatus(response.Internal.Status) && response.Internal.Body is { Length: > 0 } picture)
        {
            account.PictureContent = picture;
        }
    }

    /// <summary>https://w3c-fedid.github.io/FedCM/#fetch-the-client-metadata, or null on failure.</summary>
    public async Task<IdentityProviderClientMetadata?> FetchClientMetadataAsync(IdentityProviderConfigFile config, IdentityProviderConfig provider, CancellationToken cancellationToken)
    {
        if (ComputeManifestUrl(config.ConfigUrl, config.Config.ClientMetadataEndpoint) is not { } url)
            return null;

        var query = "client_id=" + Uri.EscapeDataString(provider.ClientId);
        var embedderOrigin = (Document.BrowsingContext?.Top.Document ?? Document).Origin;
        if (!OriginRecord.IsSameSite(Document.Origin, embedderOrigin))
        {
            query += "&top_level_origin=" + Uri.EscapeDataString(embedderOrigin.Serialize());
        }

        url.Query = query;
        var request = CreateRequest(url, cors: false, credentials: false);
        request.HeaderList.Add(new("Origin", RpOrigin));
        var response = await FetchAsync(request, cancellationToken).ConfigureAwait(true);
        return ExtractJson(response, requireOkStatus: true) is { } body ? IdentityProviderResponses.ParseClientMetadata(body) : null;
    }

    /// <summary>The body of a POST request of an endpoint of the provider (the identity assertion and disconnect endpoints): a CORS request with the cookies.</summary>
    public Task<FetchResponse> PostAsync(UrlRecord url, IEnumerable<KeyValuePair<string, string>> fields, CancellationToken cancellationToken)
    {
        var request = CreateRequest(url, cors: true, credentials: true);
        request.Method = "POST";
        request.Body = System.Text.Encoding.UTF8.GetBytes(FormUrlEncoded.Serialize(fields));
        request.HeaderList.Add(new("Content-Type", "application/x-www-form-urlencoded"));
        return FetchAsync(request, cancellationToken);
    }

    /// <summary>The fields of the request permission to sign-up extension (https://w3c-fedid.github.io/FedCM/#fetch-an-identity-assertion): the requested fields and the recognized ones that the user was prompted for.</summary>
    public static (IReadOnlyList<string> Fields, IReadOnlyList<string> DisclosureShownFor) GetDisclosedFields(IdentityProviderRequestOptions provider, bool permissionRequested)
    {
        if (!permissionRequested)
            return ([], []);

        var fields = provider.Fields ?? ["name", "email", "picture"];
        return (fields, fields.Where(field => RecognizedFields.Contains(field, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// https://w3c-fedid.github.io/FedCM/#extract-the-json-fetch-response: the body of a response whose MIME type is a JSON MIME
    /// type (and whose status is an ok status when <paramref name="requireOkStatus"/> is true), or null.
    /// </summary>
    public static byte[]? ExtractJson(FetchResponse response, bool requireOkStatus)
    {
        if (response.IsNetworkError)
            return null;

        var internalResponse = response.Internal;
        if (requireOkStatus && !HeaderList.IsOkStatus(internalResponse.Status))
            return null;

        if (HeaderList.ExtractMimeType(internalResponse.HeaderList) is not { } mimeType || !IsJsonMimeType(mimeType))
            return null;

        return internalResponse.Body ?? [];
    }

    /// <summary>https://mimesniff.spec.whatwg.org/#json-mime-type</summary>
    private static bool IsJsonMimeType(MimeType mimeType) => mimeType.Subtype.EndsWith("+json", StringComparison.Ordinal) || mimeType.Essence is "application/json" or "text/json";

    private async Task<byte[]?> FetchJsonAsync(UrlRecord url, CancellationToken cancellationToken, bool credentials = false)
    {
        var response = await FetchAsync(CreateRequest(url, cors: false, credentials), cancellationToken).ConfigureAwait(true);
        return ExtractJson(response, requireOkStatus: true);
    }

    private FetchRequest CreateRequest(UrlRecord url, bool cors, bool credentials, string? accept = "application/json")
    {
        var cookieJar = Document.BrowsingContext?.GetCookieJar(credentialless: false);
        var request = new FetchRequest(url)
        {
            Client = Document,
            Origin = cors ? RpOrigin : "null",
            Referrer = "no-referrer",
            ReferrerPolicy = "no-referrer",
            ServiceWorkersMode = ServiceWorkersMode.None,
            Destination = RequestDestination.WebIdentity,
            Mode = cors ? RequestMode.Cors : RequestMode.NoCors,
            UserAgentNoCors = !cors,
            CredentialsMode = credentials ? FetchCredentialsMode.Include : FetchCredentialsMode.Omit,
            RedirectMode = FetchRedirectMode.Error,
            CacheMode = accept is null ? "default" : "no-store",

            // The cookies of a cross-site request (SameSite=None), not partitioned by the site
            CookieContext = new FetchCookieContext(cookieJar, SiteForCookies: "null", TopLevelOrigin: url.SerializeOrigin()),
        };

        if (accept is not null)
        {
            request.HeaderList.Add(new("Accept", accept));
        }

        return request;
    }

    /// <summary>Fetches a request: the task completes in a task of the event loop of the window.</summary>
    private Task<FetchResponse> FetchAsync(FetchRequest request, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<FetchResponse>();
        Fetching.Fetch(request, response => completion.TrySetResult(response), Window.QueueGlobalTask, cancellationToken);
        return completion.Task;
    }

    /// <summary>
    /// https://w3c-fedid.github.io/FedCM/#eligible-for-auto-reauthentication: the account is connected to the site, or its
    /// approved clients contain the client identifier of the site and the cookies of the provider are available to the site.
    /// </summary>
    public bool IsEligibleForAutoReauthentication(IdentityProviderAccount account, IdentityProviderConfig provider, string idpOrigin)
    {
        if (account.ApprovedClients is { } approvedClients && !approvedClients.Contains(provider.ClientId, StringComparer.Ordinal))
            return false;

        if (State.HasConnection(EmbedderOrigin, RpOrigin, idpOrigin, account.Id))
            return true;

        return account.ApprovedClients is not null && AreThirdPartyCookiesAvailable(idpOrigin);
    }

    /// <summary>https://w3c-fedid.github.io/FedCM/#compute-the-connection-status: whether the account is connected (else the user signs up).</summary>
    public bool IsConnected(IdentityProviderAccount account, IdentityProviderConfig provider, string idpOrigin)
    {
        if (account.ApprovedClients is { } approvedClients)
            return approvedClients.Contains(provider.ClientId, StringComparer.Ordinal);

        return State.HasConnection(EmbedderOrigin, RpOrigin, idpOrigin, account.Id);
    }

    /// <summary>Whether the cookies of the provider are available to the document: a provider of the site of the top-level document, or a third-party whose cookies are not blocked.</summary>
    public bool AreThirdPartyCookiesAvailable(string idpOrigin)
    {
        if (UrlParser.Parse(idpOrigin, baseUrl: null) is not { } idpUrl)
            return false;

        var idp = OriginRecord.FromUrl(idpUrl);
        var topLevel = (Document.BrowsingContext?.Top.Document ?? Document).Origin;
        if (!topLevel.IsOpaque && OriginRecord.IsSameSite(topLevel, idp))
            return true;

        return Document.BrowsingContext?.GetCookieJar(credentialless: false) is { } cookieJar && !cookieJar.IsUnpartitionedCookieAccessBlocked(topLevel, idp);
    }
}

/// <summary>
/// The windows that a FedCM request opens for the identity provider (https://w3c-fedid.github.io/FedCM/#show-an-idp-login-dialog
/// and https://w3c-fedid.github.io/FedCM/#show-a-continuation-dialog): new top-level browsing contexts without opener, which
/// the page of the provider closes with <c>IdentityProvider.close()</c> or <c>IdentityProvider.resolve()</c>.
/// </summary>
internal static class IdentityProviderDialogs
{
    private static readonly ConditionalWeakTable<BrowsingContext, Dialog> Dialogs = [];

    /// <summary>
    /// Opens a dialog and waits until it is closed: the result of <c>IdentityProvider.resolve()</c> for a continuation dialog,
    /// <see cref="DialogResult.Closed"/> when the page calls <c>IdentityProvider.close()</c>, or null when the user closes the
    /// window or the request is canceled. The task completes in a task of the event loop of <paramref name="window"/>.
    /// </summary>
    public static async Task<DialogResult?> ShowAsync(Window window, UrlRecord url, bool isContinuation, CancellationToken cancellationToken)
    {
        if (window.BrowsingContext is not { } browsingContext || cancellationToken.IsCancellationRequested)
            return null;

        var popup = browsingContext.CreateAuxiliaryBrowsingContext("", opener: null);
        var dialog = new Dialog(window, popup, isContinuation, url.SerializeOrigin());
        Dialogs.AddOrUpdate(popup, dialog);
        using var registration = cancellationToken.Register(() => window.QueueGlobalTask(() => dialog.Complete(null)));
        popup.Navigate(url.Serialize());
        return await dialog.Completion.Task.ConfigureAwait(true);
    }

    /// <summary>
    /// https://w3c-fedid.github.io/FedCM/#dom-identityprovider-close: the dialog of the window is closed. Only a document of the
    /// identity provider can close it.
    /// </summary>
    public static void Close(Window window)
    {
        if (GetDialog(window) is { } dialog && dialog.IsProviderDocument(window))
        {
            dialog.Window.QueueGlobalTask(() => dialog.Complete(DialogResult.Closed));
        }
    }

    /// <summary>https://w3c-fedid.github.io/FedCM/#dom-identityprovider-resolve</summary>
    public static Task Resolve(Window window, object? token, string? accountId)
    {
        if (GetDialog(window) is not { IsContinuation: true } dialog)
            return Task.FromException(DomException.NotAllowed("IdentityProvider.resolve() can only be called by the continuation page of a FedCM request"));

        // A document of another origin in the window of the dialog (e.g. a third-party iframe) cannot complete the sign-in
        if (!dialog.IsProviderDocument(window))
            return Task.FromException(DomException.NotAllowed("IdentityProvider.resolve() can only be called by a document of the identity provider"));

        // The token is a value of the realm of the provider: it is given to the site as JSON
        var tokenJson = ModelContext.SerializeJson(window, token) ?? "null";
        dialog.Window.QueueGlobalTask(() => dialog.Complete(new DialogResult(tokenJson, accountId)));
        return Task.CompletedTask;
    }

    /// <summary>The user closed the window of a dialog (it was destroyed before the page closed it).</summary>
    public static void NotifyDestroyed(BrowsingContext browsingContext)
    {
        if (Dialogs.TryGetValue(browsingContext, out var dialog))
        {
            dialog.Window.QueueGlobalTask(() => dialog.Complete(null));
        }
    }

    private static Dialog? GetDialog(Window window)
        => window.BrowsingContext?.Top is { } top && Dialogs.TryGetValue(top, out var dialog) && !dialog.Completion.Task.IsCompleted ? dialog : null;

    /// <summary>The result of a dialog: the token (as JSON) and the account of <c>IdentityProvider.resolve()</c>, or <see cref="Closed"/>.</summary>
    internal sealed record DialogResult(string? TokenJson, string? AccountId)
    {
        public static DialogResult Closed { get; } = new(TokenJson: null, AccountId: null);
    }

    private sealed class Dialog(Window window, BrowsingContext popup, bool isContinuation, string providerOrigin)
    {
        public Window Window { get; } = window;

        public bool IsContinuation { get; } = isContinuation;

        /// <summary>Whether the document of a window of the dialog is a document of the identity provider (same origin with the URL of the dialog).</summary>
        public bool IsProviderDocument(Window caller) => caller.Document.Origin.Serialize() == providerOrigin;

        public TaskCompletionSource<DialogResult?> Completion { get; } = new();

        /// <summary>Closes the window of the dialog and completes it (from a task of the event loop of the site).</summary>
        public void Complete(DialogResult? result)
        {
            if (Completion.Task.IsCompleted)
                return;

            Dialogs.Remove(popup);
            popup.Close();
            Completion.TrySetResult(result);
        }
    }
}
