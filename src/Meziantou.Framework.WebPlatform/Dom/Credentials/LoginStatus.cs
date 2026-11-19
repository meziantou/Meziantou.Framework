using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The login status of an identity provider (https://w3c-fedid.github.io/login-status/#enumdef-loginstatus).</summary>
/// <seealso href="https://w3c-fedid.github.io/login-status/#enumdef-loginstatus"/>
[IdlInterface("LoginStatus", "https://w3c-fedid.github.io/login-status/#enumdef-loginstatus")]
public enum LoginStatus
{
    /// <summary>The user is signed in to the identity provider.</summary>
    /// <seealso href="https://w3c-fedid.github.io/login-status/#dom-loginstatus-logged-in"/>
    [IdlMember("logged-in", "https://w3c-fedid.github.io/login-status/#dom-loginstatus-logged-in")]
    LoggedIn,

    /// <summary>The user is signed out of the identity provider.</summary>
    /// <seealso href="https://w3c-fedid.github.io/login-status/#dom-loginstatus-logged-out"/>
    [IdlMember("logged-out", "https://w3c-fedid.github.io/login-status/#dom-loginstatus-logged-out")]
    LoggedOut,
}

/// <summary>
/// The Login Status API of a window (https://w3c-fedid.github.io/login-status/#navigatorlogin): <c>navigator.login</c>. An
/// identity provider tells the user agent whether the user is signed in to it, with <see cref="SetStatus"/> or with the
/// <c>Set-Login</c> response header; a FedCM request does not fetch the accounts of a provider whose user is signed out.
/// </summary>
/// <seealso href="https://w3c-fedid.github.io/login-status/#navigatorlogin"/>
[IdlInterface("NavigatorLogin", "https://w3c-fedid.github.io/login-status/#navigatorlogin")]
public sealed class NavigatorLogin
{
    private readonly Window _window;

    internal NavigatorLogin(Window window)
    {
        _window = window;
    }

    /// <summary>
    /// Sets the login status of the origin of the document (https://w3c-fedid.github.io/login-status/#dom-navigatorlogin-setstatus).
    /// The task fails with a <c>SecurityError</c> <see cref="DomException"/> when the document is in a frame that is not same
    /// site with its ancestors.
    /// </summary>
    /// <param name="status">The login status.</param>
    /// <seealso href="https://w3c-fedid.github.io/login-status/#dom-navigatorlogin-setstatus"/>
    [IdlMember("setStatus", "https://w3c-fedid.github.io/login-status/#dom-navigatorlogin-setstatus")]
    public Task SetStatus(LoginStatus status)
    {
        var document = _window.Document;
        if (!LoginStatusHeader.IsSameSiteWithAncestors(document) || document.Origin.IsOpaque)
            return Task.FromException(new DomException("The login status can only be set by a document that is same site with its ancestors", "SecurityError"));

        _window.Options.IdentityCredentials.SetLoginStatus(document.Origin.Serialize(), status == LoginStatus.LoggedIn);
        return Task.CompletedTask;
    }
}

// The Login Status API (https://w3c-fedid.github.io/login-status/#javascript-api)
public sealed partial class Navigator
{
    private NavigatorLogin? _login;

    /// <summary>Gets the Login Status API of the window (https://w3c-fedid.github.io/login-status/#dom-navigator-login).</summary>
    /// <seealso href="https://w3c-fedid.github.io/login-status/#dom-navigator-login"/>
    [IdlMember("login", "https://w3c-fedid.github.io/login-status/#dom-navigator-login")]
    public NavigatorLogin Login => _login ??= new NavigatorLogin(_window);
}

/// <summary>The <c>Set-Login</c> response header (https://w3c-fedid.github.io/login-status/#login-status-http).</summary>
internal static class LoginStatusHeader
{
    /// <summary>
    /// Processes the <c>Set-Login</c> header of a response of an HTTP fetch or of a redirect: it sets the login status of the
    /// origin of the response for a top-level navigation, or for a request of a document that is same site with its ancestors
    /// to its own site.
    /// </summary>
    public static void ProcessResponse(FetchRequest request, FetchResponse response)
    {
        if (StructuredFields.GetItem(response.Internal.HeaderList, "Set-Login") is not { Value: StructuredFieldToken { Value: var token } })
            return;

        bool loggedIn;
        switch (token)
        {
            case "logged-in":
                loggedIn = true;
                break;
            case "logged-out":
                loggedIn = false;
                break;
            default:
                return;
        }

        var url = request.CurrentUrl;
        var origin = OriginRecord.FromUrl(url);
        if (origin.IsOpaque)
            return;

        if (request.Destination != RequestDestination.Document || request.Mode != RequestMode.Navigate)
        {
            if (request.Client is not { } client)
                return;

            if (request.Origin == "null" || UrlParser.Parse(request.Origin, baseUrl: null) is not { } requestOriginUrl || !OriginRecord.IsSameSite(origin, OriginRecord.FromUrl(requestOriginUrl)))
                return;

            if (!IsSameSiteWithAncestors(client))
                return;
        }

        var options = request.Navigable?.Options ?? request.Client?.EnvironmentOptions ?? request.KeepaliveBrowsingContext?.Options;
        options?.IdentityCredentials.SetLoginStatus(origin.Serialize(), loggedIn);
    }

    /// <summary>https://w3c-fedid.github.io/login-status/#same-site-with-its-ancestors</summary>
    public static bool IsSameSiteWithAncestors(Document document)
    {
        if (document.WorkerGlobal is not null || document.BrowsingContext is null)
            return false;

        var origin = document.Origin;
        for (var navigable = document.BrowsingContext.Parent; navigable is not null; navigable = navigable.Parent)
        {
            if (!OriginRecord.IsSameSite(navigable.Document.Origin, origin))
                return false;
        }

        return true;
    }
}
