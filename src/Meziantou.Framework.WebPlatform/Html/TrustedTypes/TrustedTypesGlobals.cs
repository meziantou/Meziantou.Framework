namespace Meziantou.Framework.WebPlatform.Dom;

public partial class Document
{
    private List<TrustedTypesDirectives>? _trustedTypesDirectives;
    private TrustedTypePolicyFactory? _trustedTypePolicyFactory;
    private OriginRecord? _contentSecurityPolicySelfOrigin;

    /// <summary>
    /// The Trusted Types directives of the content security policies of the document (the CSP list of its policy container),
    /// which enforce Trusted Types for the objects whose relevant global object is the window of the document (or the
    /// document itself when it has no window). A document loaded in a <see cref="BrowsingContext"/> gets the directives of
    /// the <c>Content-Security-Policy</c> and <c>Content-Security-Policy-Report-Only</c> headers of its response
    /// (<see cref="DocumentResource.Headers"/>) and of its <c>&lt;meta http-equiv="Content-Security-Policy"&gt;</c>
    /// elements. Otherwise, it is empty by default: Trusted Types are not enforced. See
    /// <see cref="Dom.TrustedTypesDirectives.Parse"/> to get the directives of a <c>Content-Security-Policy</c> header.
    /// </summary>
    [NotInIdl("The Trusted Types directives of the CSP list of the document, which is not exposed to the scripts")]
    public IList<TrustedTypesDirectives> TrustedTypesDirectives => _trustedTypesDirectives ??= [];

    internal List<TrustedTypesDirectives>? TrustedTypesDirectivesOrNull => _trustedTypesDirectives;

    /// <summary>
    /// The CSP list of the policy container of the document (https://w3c.github.io/webappsec-csp/#csp-list), whose fetch
    /// directives block the requests of the subresources (see <see cref="ContentSecurityPolicy.ShouldBlockRequest(FetchRequest)"/>).
    /// </summary>
    internal List<ContentSecurityPolicy>? ContentSecurityPoliciesOrNull { get; private set; }

    /// <summary>
    /// The self-origin of the CSP list (https://w3c.github.io/webappsec-csp/#csp-list-self-origin), which the
    /// <c>'self'</c> source expression matches: the origin of the URL of the response of a document loaded by a
    /// <see cref="BrowsingContext"/> (https://w3c.github.io/webappsec-csp/#parse-response-csp), even when the document has
    /// an opaque origin because it is sandboxed; else the origin of the document.
    /// </summary>
    internal OriginRecord ContentSecurityPolicySelfOrigin
    {
        get => _contentSecurityPolicySelfOrigin ?? Origin;
        set => _contentSecurityPolicySelfOrigin = value;
    }

    /// <summary>
    /// The required CSP of the policy container of the document (https://w3c.github.io/webappsec-cspee/#policy-container-required-csp):
    /// the serialized policy required by the <c>csp</c> attribute of the iframe element of an ancestor, which the documents of
    /// the child navigables of the document must enforce too, or null.
    /// </summary>
    internal string? RequiredContentSecurityPolicy { get; set; }

