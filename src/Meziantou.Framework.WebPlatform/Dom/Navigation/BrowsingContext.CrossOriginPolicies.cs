using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A browsing context (https://html.spec.whatwg.org/multipage/document-sequences.html#browsing-context) of a navigable: the
/// navigable keeps it until a navigation whose opener policy requires a browsing context group switch replaces it by a new
/// one. The WindowProxy objects are the ones of a browsing context: the one of a replaced browsing context keeps its last
/// window, whose browsing context is discarded (e.g. the popup appears closed to its opener).
/// </summary>
internal sealed class BrowsingContextInstance
{
    private BrowsingContext _navigable;
    private Window? _discardedWindow;

    public BrowsingContextInstance(BrowsingContext navigable)
    {
        _navigable = navigable;
    }

    /// <summary>Whether a browsing context group switch replaced this browsing context.</summary>
    public bool IsDiscarded { get; private set; }

    /// <summary>The active window of the browsing context, or its last window when it was replaced.</summary>
    public Window? ActiveWindow => IsDiscarded ? _discardedWindow : _navigable.Document?.RelevantWindow;

    /// <summary>The browsing context of a prerendering traversable becomes the browsing context of the navigable that activates its document.</summary>
    internal void Retarget(BrowsingContext navigable) => _navigable = navigable;

    internal void Discard(Window? window)
    {
        IsDiscarded = true;
        _discardedWindow = window;
    }
}

public sealed partial class BrowsingContext
{
    private BrowsingContextGroup? _group;

    /// <summary>The browsing context that is active in the navigable (https://html.spec.whatwg.org/multipage/document-sequences.html#nav-bc).</summary>
    internal BrowsingContextInstance ActiveBrowsingContext { get => field ??= new BrowsingContextInstance(this); private set; }

    /// <summary>The browsing context group of the top-level browsing context (https://html.spec.whatwg.org/multipage/document-sequences.html#tlbc-group).</summary>
    internal BrowsingContextGroup Group => Parent is null ? _group ??= new BrowsingContextGroup() : Top.Group;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsers.html#obtain-browsing-context-navigation: the navigation of a top-level
    /// navigable whose opener policy requires it continues in a new top-level browsing context, in a new browsing context
    /// group: it has no opener and no name, and the WindowProxy of the previous browsing context keeps its last window.
    /// </summary>
    private void SwitchBrowsingContextGroup(OpenerPolicy navigationCoop)
    {
        ActiveBrowsingContext.Discard(Document?.RelevantWindow);
        ActiveBrowsingContext = new BrowsingContextInstance(this);
        _group = new BrowsingContextGroup
        {
            CrossOriginIsolationMode = navigationCoop.Value == OpenerPolicyValue.SameOriginPlusCoep ? CrossOriginIsolationMode.Concrete : CrossOriginIsolationMode.None,
        };

        Opener = null;
        Name = "";

        // The opener browsing context of the popups opened by the documents of the navigable is the discarded browsing context
        foreach (var topLevel in GetGroupTopLevelBrowsingContexts())
        {
            if (topLevel.Opener is { } opener && ReferenceEquals(opener.Top, this))
            {
                topLevel.Opener = null;
            }
        }
    }

    /// <summary>
    /// Whether the navigable belongs to another browsing context group than this one, and cannot be found by its name
    /// (https://html.spec.whatwg.org/multipage/document-sequences.html#find-a-navigable-by-target-name).
    /// </summary>
    private bool IsInOtherGroup(BrowsingContext navigable) => !ReferenceEquals(navigable.Group, Group);

    /// <summary>
    /// The final sandboxing flag set of a navigation response (https://html.spec.whatwg.org/multipage/browsing-the-web.html#create-navigation-params-by-fetching):
    /// the sandboxing flags of the navigable and the ones of the CSP of the response.
    /// </summary>
    private SandboxingFlags GetFinalSandboxingFlags(IReadOnlyList<KeyValuePair<string, string>> headers, SandboxingFlags? targetSandboxingFlags)
    {
        var sandboxingFlags = targetSandboxingFlags ?? GetIframeSandboxingFlags();
        foreach (var (name, value) in headers)
        {
            if (Infra.InfraAscii.EqualsIgnoreAsciiCase(name, "Content-Security-Policy"))
            {
                sandboxingFlags |= Sandboxing.ParseContentSecurityPolicy(value);
            }
        }

        return sandboxingFlags;
    }

    /// <summary>
    /// The opener policy of the about:blank and same-origin blob: documents: the one of the top-level document of their
    /// initiator when it is same origin with it, as for the initial about:blank documents (the browsers do it, the
    /// specification does not say it yet).
    /// </summary>
    private static void InheritOpenerPolicy(Document? sourceDocument, DocumentParameters parameters)
    {
        if (sourceDocument?.BrowsingContext is { } sourceNavigable && OriginRecord.IsSameOrigin(sourceDocument.Origin, sourceDocument.TopLevelOrigin))
        {
            parameters.OpenerPolicy = sourceNavigable.Top.Document.OpenerPolicy;
        }
    }

    /// <summary>
    /// The opener policy of a navigation response of a top-level navigable
    /// (https://html.spec.whatwg.org/multipage/browsing-the-web.html#create-navigation-params-by-fetching, the steps of the
    /// opener policy): the policy of the response and the enforcement result, which tells whether the navigation switches
    /// browsing context group. Returns false when the response is a network error: a sandboxed document cannot have an
    /// opener policy.
    /// </summary>
    private bool ApplyOpenerPolicy(UrlRecord url, DocumentParameters parameters, IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        if (Parent is not null)
            return true;

        var sandboxingFlags = GetFinalSandboxingFlags(headers, parameters.TargetSandboxingFlags);
        OriginRecord responseOrigin;
        if (parameters.OpaqueOrigin || (sandboxingFlags & SandboxingFlags.Origin) != SandboxingFlags.None)
        {
            responseOrigin = OriginRecord.CreateOpaque();
        }
        else
        {
            responseOrigin = parameters.OriginDocument?.Origin ?? OriginRecord.FromUrl(url);
        }

        // The policy is enforced for each response of a navigation, including the redirects
        var activeDocument = Document;
        var responseCoop = parameters.OpenerPolicy ?? OpenerPolicy.Obtain(headers, ReferrerPolicies.IsPotentiallyTrustworthy(url));
        var enforcementResult = parameters.OpenerPolicyEnforcementResult ?? new OpenerPolicyEnforcementResult(activeDocument.UrlRecord, activeDocument.Origin, activeDocument.OpenerPolicy);
        parameters.OpenerPolicy = responseCoop;
        parameters.OpenerPolicyEnforcementResult = enforcementResult.Enforce(activeDocument.IsInitialAboutBlank, url, responseOrigin, responseCoop);

        // The sandboxing flags of the navigable (e.g. inherited from a sandboxed opener) and an opener policy cannot be
        // combined. The browsers do not apply the check to the flags of the CSP of the response, whose document has an
        // opaque origin (and its opener policy switches the browsing context group).
        return GetIframeSandboxingFlags() == SandboxingFlags.None || responseCoop.Value == OpenerPolicyValue.UnsafeNone;
    }

    /// <summary>The value of the <c>Document-Isolation-Policy</c> header of a response (see <see cref="Document.DocumentIsolationPolicy"/>).</summary>
    internal static EmbedderPolicyValue GetDocumentIsolationPolicy(IReadOnlyList<KeyValuePair<string, string>> headers, bool isSecureContext)
    {
        if (!isSecureContext)
            return EmbedderPolicyValue.UnsafeNone;

        return (Infra.StructuredFields.GetItem(headers, "Document-Isolation-Policy")?.Value as Infra.StructuredFieldToken?)?.Value switch
        {
            "isolate-and-require-corp" => EmbedderPolicyValue.RequireCorp,
            "isolate-and-credentialless" => EmbedderPolicyValue.Credentialless,
            _ => EmbedderPolicyValue.UnsafeNone,
        };
    }

    /// <summary>The cookie jar of the requests of the documents of the navigable (see <see cref="Document.CookieJar"/>).</summary>
    internal CookieJar? GetCookieJar(bool credentialless)
    {
        if (Options.CookieJar is null)
            return null;

        return credentialless ? Top.Document.CredentiallessCookieJar : Options.CookieJar;
    }

    /// <summary>Whether the documents of the navigable are credentialless: its container is a credentialless iframe, or its parent's document is credentialless.</summary>
    internal bool IsCredentiallessNavigable => Container is HtmlIFrameElement { Credentialless: true } || (Container?.NodeDocument.IsCredentialless ?? false);

    /// <summary>
    /// Whether the response of a navigation of a child navigable is blocked by the embedder policy of its container document:
    /// the cross-origin resource policy check (<see cref="IsBlockedByResourcePolicy"/>, unless <paramref name="checkResourcePolicy"/>
    /// is false: the fetch of the navigation checks each response), and https://html.spec.whatwg.org/multipage/browsers.html#check-a-navigation-response's-adherence-to-its-embedder-policy:
    /// a document whose embedder policy requires it only embeds documents that have an embedder policy.
    /// </summary>
    private bool IsBlockedByEmbedderPolicy(UrlRecord url, IReadOnlyList<KeyValuePair<string, string>> headers, bool checkResourcePolicy = true)
    {
        if (Parent is null || Container?.NodeDocument is not { } containerDocument)
            return false;

        // A credentialless iframe (https://wicg.github.io/anonymous-iframe/) loads its documents without credentials: the
        // embedder policy of its container document does not apply to them
        if (Container is HtmlIFrameElement { Credentialless: true })
            return false;

        if (checkResourcePolicy && IsBlockedByResourcePolicy(url, headers))
            return true;

        // The embedder policy of the response (its document is in a secure context, as its container document); the violations
        // of the report-only value of the embedder policy of the container document are reported
        var parentPolicy = containerDocument.EmbedderPolicy;
        if (parentPolicy.Value == EmbedderPolicyValue.UnsafeNone && parentPolicy.ReportOnlyValue == EmbedderPolicyValue.UnsafeNone)
            return false;

        var responsePolicy = EmbedderPolicy.Obtain(headers, isSecureContext: ReferrerPolicies.IsPotentiallyTrustworthy(url));
        if (EmbedderPolicy.IsCompatibleWithCrossOriginIsolation(parentPolicy.ReportOnlyValue) && !EmbedderPolicy.IsCompatibleWithCrossOriginIsolation(responsePolicy.Value))
        {
            EmbedderPolicy.QueueInheritanceViolation(url, "navigation", parentPolicy.ReportOnlyReportingEndpoint, "reporting", containerDocument);
        }

        if (!EmbedderPolicy.IsCompatibleWithCrossOriginIsolation(parentPolicy.Value) || EmbedderPolicy.IsCompatibleWithCrossOriginIsolation(responsePolicy.Value))
            return false;

        EmbedderPolicy.QueueInheritanceViolation(url, "navigation", parentPolicy.ReportingEndpoint, "enforce", containerDocument);
        return true;
    }

    /// <summary>
    /// Whether a response of a navigation of a child navigable (a redirect, or the final response) is blocked by the cross-origin
    /// resource policy check of its container document (https://html.spec.whatwg.org/multipage/browsing-the-web.html#create-navigation-params-by-fetching,
    /// step 19.6.4). The embedder policy of the container document of a credentialless iframe does not apply.
    /// </summary>
    private bool IsBlockedByResourcePolicy(UrlRecord url, IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        if (Parent is null || Container?.NodeDocument is not { } containerDocument || Container is HtmlIFrameElement { Credentialless: true })
            return false;

        return url.Scheme is "http" or "https" && CrossOriginResourcePolicy.IsBlocked(containerDocument, containerDocument.EmbedderPolicy.Value, FetchMetadata.GetDestinationName(GetNavigationDestination()), url, url, headers, forNavigation: true, requestIncludesCredentials: true);
    }
}
