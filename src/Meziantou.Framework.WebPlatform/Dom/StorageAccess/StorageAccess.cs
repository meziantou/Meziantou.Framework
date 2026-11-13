using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://privacycg.github.io/storage-access/#storage-access-eligibility: whether a request can use the storage access of its client.</summary>
internal enum StorageAccessEligibility
{
    /// <summary>The request is not made from a cross-site context: its cookies do not depend on the storage access.</summary>
    Unset,

    /// <summary>The request cannot use the storage access of its client.</summary>
    Ineligible,

    /// <summary>The request can use the storage access of its client (a same-origin request of a client that has storage access).</summary>
    Eligible,
}

/// <summary>https://privacycg.github.io/storage-access-headers/#storage-access-status: the value of the <c>Sec-Fetch-Storage-Access</c> header.</summary>
internal enum StorageAccessStatus
{
    /// <summary>The request has no access to the unpartitioned cookies.</summary>
    None,

    /// <summary>The request has no access to the unpartitioned cookies, but the <c>storage-access</c> permission is granted.</summary>
    Inactive,

    /// <summary>The request has access to the unpartitioned cookies.</summary>
    Active,
}

/// <summary>
/// The algorithms of the Storage Access API (https://privacycg.github.io/storage-access/) and of the Storage Access Headers
/// (https://privacycg.github.io/storage-access-headers/): the <c>storage-access</c> permission, keyed by the sites of the
/// top-level document and of the embedded document, the "has storage access" bit of the environments
/// (<see cref="Document.EnvironmentHasStorageAccess"/>), and the storage-access eligibility of the requests. The
/// unpartitioned cookies of a third-party context are only blocked when the cookie jar blocks them
/// (<see cref="CookieJar.BlockThirdPartyCookies"/>, <see cref="CookieJar.SetThirdPartyCookieAccess"/>).
/// </summary>
internal static class StorageAccess
{
    /// <summary>The name of the powerful feature and of the policy-controlled feature (https://privacycg.github.io/storage-access/#permissions-integration).</summary>
    public const string FeatureName = "storage-access";

    /// <summary>
    /// https://privacycg.github.io/storage-access/#permissions-integration: the permission key of the <c>storage-access</c>
    /// feature, the site of the top-level origin and the site of the embedded origin, serialized as the key of the
    /// <see cref="PermissionStore"/>.
    /// </summary>
    public static string GetPermissionKey(OriginRecord topLevel, OriginRecord embedded) => topLevel.SerializeSite() + " " + embedded.SerializeSite();

    /// <summary>The permission store of a document, or null when it has no browsing context.</summary>
    private static PermissionStore? GetPermissionStore(Document document) => document.EnvironmentOptions?.EffectivePermissions;

    /// <summary>
    /// Whether the storage access of an embedded origin is granted for a top-level origin: the <c>storage-access</c>
    /// permission is granted, or the origins are same site (<see cref="Document.RequestStorageAccess"/> grants it without
    /// asking the user, e.g. a frame of the top-level site with a cross-site ancestor).
    /// </summary>
    public static bool IsGranted(PermissionStore? store, OriginRecord topLevel, OriginRecord embedded)
        => OriginRecord.IsSameSite(topLevel, embedded) || GetPermissionState(store, topLevel, embedded) == PermissionState.Granted;

    /// <summary>The state of the <c>storage-access</c> permission for a top-level origin and an embedded origin.</summary>
    public static PermissionState GetPermissionState(PermissionStore? store, OriginRecord topLevel, OriginRecord embedded)
    {
        if (store is null || topLevel.IsOpaque || embedded.IsOpaque)
            return PermissionState.Prompt;

        return store.GetState(FeatureName, GetPermissionKey(topLevel, embedded));
    }

    /// <summary>
    /// Whether the scripts of a document (<c>document.cookie</c>, <c>cookieStore</c>) have access to the unpartitioned
    /// cookies through the Storage Access API: the environment of the document has storage access, and the permission is
    /// still granted (a revocation of the permission takes effect immediately).
    /// </summary>
    public static bool HasUnpartitionedCookieAccess(Document document)
    {
        if (!document.EnvironmentHasStorageAccess)
            return false;

        return IsGranted(GetPermissionStore(document), CookieContexts.GetTopLevelOrigin(document), document.Origin);
    }