    /// <summary>
    /// Adds a serialized content security policy, or a list of policies separated by commas (the value of a
    /// <c>Content-Security-Policy</c> header, https://w3c.github.io/webappsec-csp/#parse-response-csp). The policy of a
    /// <c>meta</c> element is a single policy. The policies without Trusted Types directive are not added to
    /// <see cref="TrustedTypesDirectives"/>.
    /// </summary>
    internal void AddContentSecurityPolicy(string serializedPolicyList, bool reportOnly, bool fromMeta = false)
    {
        foreach (var serializedPolicy in fromMeta ? [serializedPolicyList] : serializedPolicyList.Split(','))
        {
            var policy = ContentSecurityPolicy.Parse(serializedPolicy, reportOnly, fromMeta);
            AddContentSecurityPolicy(policy);
            var directives = Dom.TrustedTypesDirectives.Parse(serializedPolicy, reportOnly);
            if (directives.RequireTrustedTypesForScript || directives.TrustedTypes is not null)
            {
                directives.Policy = policy;
                TrustedTypesDirectives.Add(directives);
            }
        }
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#parse-response-csp: adds the policies of the <c>Content-Security-Policy</c>
    /// headers of a response, then the ones of its <c>Content-Security-Policy-Report-Only</c> headers.
    /// </summary>
    internal void AddContentSecurityPolicies(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        foreach (var reportOnly in (ReadOnlySpan<bool>)[false, true])
        {
            var headerName = reportOnly ? "Content-Security-Policy-Report-Only" : "Content-Security-Policy";
            foreach (var (name, value) in headers)
            {
                if (!Infra.InfraAscii.EqualsIgnoreAsciiCase(name, headerName))
                    continue;

                AddContentSecurityPolicy(value, reportOnly);
            }
        }
    }

    /// <summary>
    /// Sets the policies of the document to the ones of an inherited policy container (a clone of the policy container of the
    /// initiator, the parent or the creator of the document, https://html.spec.whatwg.org/multipage/browsers.html#determining-navigation-params-policy-container):
    /// its CSP list, the self-origin of its CSP list and its Trusted Types directives. The policies of the <c>meta</c>
    /// elements of the document are added to them.
    /// </summary>
    internal void SetPolicyContainer(PolicyContainer policyContainer)
    {
        ContentSecurityPoliciesOrNull = policyContainer.ContentSecurityPolicies is { } policies ? [.. policies] : null;
        _trustedTypesDirectives = policyContainer.TrustedTypesDirectives is { } directives ? [.. directives] : null;
        RequiredContentSecurityPolicy = policyContainer.RequiredContentSecurityPolicy;
        if (policyContainer.ContentSecurityPolicySelfOrigin is { } selfOrigin)
        {
            ContentSecurityPolicySelfOrigin = selfOrigin;
        }

        // https://w3c.github.io/webappsec-csp/#run-document-csp-initialization: the initialization of the directives of the
        // inherited policies (e.g. the upgrade-insecure-requests directive of the policies of the opener of a popup)
        if (ContentSecurityPoliciesOrNull is { } inheritedPolicies)
        {
            foreach (var policy in inheritedPolicies)
            {
                UpgradeInsecureRequests.Initialize(this, policy);
            }
        }
    }

    internal void AddContentSecurityPolicy(ContentSecurityPolicy policy)
    {
        if (policy.Directives.Count > 0)
        {
            (ContentSecurityPoliciesOrNull ??= []).Add(policy);
            UpgradeInsecureRequests.Initialize(this, policy);
        }
    }

    /// <summary>
    /// The trusted type policy factory of the relevant global object of the document
    /// (https://w3c.github.io/trusted-types/dist/spec/#dom-windoworworkerglobalscope-trustedtypes): the one of its window
    /// (<see cref="Window.TrustedTypes"/>), or of the window of the document that created it. A document without a window is
    /// its own global object.
    /// </summary>
    [NotInIdl("The trustedTypes attribute of the global object, for the documents that have no window")]
    public TrustedTypePolicyFactory TrustedTypes => TrustedTypePolicyFactory.Get(this);

    /// <summary>The trusted type policy factory of a document that has no window.</summary>
    internal TrustedTypePolicyFactory OwnTrustedTypePolicyFactory => _trustedTypePolicyFactory ??= new TrustedTypePolicyFactory(this);
}

public sealed partial class Window
{
    private TrustedTypePolicyFactory? _trustedTypes;

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#dom-windoworworkerglobalscope-trustedtypes</summary>
    /// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#dom-windoworworkerglobalscope-trustedtypes"/>
    [IdlMember("trustedTypes", "https://w3c.github.io/trusted-types/dist/spec/#dom-windoworworkerglobalscope-trustedtypes")]
    public TrustedTypePolicyFactory TrustedTypes => _trustedTypes ?? CreateTrustedTypes();

    private TrustedTypePolicyFactory CreateTrustedTypes()
    {
        _trustedTypes = new TrustedTypePolicyFactory(this);
        ScriptEngine?.OnTrustedTypePolicyFactoryCreated(this);
        return _trustedTypes;
    }
}

public abstract partial class WorkerGlobalScope
{
    private TrustedTypePolicyFactory? _trustedTypes;

    /// <summary>
    /// https://w3c.github.io/trusted-types/dist/spec/#dom-windoworworkerglobalscope-trustedtypes: the trusted type policy
    /// factory of the worker. Trusted Types are enforced by the <c>require-trusted-types-for</c> and <c>trusted-types</c>
    /// directives of the CSP list of the worker (the policies of the response of its script, or the ones of its creator for a
    /// worker whose URL is a local scheme).
    /// </summary>
    /// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#dom-windoworworkerglobalscope-trustedtypes"/>
    [IdlMember("trustedTypes", "https://w3c.github.io/trusted-types/dist/spec/#dom-windoworworkerglobalscope-trustedtypes")]
    public TrustedTypePolicyFactory TrustedTypes => _trustedTypes ?? CreateTrustedTypes();

    private TrustedTypePolicyFactory CreateTrustedTypes()
    {
        _trustedTypes = new TrustedTypePolicyFactory(EnvironmentDocument);
        ScriptEngine?.OnTrustedTypePolicyFactoryCreated(this);
        return _trustedTypes;
    }
}
