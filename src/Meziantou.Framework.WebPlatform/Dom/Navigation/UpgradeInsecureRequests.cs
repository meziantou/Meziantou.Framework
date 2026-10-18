using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://w3c.github.io/webappsec-upgrade-insecure-requests/#insecure-requests-policy</summary>
internal enum InsecureRequestsPolicy
{
    DoNotUpgrade,
    Upgrade,
}

public partial class Document
{
    /// <summary>
    /// The insecure requests policy of the environment settings object of the document
    /// (https://w3c.github.io/webappsec-upgrade-insecure-requests/#insecure-requests-policy), set by the
    /// <c>upgrade-insecure-requests</c> directive of an enforced content security policy, or inherited (see
    /// <see cref="UpgradeInsecureRequests.Inherit"/>).
    /// </summary>
    internal InsecureRequestsPolicy InsecureRequestsPolicy { get; private set; }

    /// <summary>
    /// The upgrade insecure navigations set (https://w3c.github.io/webappsec-upgrade-insecure-requests/#upgrade-insecure-navigations-set):
    /// the host and the port (-1 for the default port of the scheme) of the URLs whose navigations are upgraded, or null when empty.
    /// </summary>
    internal HashSet<(string Host, int Port)>? UpgradeInsecureNavigationsSet { get; private set; }

    /// <summary>Sets the insecure requests policy to "Upgrade", and adds <paramref name="navigations"/> to the upgrade insecure navigations set.</summary>
    internal void SetInsecureRequestsPolicyUpgrade(IEnumerable<(string Host, int Port)>? navigations)
    {
        InsecureRequestsPolicy = InsecureRequestsPolicy.Upgrade;
        if (navigations is not null)
        {
            (UpgradeInsecureNavigationsSet ??= []).UnionWith(navigations);
        }
    }
}

/// <summary>
/// Upgrade Insecure Requests (https://w3c.github.io/webappsec-upgrade-insecure-requests/): the <c>upgrade-insecure-requests</c>
/// directive of a content security policy upgrades the <c>http:</c> requests of a document (or worker) to <c>https:</c>, before
/// the mixed content checks (the request URL of a <c>ws:</c> WebSocket is its <c>http:</c> URL: it is upgraded to <c>wss:</c>).
/// The navigations are upgraded when they are form submissions, navigations of nested navigables, or navigations to the host and
/// port of a protected resource (the upgrade insecure navigations set). The documents of the nested navigables and the workers
/// inherit the policy of their embedding document or of their creator.
/// </summary>
internal static class UpgradeInsecureRequests
{
    private const string DirectiveName = "upgrade-insecure-requests";

    /// <summary>
    /// https://w3c.github.io/webappsec-upgrade-insecure-requests/#delivery: the initialization of the
    /// <c>upgrade-insecure-requests</c> directive of a policy of <paramref name="document"/> (the protected resource): an
    /// enforced policy sets the insecure requests policy to "Upgrade", and adds the host and port of the URL of the document
    /// to the upgrade insecure navigations set. A report-only policy has no effect (the directive does not report).
    /// </summary>
    public static void Initialize(Document document, ContentSecurityPolicy policy)
    {
        if (policy.ReportOnly || !HasDirective(policy))
            return;

        var url = document.UrlRecord;
        document.SetInsecureRequestsPolicyUpgrade(url.Host is { } host ? [(host.Serialized, url.Port)] : null);
    }

    private static bool HasDirective(ContentSecurityPolicy policy)
    {
        foreach (var (name, _) in policy.Directives)
        {
            if (name == DirectiveName)
                return true;
        }

        return false;
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-upgrade-insecure-requests/#nested-browsing-contexts and
    /// https://w3c.github.io/webappsec-upgrade-insecure-requests/#workers: a document of a nested navigable inherits the policy
    /// and the upgrade insecure navigations set of its embedding document (<paramref name="source"/>), and a worker the ones of
    /// its creator. The specification copies the policy of the embedding document to the nested browsing context when it is
    /// created; as in the browsers, the policy of the embedding document is copied when the document is created (a
    /// <c>meta</c> element added after the creation of an iframe applies to its next documents).
    /// </summary>
    public static void Inherit(Document document, Document? source)
    {
        if (source is { InsecureRequestsPolicy: InsecureRequestsPolicy.Upgrade })
        {
            document.SetInsecureRequestsPolicyUpgrade(source.UpgradeInsecureNavigationsSet);
        }
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-upgrade-insecure-requests/#upgrade-request (https://fetch.spec.whatwg.org/#main-fetch,
    /// step 4), for each URL of a redirect chain: a navigation request gets the <c>Upgrade-Insecure-Requests: 1</c> header
    /// (there is no preloaded HSTS host list: it is always sent); the <c>http:</c> URL of a request is upgraded to <c>https:</c>
    /// when the insecure requests policy of its client is "Upgrade" (a navigation must also be a form submission, the
    /// navigation of a nested navigable, or a navigation to the upgrade insecure navigations set of the client).
    /// </summary>
    public static void UpgradeRequest(FetchRequest request)
    {
        // https://fetch.spec.whatwg.org/#navigation-request
        var isNavigationRequest = request.Destination is RequestDestination.Document or RequestDestination.Embed or RequestDestination.Frame or RequestDestination.Iframe or RequestDestination.Object;
        if (isNavigationRequest && !FetchHeaders.Contains(request.HeaderList, "Upgrade-Insecure-Requests"))
        {
            request.HeaderList.Add(new("Upgrade-Insecure-Requests", "1"));
        }

        var url = request.CurrentUrl;
        if (url.Scheme != "http" || request.Client is not { InsecureRequestsPolicy: InsecureRequestsPolicy.Upgrade } client)
            return;

        // The navigations of the nested navigables are upgraded, and the navigations of a top-level navigable that are form
        // submissions or whose host and port are in the upgrade insecure navigations set
        if (request.Destination == RequestDestination.Document && !request.IsFormSubmission
            && (url.Host is not { } host || client.UpgradeInsecureNavigationsSet is not { } navigations || !navigations.Contains((host.Serialized, url.Port))))
        {
            return;
        }

        request.UrlList[^1] = Upgrade(url);
    }

    /// <summary>
    /// The URL a subresource request of <paramref name="client"/> fetches (see <see cref="UpgradeRequest"/>): the <c>https:</c>
    /// URL of an <c>http:</c> URL when the insecure requests policy of the client is "Upgrade", else <paramref name="url"/>.
    /// </summary>
    public static UrlRecord UpgradeSubresourceUrl(Document client, UrlRecord url)
        => url.Scheme == "http" && client.InsecureRequestsPolicy == InsecureRequestsPolicy.Upgrade ? Upgrade(url) : url;

    private static UrlRecord Upgrade(UrlRecord url)
    {
        // Setting the scheme resets the port when it is the default port of https (443): the other ports are kept
        var upgraded = url.Clone();
        return UrlParser.ParseWithStateOverride("https:", upgraded, UrlParserState.SchemeStart) ? upgraded : url;
    }
}