    /// <summary>
    /// The "first-party-site context" of a document (https://privacycg.github.io/storage-access/#first-party-site-context):
    /// the active document of a top-level browsing context, or a document whose origin is same site with the top-level origin.
    /// As the cookies, a document that has a cross-site ancestor (A embeds B, which embeds A) is in a third-party context.
    /// </summary>
    public static bool IsFirstPartySiteContext(Document document)
        => document.BrowsingContext is { Parent: null } || !CookieContexts.GetSiteForCookies(document).IsOpaque;

    /// <summary>https://privacycg.github.io/storage-access/#determine-the-initial-storage-access-eligibility</summary>
    public static StorageAccessEligibility DetermineInitialEligibility(FetchRequest request)
    {
        // The ancestry of the client (https://github.com/whatwg/html/pull/11133): the requests of a client that has no
        // cross-site ancestor do not depend on the storage access
        if (request.Client is not { } client || !CookieContexts.GetSiteForCookies(client).IsOpaque)
            return StorageAccessEligibility.Unset;

        if (!client.EnvironmentHasStorageAccess)
            return StorageAccessEligibility.Ineligible;

        if (request.Origin != request.Url.SerializeOrigin())
            return StorageAccessEligibility.Ineligible;

        if (!ShouldRequestBeAllowedToUseFeature(request))
            return StorageAccessEligibility.Ineligible;

        return StorageAccessEligibility.Eligible;
    }

    /// <summary>
    /// https://privacycg.github.io/storage-access/#http-redirect-fetch: a redirect to another origin makes the request
    /// ineligible for storage access.
    /// </summary>
    public static void ProcessRedirect(FetchRequest request, UrlRecord locationUrl)
    {
        if (request.StorageAccessEligibility != StorageAccessEligibility.Unset && locationUrl.SerializeOrigin() != request.CurrentUrl.SerializeOrigin())
        {
            request.StorageAccessEligibility = StorageAccessEligibility.Ineligible;
        }
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-permissions-policy/#algo-should-request-be-allowed-to-use-feature for the
    /// <c>storage-access</c> feature: the policy of the client for the origin of the request, or for the navigation of an
    /// iframe, the inherited policy of a document of its origin in the iframe. The requests of a worker (which has no
    /// window) are not allowed.
    /// </summary>
    private static bool ShouldRequestBeAllowedToUseFeature(FetchRequest request)
    {
        var feature = PolicyControlledFeature.StorageAccess;
        if (request.Mode == RequestMode.Navigate && request.Navigable?.Container is { } container)
            return PermissionsPolicyAlgorithms.DefineInheritedPolicy(feature, container, OriginRecord.FromUrl(request.CurrentUrl));

        if (request.Client is not { WorkerGlobal: null } document)
            return false;

        var origin = request.Origin == document.SerializeOrigin() ? document.Origin : OriginRecord.FromUrl(request.CurrentUrl);
        return PermissionsPolicyAlgorithms.IsFeatureEnabled(feature, document, origin, report: false);
    }

    /// <summary>
    /// Whether the unpartitioned cookies of the current URL of a request are available
    /// (https://privacycg.github.io/storage-access-headers/#determine-whether-the-user-agents-cookie-store-allows-unpartitioned-cookies-to-be-accessed):
    /// the user explicitly allows them, or the request is eligible for storage access and the permission is granted (see
    /// <see cref="IsGranted"/>).
    /// </summary>
    private static bool AllowsUnpartitionedCookies(CookieJar? cookieJar, PermissionStore? permissions, OriginRecord topLevel, OriginRecord destination, StorageAccessEligibility eligibility)
    {
        if (cookieJar?.ExplicitlyAllowsUnpartitionedCookieAccess(topLevel, destination) == true)
            return true;

        return eligibility == StorageAccessEligibility.Eligible && IsGranted(permissions, topLevel, destination);
    }

    /// <summary>
    /// Whether a request has storage access for its current URL: it is eligible for storage access and the permission is
    /// granted for the site of the URL (its unpartitioned cookies are then sent and stored even when the jar blocks the
    /// third-party cookies).
    /// </summary>
    public static bool HasStorageAccess(StorageAccessEligibility eligibility, PermissionStore? permissions, OriginRecord? topLevel, UrlRecord url)
        => eligibility == StorageAccessEligibility.Eligible && topLevel is not null && IsGranted(permissions, topLevel, OriginRecord.FromUrl(url));

    /// <summary>
    /// https://privacycg.github.io/storage-access-headers/#request-storage-access-status: the storage access status of a
    /// request whose cookie context is <paramref name="context"/>, or null when the request is not made from a third-party
    /// context (the cookie store would attach its <c>SameSite=Strict</c> cookies, or it is a top-level navigation, whose
    /// site for cookies is its URL).
    /// </summary>
    public static StorageAccessStatus? GetStatus(FetchRequest request, CookieRequestContext? context, CookieJar? cookieJar, PermissionStore? permissions)
    {
        if (context is not { } cookieContext || cookieContext.IsTopLevelNavigation || cookieContext.SameSite == CookieSameSiteStatus.Strict || cookieContext.TopLevelOrigin is not { } topLevel)
            return null;

        var destination = OriginRecord.FromUrl(request.CurrentUrl);
        if (AllowsUnpartitionedCookies(cookieJar, permissions, topLevel, destination, request.StorageAccessEligibility))
            return StorageAccessStatus.Active;

        if (request.StorageAccessEligibility == StorageAccessEligibility.Eligible)
            return StorageAccessStatus.None;

        if (!ShouldRequestBeAllowedToUseFeature(request))
            return StorageAccessStatus.None;

        return AllowsUnpartitionedCookies(cookieJar, permissions, topLevel, destination, StorageAccessEligibility.Eligible) ? StorageAccessStatus.Inactive : StorageAccessStatus.None;
    }

    /// <summary>The token of a storage access status.</summary>
    public static string Serialize(StorageAccessStatus status) => status switch
    {
        StorageAccessStatus.Inactive => "inactive",
        StorageAccessStatus.Active => "active",
        _ => "none",
    };

    /// <summary>
    /// https://privacycg.github.io/storage-access-headers/#perform-a-storage-access-retry-check: whether the response asks
    /// to retry the request with its unpartitioned cookies (<c>Activate-Storage-Access: retry; allowed-origin=...</c>).
    /// </summary>
    public static bool PerformRetryCheck(FetchRequest request, StorageAccessStatus? status, IReadOnlyList<KeyValuePair<string, string>> responseHeaders)
    {
        if (request.CredentialsMode != FetchCredentialsMode.Include || request.StorageAccessEligibility == StorageAccessEligibility.Eligible || status != StorageAccessStatus.Inactive)
            return false;

        if (StructuredFields.GetItem(responseHeaders, "Activate-Storage-Access") is not { Value: StructuredFieldToken { Value: "retry" } } item)
            return false;

        return item.GetParameter("allowed-origin") switch
        {
            StructuredFieldToken { Value: "*" } => true,
            string allowedOrigin => allowedOrigin == request.Origin,
            _ => false,
        };
    }

    /// <summary>
    /// https://privacycg.github.io/storage-access-headers/#perform-a-storage-access-load-check: whether the response of a
    /// navigation asks to load the document with storage access (<c>Activate-Storage-Access: load</c>).
    /// </summary>
    public static bool PerformLoadCheck(StorageAccessStatus? status, IReadOnlyList<KeyValuePair<string, string>> responseHeaders)
    {
        if (status is not (StorageAccessStatus.Inactive or StorageAccessStatus.Active))
            return false;

        return StructuredFields.GetItem(responseHeaders, "Activate-Storage-Access") is { Value: StructuredFieldToken { Value: "load" } };
    }
}
