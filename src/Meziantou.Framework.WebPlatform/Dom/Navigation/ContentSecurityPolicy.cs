using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A content security policy (https://w3c.github.io/webappsec-csp/#content-security-policy-object), and the fetch
/// directives (https://w3c.github.io/webappsec-csp/#directives-fetch) that block the requests of the subresources of a
/// document (<see cref="ShouldBlockRequest(FetchRequest)"/>). The violations are reported by
/// <see cref="ContentSecurityPolicyViolation.Report"/> (the <c>securitypolicyviolation</c> event and the <c>report-uri</c>
/// reports); the policies whose disposition is "report" (<c>Content-Security-Policy-Report-Only</c>) report their
/// violations and block nothing. The inline scripts and styles, the event handler content attributes, the <c>style</c>
/// attributes and the <c>javascript:</c> URL navigations are checked by <see cref="ShouldBlockInline(Element, ContentSecurityPolicyInlineType, string, bool)"/>.
/// The navigations are checked by the fetch directives of their request (<c>frame-src</c>, <c>object-src</c>), by the
/// pre-navigation checks (<see cref="ShouldBlockNavigationRequest"/>, <c>form-action</c>), and by the navigation response
/// checks (<see cref="ShouldBlockNavigationResponse"/>, <c>frame-ancestors</c>); the <c>base-uri</c> directive by
/// <see cref="IsBaseAllowed"/>. The Trusted Types directives are handled by <see cref="TrustedTypesDirectives"/>, and the
/// required policies of CSP Embedded Enforcement (the <c>csp</c> attribute of the iframe elements) by
/// <see cref="IsValidRequiredPolicy"/>, <see cref="IsResponseBlockedByRequiredPolicy"/> and <see cref="Subsumes"/>.
/// </summary>
internal sealed partial class ContentSecurityPolicy
{
    /// <summary>A cryptographic nonce metadata that matches any nonce-source (see <see cref="Scripting.ModuleFetchOptions.IsUnknownReferrer"/>).</summary>
    public const string AnyNonce = "\0any";

    private ContentSecurityPolicy(List<(string Name, List<string> Value)> directives, bool reportOnly, bool fromMeta, string serialized)
    {
        Directives = directives;
        ReportOnly = reportOnly;
        IsHeaderDelivered = !fromMeta;
        Serialized = serialized;
    }

    /// <summary>The directive set: the name (lowercase) and the value (a list of tokens) of each directive.</summary>
    public IReadOnlyList<(string Name, List<string> Value)> Directives { get; }

    /// <summary>Whether the disposition is "report": the policy does not block anything, its violations are only reported.</summary>
    public bool ReportOnly { get; }

    /// <summary>
    /// Whether the source of the policy is "header" (https://w3c.github.io/webappsec-csp/#content-security-policy-object-source):
    /// it was delivered by a <c>Content-Security-Policy</c> or <c>Content-Security-Policy-Report-Only</c> header, not by a
    /// <c>meta</c> element.
    /// </summary>
    public bool IsHeaderDelivered { get; }

    /// <summary>https://w3c.github.io/webappsec-csp/#content-security-policy-object-disposition</summary>
    public SecurityPolicyViolationEventDisposition Disposition => ReportOnly ? SecurityPolicyViolationEventDisposition.Report : SecurityPolicyViolationEventDisposition.Enforce;

    /// <summary>
    /// The serialization of the policy (the <c>originalPolicy</c> of the violation reports): the serialized policy as it was
    /// delivered, without its leading and trailing whitespace, as in the browsers (the specification does not define it).
    /// </summary>
    public string Serialized { get; }

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#parse-serialized-policy. A policy delivered by a <c>meta</c> element ignores the
    /// <c>report-uri</c>, <c>frame-ancestors</c> and <c>sandbox</c> directives
    /// (https://html.spec.whatwg.org/multipage/semantics.html#attr-meta-http-equiv-content-security-policy).
    /// </summary>
    public static ContentSecurityPolicy Parse(string serialized, bool reportOnly, bool fromMeta)
    {
        var directives = new List<(string Name, List<string> Value)>();
        foreach (var rawToken in serialized.Split(';'))
        {
            var token = InfraAscii.StripLeadingAndTrailingAsciiWhitespace(rawToken).ToString();
            if (token.Length == 0 || !Ascii.IsValid(token))
                continue;

            // As in the browsers, a directive that contains a character that the grammar does not allow in a directive name or
            // value (https://w3c.github.io/webappsec-csp/#grammardef-serialized-policy: a control character, e.g. U+000B VT,
            // which is not ASCII whitespace) is ignored
            var values = InfraString.SplitOnAsciiWhitespace(token);
            if (values.Exists(value => value.AsSpan().ContainsAnyInRange('\0', ' ') || value.Contains('\x7F', StringComparison.Ordinal)))
                continue;

            var name = InfraAscii.ToAsciiLowercase(values[0]);

            // The first directive with a given name is used
            if (directives.Exists(directive => directive.Name == name))
                continue;

            if (fromMeta && name is "report-uri" or "frame-ancestors" or "sandbox")
                continue;

            values.RemoveAt(0);
            directives.Add((name, values));
        }

        return new ContentSecurityPolicy(directives, reportOnly, fromMeta, InfraAscii.StripLeadingAndTrailingAsciiWhitespace(serialized).ToString());
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#report-for-request (https://fetch.spec.whatwg.org/#main-fetch, step 4): reports the
    /// violations of the fetch directives of the report-only policies of the CSP list of the client of a request (or of its
    /// <see cref="FetchRequest.PolicyClient"/>). It runs before the request is upgraded by the <c>upgrade-insecure-requests</c>
    /// directive and by Mixed Content, so the report-only policies check its <c>http:</c> URL. The resource of the violations
    /// is the URL of the request, not its current URL (the target of a redirect is not exposed,
    /// https://w3c.github.io/webappsec-csp/#create-violation-for-request).
    /// </summary>
    public static void ReportRequestViolations(FetchRequest request)
    {
        if ((request.PolicyClient ?? request.Client) is not { ContentSecurityPoliciesOrNull: { } policies } client)
            return;

        ShouldBlockRequest(policies, client.ContentSecurityPolicySelfOrigin, request.CurrentUrl, request.Destination, request.Initiator, request.ParserInserted, request.RedirectCount, request.Nonce, client, request.Url, SecurityPolicyViolationEventDisposition.Report, request.IsPrefetch);
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#should-block-request (https://fetch.spec.whatwg.org/#main-fetch, step 7): whether
    /// the current URL of a request is blocked by the fetch directives of an enforced policy of the CSP list of its client (a
    /// network error), or of its <see cref="FetchRequest.PolicyClient"/>. The violations of the enforced policies are reported
    /// (the report-only policies are checked by <see cref="ReportRequestViolations"/>); their resource is the URL of the
    /// request, not its current URL.
    /// </summary>
    public static bool ShouldBlockRequest(FetchRequest request)
    {
        if ((request.PolicyClient ?? request.Client) is not { ContentSecurityPoliciesOrNull: { } policies } client)
            return false;

        return ShouldBlockRequest(policies, client.ContentSecurityPolicySelfOrigin, request.CurrentUrl, request.Destination, request.Initiator, request.ParserInserted, request.RedirectCount, request.Nonce, client, request.Url, SecurityPolicyViolationEventDisposition.Enforce, request.IsPrefetch);
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#should-block-request: whether the request of a subresource of a document is
    /// blocked by the fetch directives of an enforced policy of <paramref name="policyDocument"/> (a network error), before
    /// it is fetched (e.g. <c>navigator.sendBeacon()</c>). The violations are reported when
    /// <paramref name="reportViolations"/> is true (see <see cref="ShouldBlockRequest(FetchRequest)"/>).
    /// <paramref name="initiator"/> is the element that fetches the resource: its nonce and its <c>integrity</c> attribute
    /// are the cryptographic nonce metadata and the integrity metadata of the request. <paramref name="redirectCount"/> is
    /// nonzero when the URL is the one of a redirect (the paths of the source expressions are ignored). <paramref name="nonce"/>
    /// overrides the nonce of the initiator (e.g. the fetch options of a module script). Only the policies of
    /// <paramref name="disposition"/> are checked when it is not null.
    /// </summary>
    public static bool ShouldBlockRequest(Document policyDocument, UrlRecord url, RequestDestination destination, Element? initiator = null, bool parserInserted = false, int redirectCount = 0, string? nonce = null, bool reportViolations = true, SecurityPolicyViolationEventDisposition? disposition = null)
        => ShouldBlockRequest(policyDocument.ContentSecurityPoliciesOrNull, policyDocument.ContentSecurityPolicySelfOrigin, url, destination, initiator, parserInserted, redirectCount, nonce, reportViolations ? policyDocument : null, url, disposition);

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#should-block-request, given a CSP list (<paramref name="policies"/>) and its
    /// self-origin, without reporting the violations (e.g. the speculative fetches, whose request is checked again when it
    /// is fetched); see <see cref="ShouldBlockRequest(Document, UrlRecord, RequestDestination, Element?, bool, int, string?, bool, SecurityPolicyViolationEventDisposition?)"/>.
    /// </summary>
    public static bool ShouldBlockRequest(IReadOnlyList<ContentSecurityPolicy>? policies, OriginRecord selfOrigin, UrlRecord url, RequestDestination destination, Element? initiator = null, bool parserInserted = false, int redirectCount = 0, string? nonce = null)
        => ShouldBlockRequest(policies, selfOrigin, url, destination, initiator, parserInserted, redirectCount, nonce, reportingClient: null, url);

    /// <summary>
    /// The checks of the request for each policy (https://w3c.github.io/webappsec-csp/#does-request-violate-policy): a
    /// violation of a policy is reported for <paramref name="reportingClient"/> (the client of the request) with
    /// <paramref name="resource"/> as its resource, and blocks the request when the policy is enforced. Only the policies of
    /// <paramref name="disposition"/> are checked when it is not null. The request of a resource hint (a prefetch) violates
    /// a policy that has a <c>default-src</c> directive when no fetch directive of the policy allows its URL
    /// (https://w3c.github.io/webappsec-csp/#does-resource-hint-violate-policy).
    /// </summary>
    private static bool ShouldBlockRequest(IReadOnlyList<ContentSecurityPolicy>? policies, OriginRecord selfOrigin, UrlRecord url, RequestDestination destination, Element? initiator, bool parserInserted, int redirectCount, string? nonce, Document? reportingClient, UrlRecord resource, SecurityPolicyViolationEventDisposition? disposition = null, bool resourceHint = false)
    {
        if (policies is not { Count: > 0 } || GetEffectiveDirective(destination) is not { } effectiveDirective)
            return false;

        string? integrity = null;
        var blocked = false;
        foreach (var policy in policies)
        {
            // A report-only policy only reports its violations
            if ((policy.ReportOnly && reportingClient is null) || (disposition is { } checkedDisposition && policy.Disposition != checkedDisposition))
                continue;

            if (resourceHint)
            {
                if (policy.GetDirectiveValue("default-src") is null || FetchDirectives.Any(name => policy.GetDirectiveValue(name) is { } list && !IsBlocked("default-src", list, url, destination, nonce: "", integrity: "", parserInserted: false, selfOrigin, redirectCount)))
                    continue;

                if (reportingClient is not null)
                {
                    var violation = ContentSecurityPolicyViolation.Create(reportingClient, policy, "default-src");
                    violation.ResourceUrl = resource;
                    violation.Report();
                }

                blocked |= !policy.ReportOnly;
                continue;
            }

            // The directive that executes is the first one of the fallback list that the policy contains
            // (https://w3c.github.io/webappsec-csp/#should-directive-execute): its value is checked as the effective directive
            foreach (var name in GetFallbackList(effectiveDirective))
            {
                if (policy.GetDirectiveValue(name) is not { } sourceList)
                    continue;

                nonce ??= initiator is null ? "" : GetNonceMetadata(initiator);
                integrity ??= initiator is HtmlScriptElement or HtmlLinkElement ? initiator.GetAttributeValue("integrity") ?? "" : "";
                if (IsBlocked(effectiveDirective, sourceList, url, destination, nonce, integrity, parserInserted, selfOrigin, redirectCount))
                {
                    // https://w3c.github.io/webappsec-csp/#create-violation-for-request
                    if (reportingClient is not null)
                    {
                        var violation = ContentSecurityPolicyViolation.Create(reportingClient, policy, effectiveDirective);
                        if (effectiveDirective == "frame-src" && resource.Scheme is "http" or "https" && !OriginRecord.IsSameOrigin(OriginRecord.FromUrl(resource), reportingClient.Origin))
                        {
                            // As in the browsers, the blocked URI of the navigation of a child navigable to another origin is the
                            // origin of the URL
                            violation.Resource = OriginRecord.FromUrl(resource).Serialize();
                        }
                        else
                        {
                            violation.ResourceUrl = resource;
                        }

                        violation.Report();
                    }

                    if (!policy.ReportOnly)
                    {
                        blocked = true;
                    }
                }

                break;
            }
        }

        return blocked;
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#should-block-response (https://fetch.spec.whatwg.org/#main-fetch, step 19): the
    /// post-request checks of the fetch directives match the URL of the response
    /// (https://w3c.github.io/webappsec-csp/#match-response-to-source-list). The URL of a response is the
    /// current URL of its request, already checked by the pre-request checks, unless a service worker provided a response
    /// of another URL: only that response is checked again.
    /// </summary>
    public static bool ShouldBlockResponse(FetchRequest request, FetchResponse response)
    {
        if ((request.PolicyClient ?? request.Client) is not { ContentSecurityPoliciesOrNull: { } policies } client || response.IsNetworkError || response.Internal.UrlList is not [.., var responseUrl] || ReferenceEquals(responseUrl, request.CurrentUrl) || responseUrl.Serialize() == request.CurrentUrl.Serialize())
            return false;

        return ShouldBlockRequest(policies, client.ContentSecurityPolicySelfOrigin, responseUrl, request.Destination, request.Initiator, request.ParserInserted, request.RedirectCount, request.Nonce, client, request.Url);
    }

    /// <summary>
    /// The document whose CSP list applies to the inline content of an element (the CSP list of the global object of its
    /// document, https://w3c.github.io/webappsec-csp/#should-block-inline), or null when that CSP list is empty: the
    /// associated document of the window, or the document that is its own global object (see
    /// <see cref="TrustedTypePolicyFactory.GetGlobalDocument"/>). The documents created by a window (<c>DOMParser</c>, the
    /// fragment parsing, the template contents) use the policies of the window.
    /// </summary>
    public static Document? GetInlineCheckDocument(Element element)
    {
        var document = element.NodeDocument;
        if (document.ContentSecurityPoliciesOrNull is null && document.OriginDocument is null && !document.IsTemplateContentsOwnerDocument)
            return null;

        document = TrustedTypePolicyFactory.GetGlobalDocument(document);
        return document.ContentSecurityPoliciesOrNull is null ? null : document;
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#should-block-inline: whether the inline behavior of <paramref name="type"/>
    /// defined by <paramref name="source"/> (the source text of an inline script or style, the value of an event handler
    /// content attribute or of a <c>style</c> attribute) is blocked by an enforced policy of the CSP list of the global
    /// object of the element. The violations of all the policies are reported (with the element, and the source as sample
    /// when the directive contains <c>'report-sample'</c>). <paramref name="parserInserted"/> is whether a script element
    /// is parser-inserted (<c>'strict-dynamic'</c> allows the inline scripts that are not).
    /// </summary>
    public static bool ShouldBlockInline(Element element, ContentSecurityPolicyInlineType type, string source, bool parserInserted = false)
    {
        if (GetInlineCheckDocument(element) is not { } document)
            return false;

        return ShouldBlockInline(document, element, type, source, parserInserted, location: null);
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#should-block-navigation-request, steps 1 to 3: the pre-navigation checks of the
    /// policies of the CSP list of <paramref name="policyDocument"/> (the policy container of the source of the navigation,
    /// which also receives the violations) for the navigation request whose URL is <paramref name="requestUrl"/> and whose
    /// current URL is <paramref name="currentUrl"/> (the URL of a redirect when <paramref name="redirectCount"/> is not 0):
    /// the <c>form-action</c> directive of a form submission
    /// (https://w3c.github.io/webappsec-csp/#form-action-pre-navigate). The <c>require-trusted-types-for</c> pre-navigation
    /// check and the inline checks only apply to the <c>javascript:</c> URLs (see
    /// <see cref="ShouldBlockJavaScriptUrlNavigation"/>). The violations of all the policies are reported, with the URL of the
    /// request as resource.
    /// </summary>
    public static bool ShouldBlockNavigationRequest(Document policyDocument, UrlRecord requestUrl, UrlRecord currentUrl, bool isFormSubmission, int redirectCount = 0)
    {
        if (!isFormSubmission || policyDocument.ContentSecurityPoliciesOrNull is not { } policies)
            return false;

        var blocked = false;
        foreach (var policy in policies)
        {
            // https://w3c.github.io/webappsec-csp/#form-action-pre-navigate: "Does request match source list?"
            if (policy.GetDirectiveValue("form-action") is not { } sourceList || DoesUrlMatchSourceList(currentUrl, sourceList, policyDocument.ContentSecurityPolicySelfOrigin, redirectCount))
                continue;

            var violation = ContentSecurityPolicyViolation.Create(policyDocument, policy, "form-action");
            violation.ResourceUrl = requestUrl;
            violation.Report();
            if (!policy.ReportOnly)
            {
                blocked = true;
            }
        }

        return blocked;
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#should-block-navigation-request for the navigation to a <c>javascript:</c> URL
    /// (<paramref name="url"/>, serialized), with the policies of <paramref name="policyDocument"/> (the CSP list of the policy
    /// container of the source of the navigation, which also receives the violations): the pre-navigation checks (the
    /// <c>require-trusted-types-for</c> directives, whose default policy may convert the URL, and the <c>form-action</c>
    /// directive of a form submission), then, when they allow the navigation, the inline checks of the script directives
    /// (steps 4 and 5). <paramref name="element"/> is the target of the <c>securitypolicyviolation</c> event of the inline
    /// check (the hyperlink or the navigable container, as in the browsers), and <paramref name="location"/> the location of
    /// the script that started the navigation.
    /// </summary>
    public static bool ShouldBlockJavaScriptUrlNavigation(Document policyDocument, ref string url, bool isFormSubmission, Element? element, Scripting.ScriptLocation? location)
    {
        // Steps 1 to 3: the pre-navigation checks
        var blocked = TrustedTypesAlgorithms.ShouldJavaScriptUrlNavigationBeBlocked(policyDocument, ref url);
        if (isFormSubmission && policyDocument.ContentSecurityPoliciesOrNull is not null && UrlParser.Parse(url, baseUrl: null) is { } requestUrl)
        {
            blocked |= ShouldBlockNavigationRequest(policyDocument, requestUrl, requestUrl, isFormSubmission);
        }

        // Steps 4 and 5: the inline checks, when the pre-navigation checks allow the navigation
        if (blocked || policyDocument.ContentSecurityPoliciesOrNull is null)
            return blocked;

        return ShouldBlockInline(policyDocument, element, ContentSecurityPolicyInlineType.Navigation, url, parserInserted: false, location);
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#parse-response-csp: the CSP list of a response (the policies of its
    /// <c>Content-Security-Policy</c> headers, then the ones of its <c>Content-Security-Policy-Report-Only</c> headers), or
    /// null when it has none.
    /// </summary>
    public static List<ContentSecurityPolicy>? ParseResponsePolicies(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        List<ContentSecurityPolicy>? policies = null;
        foreach (var reportOnly in (ReadOnlySpan<bool>)[false, true])
        {
            var headerName = reportOnly ? "Content-Security-Policy-Report-Only" : "Content-Security-Policy";
            foreach (var (name, value) in headers)
            {
                if (!InfraAscii.EqualsIgnoreAsciiCase(name, headerName))
                    continue;

                foreach (var serializedPolicy in value.Split(','))
                {
                    var policy = Parse(serializedPolicy, reportOnly, fromMeta: false);
                    if (policy.Directives.Count > 0)
                    {
                        (policies ??= []).Add(policy);
                    }
                }
            }
        }

        return policies;
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#should-block-navigation-response: the navigation response checks of the policies of
    /// the response of a navigation of <paramref name="target"/> (<paramref name="responsePolicies"/>, whose self-origin is
    /// <paramref name="selfOrigin"/>): the <c>frame-ancestors</c> directive
    /// (https://w3c.github.io/webappsec-csp/#frame-ancestors-navigation-response) blocks the response when the origin of a
    /// container document of the ancestors of a child navigable does not match its source list. The response of a local URL is
    /// allowed. The policies of the source of the navigation have no navigation response check (the <c>frame-ancestors</c>
    /// check of the "source" type allows it). The violations have no global object: no event is fired, and the reports of the
    /// <c>report-uri</c> directive are sent from the origin of the response, with <paramref name="reportingDocument"/> (the
    /// container document) as the environment of the fetch. The report of the <c>report-to</c> directive is sent to the
    /// endpoint of the response (<paramref name="responseHeaders"/>), as in Chromium.
    /// </summary>
    public static bool ShouldBlockNavigationResponse(IReadOnlyList<ContentSecurityPolicy> responsePolicies, OriginRecord selfOrigin, UrlRecord responseUrl, int responseStatus, IReadOnlyList<KeyValuePair<string, string>> responseHeaders, BrowsingContext target, Document reportingDocument)
    {
        if (responseUrl.Scheme is "about" or "blob" or "data" || target.Parent is null)
            return false;

        var blocked = false;
        foreach (var policy in responsePolicies)
        {
            if (policy.GetDirectiveValue("frame-ancestors") is not { } sourceList || DoAncestorsMatchSourceList(target, sourceList, selfOrigin))
                continue;

            var violation = ContentSecurityPolicyViolation.CreateWithoutGlobal(reportingDocument, policy, "frame-ancestors", responseUrl, responseStatus, responseHeaders);
            violation.ResourceUrl = responseUrl;
            violation.Report();
            if (!policy.ReportOnly)
            {
                blocked = true;
            }
        }

        return blocked;

        // https://w3c.github.io/webappsec-csp/#frame-ancestors-navigation-response, steps 5 to 7: the origin of the container
        // document of each ancestor (an opaque origin, serialized as "null", is not a URL: it does not match)
        static bool DoAncestorsMatchSourceList(BrowsingContext target, List<string> sourceList, OriginRecord selfOrigin)
        {
            for (var current = target; current.Parent is not null && current.Container?.NodeDocument is { } document; current = document.BrowsingContext ?? current.Parent)
            {
                if (UrlParser.Parse(document.SerializeOrigin(), baseUrl: null) is not { } origin || !DoesUrlMatchSourceList(origin, sourceList, selfOrigin, redirectCount: 0))
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#allow-base-for-document: whether <paramref name="baseUrl"/> (the URL of the
    /// <c>href</c> attribute of the first <c>base</c> element) is allowed by the <c>base-uri</c> directives of the policies of
    /// the global object of <paramref name="document"/>. The violations of all the policies are reported. As in the browsers
    /// (and the tests), the blocked URI of the violation is the base URL, rather than <c>inline</c>.
    /// </summary>
    public static bool IsBaseAllowed(UrlRecord baseUrl, Document document)
    {
        if (TrustedTypePolicyFactory.GetGlobalDocument(document) is not { ContentSecurityPoliciesOrNull: { } policies } global)
            return true;

        var allowed = true;
        foreach (var policy in policies)
        {
            if (policy.GetDirectiveValue("base-uri") is not { } sourceList || DoesUrlMatchSourceList(baseUrl, sourceList, global.ContentSecurityPolicySelfOrigin, redirectCount: 0))
                continue;

            var violation = ContentSecurityPolicyViolation.Create(global, policy, "base-uri");
            violation.ResourceUrl = baseUrl;
            violation.Report();
            if (!policy.ReportOnly)
            {
                allowed = false;
            }
        }

        return allowed;
    }

    /// <summary>The inline checks of each policy of the CSP list of <paramref name="document"/>.</summary>
    private static bool ShouldBlockInline(Document document, Element? element, ContentSecurityPolicyInlineType type, string source, bool parserInserted, Scripting.ScriptLocation? location)
    {
        // https://w3c.github.io/webappsec-csp/#effective-directive-for-inline-check
        var effectiveDirective = type switch
        {
            ContentSecurityPolicyInlineType.Script or ContentSecurityPolicyInlineType.Navigation => "script-src-elem",
            ContentSecurityPolicyInlineType.ScriptAttribute => "script-src-attr",
            ContentSecurityPolicyInlineType.Style => "style-src-elem",
            _ => "style-src-attr",
        };

        var blocked = false;
        byte[]? sourceBytes = null;
        foreach (var policy in document.ContentSecurityPoliciesOrNull!)
        {
            // The inline check of a directive only runs when it is the first directive of the fallback list of the effective
            // directive that the policy contains (https://w3c.github.io/webappsec-csp/#should-directive-execute)
            foreach (var name in GetFallbackList(effectiveDirective))
            {
                if (policy.GetDirectiveValue(name) is not { } sourceList)
                    continue;

                if (!DoesElementMatchSourceList(element, sourceList, type, source, parserInserted, ref sourceBytes))
                {
                    var violation = ContentSecurityPolicyViolation.Create(document, policy, effectiveDirective);
                    violation.Resource = "inline";
                    violation.Element = element;
                    if (ContainsKeyword(sourceList, "'report-sample'"))
                    {
                        violation.Sample = ContentSecurityPolicyViolation.ClipSample(source);
                    }

                    if (location is { } scriptLocation)
                    {
                        violation.SetSourceLocation(scriptLocation);
                    }
                    else if (violation.SourceFile is null && element is not null && GetInlineSourcePosition(element) is { } position)
                    {
                        // The content of an element created by the parser, when no script is running: the position of its source
                        // text in the document (the line, and the column after the start tag, 1-based)
                        violation.SourceFile = element.NodeDocument.UrlRecord;
                        violation.LineNumber = position.Line;
                        violation.ColumnNumber = position.Column + 1;
                    }

                    violation.Report();
                    if (!policy.ReportOnly)
                    {
                        blocked = true;
                    }
                }

                break;
            }
        }

        return blocked;
    }

    /// <summary>The position in the document of the source text of a script or a style element created by the parser, or null.</summary>
    private static (int Line, int Column)? GetInlineSourcePosition(Element element) => element switch
    {
        HtmlScriptElement { HasSourceTextPosition: true } script => script.SourceTextPosition,
        _ => (element.RareDataOrNull as ElementRareData)?.InlineSourcePosition,
    };

    /// <summary>https://w3c.github.io/webappsec-csp/#match-element-to-source-list</summary>
    private static bool DoesElementMatchSourceList(Element? element, List<string> sourceList, ContentSecurityPolicyInlineType type, string source, bool parserInserted, ref byte[]? sourceBytes)
    {
        if (DoesSourceListAllowAllInlineBehavior(sourceList, type))
            return true;

        // 'inline-speculation-rules' allows the inline speculation rules (https://html.spec.whatwg.org/multipage/speculative-loading.html#speculative-loading-security)
        if (type == ContentSecurityPolicyInlineType.Script && element is HtmlScriptElement { ScriptType: ScriptElementType.SpeculationRules } && ContainsKeyword(sourceList, "'inline-speculation-rules'"))
            return true;

        // The nonces only apply to the inline scripts and styles, not to the attributes nor to the javascript: navigations
        if (type is ContentSecurityPolicyInlineType.Script or ContentSecurityPolicyInlineType.Style && element is not null && IsElementNonceable(element) && DoesNonceMatchSourceList(element.CryptographicNonce, sourceList))
            return true;

        // The hashes apply to the attributes and to the javascript: navigations with 'unsafe-hashes'
        if (type is not (ContentSecurityPolicyInlineType.Script or ContentSecurityPolicyInlineType.Style) && !ContainsKeyword(sourceList, "'unsafe-hashes'"))
            return false;

        Span<byte> hash = stackalloc byte[64];
        foreach (var expression in sourceList)
        {
            if (InfraAscii.EqualsIgnoreAsciiCase(expression, "'strict-dynamic'"))
            {
                if (type == ContentSecurityPolicyInlineType.Script && !parserInserted)
                    return true;

                continue;
            }

            HashAlgorithmName algorithm;
            string value;
            if (TryGetPrefixedBase64Value(expression, "'sha256-", out value))
            {
                algorithm = HashAlgorithmName.SHA256;
            }
            else if (TryGetPrefixedBase64Value(expression, "'sha384-", out value))
            {
                algorithm = HashAlgorithmName.SHA384;
            }
            else if (TryGetPrefixedBase64Value(expression, "'sha512-", out value))
            {
                algorithm = HashAlgorithmName.SHA512;
            }
            else
            {
                continue;
            }

            // The source is converted to UTF-8 (a lone surrogate is replaced by U+FFFD, https://webidl.spec.whatwg.org/#dfn-obtain-unicode)
            sourceBytes ??= Encoding.UTF8.GetBytes(source);
            var hashLength = algorithm == HashAlgorithmName.SHA256 ? SHA256.HashData(sourceBytes, hash) : algorithm == HashAlgorithmName.SHA384 ? SHA384.HashData(sourceBytes, hash) : SHA512.HashData(sourceBytes, hash);
            var actual = Convert.ToBase64String(hash[..hashLength]);

            // The base64url values are normalized to base64
            if (actual == value.Replace('-', '+').Replace('_', '/'))
                return true;
        }

        return false;
    }

    /// <summary>https://w3c.github.io/webappsec-csp/#allow-all-inline</summary>
    private static bool DoesSourceListAllowAllInlineBehavior(List<string> sourceList, ContentSecurityPolicyInlineType type)
    {
        var allowAllInline = false;
        foreach (var expression in sourceList)
        {
            if (IsNonceOrHashSource(expression))
                return false;

            if (type is ContentSecurityPolicyInlineType.Script or ContentSecurityPolicyInlineType.ScriptAttribute or ContentSecurityPolicyInlineType.Navigation && InfraAscii.EqualsIgnoreAsciiCase(expression, "'strict-dynamic'"))
                return false;

            if (InfraAscii.EqualsIgnoreAsciiCase(expression, "'unsafe-inline'"))
            {
                allowAllInline = true;
            }
        }

        return allowAllInline;

        static bool IsNonceOrHashSource(string expression)
            => TryGetPrefixedBase64Value(expression, "'nonce-", out _) || TryGetPrefixedBase64Value(expression, "'sha256-", out _) || TryGetPrefixedBase64Value(expression, "'sha384-", out _) || TryGetPrefixedBase64Value(expression, "'sha512-", out _);
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#is-element-nonceable: a nonce-source does not match an element whose nonce is
    /// empty, a script element one of whose attributes contains <c>&lt;script</c>, <c>&lt;style</c> or <c>&lt;link</c> in its
    /// name or its value (dangling markup), nor an element that had a duplicate-attribute parse error. As in the browsers, the
    /// nonce is the cryptographic nonce of the element (<c>element.nonce</c>, also set by the IDL attribute), not the
    /// <c>nonce</c> content attribute (which is hidden).
    /// </summary>
    public static bool IsElementNonceable(Element element)
    {
        if (element.CryptographicNonce.Length == 0)
            return false;

        if (element is HtmlScriptElement or SvgScriptElement)
        {
            for (var i = 0; i < element.AttributeCount; i++)
            {
                if (ContainsDanglingMarkup(element.GetAttributeQualifiedName(i)) || ContainsDanglingMarkup(element.GetAttributeValue(i)))
                    return false;
            }
        }

        return !(element.RareDataOrNull is ElementRareData { HadDuplicateAttributeParseError: true });

        static bool ContainsDanglingMarkup(string value)
            => value.Contains('<', StringComparison.Ordinal)
                && (value.Contains("<script", StringComparison.OrdinalIgnoreCase) || value.Contains("<style", StringComparison.OrdinalIgnoreCase) || value.Contains("<link", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The cryptographic nonce metadata of the requests of an element (e.g. a script, a style sheet): its cryptographic nonce,
    /// or the empty string when it is not nonceable (https://w3c.github.io/webappsec-csp/#is-element-nonceable), as in the
    /// browsers.
    /// </summary>
    public static string GetNonceMetadata(Element element)
    {
        var nonce = element.CryptographicNonce;
        return nonce.Length == 0 || IsElementNonceable(element) ? nonce : "";
    }

    /// <summary>Whether a source list contains a keyword-source (ASCII case-insensitive).</summary>
    private static bool ContainsKeyword(List<string> sourceList, string keyword)
    {
        foreach (var expression in sourceList)
        {
            if (InfraAscii.EqualsIgnoreAsciiCase(expression, keyword))
                return true;
        }

        return false;
    }

    /// <summary>Whether the policy contains a directive (https://w3c.github.io/webappsec-csp/#content-security-policy-object-directive-set).</summary>
    public bool ContainsDirective(string name) => GetDirectiveValue(name) is not null;

    /// <summary>The value of the directive of the directive set whose name is <paramref name="name"/> (lowercase), or null.</summary>
    public List<string>? GetDirectiveValue(string name)
    {
        foreach (var directive in Directives)
        {
            if (directive.Name == name)
                return directive.Value;
        }

        return null;
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#effective-directive-for-a-request. The violation reports are not checked (null),
    /// nor are the navigations of the top-level navigables (the "document" destination, which the specification checks with
    /// <c>connect-src</c>, a default the browsers do not apply to the navigations).
    /// </summary>
    public static string? GetEffectiveDirective(RequestDestination destination) => destination switch
    {
        RequestDestination.Document or RequestDestination.Report => null,
        RequestDestination.Iframe or RequestDestination.Frame => "frame-src",
        RequestDestination.Embed or RequestDestination.Object => "object-src",
        RequestDestination.Font => "font-src",
        RequestDestination.Image => "img-src",
        RequestDestination.Audio or RequestDestination.Video or RequestDestination.Track => "media-src",
        RequestDestination.Style => "style-src-elem",
        RequestDestination.Script or RequestDestination.PaintWorklet => "script-src-elem",
        RequestDestination.Worker or RequestDestination.SharedWorker or RequestDestination.ServiceWorker => "worker-src",
        _ => "connect-src",
    };

    /// <summary>The fetch directives (https://w3c.github.io/webappsec-csp/#directives-fetch), which can allow the request of a resource hint.</summary>
    private static readonly string[] FetchDirectives = ["child-src", "connect-src", "default-src", "font-src", "frame-src", "img-src", "manifest-src", "media-src", "object-src", "script-src", "script-src-elem", "style-src", "style-src-elem", "worker-src"];

    /// <summary>https://w3c.github.io/webappsec-csp/#directive-fallback-list</summary>
    private static string[] GetFallbackList(string directive) => directive switch
    {
        "script-src-elem" => ["script-src-elem", "script-src", "default-src"],
        "style-src-elem" => ["style-src-elem", "style-src", "default-src"],
        "script-src-attr" => ["script-src-attr", "script-src", "default-src"],
        "style-src-attr" => ["style-src-attr", "style-src", "default-src"],
        "connect-src" => ["connect-src", "default-src"],
        "font-src" => ["font-src", "default-src"],
        "img-src" => ["img-src", "default-src"],
        "media-src" => ["media-src", "default-src"],
        "worker-src" => ["worker-src", "child-src", "script-src", "default-src"],
        "object-src" => ["object-src", "default-src"],
        "frame-src" => ["frame-src", "child-src", "default-src"],
        _ => [],
    };

    /// <summary>The pre-request check of the effective directive, with the value of the directive that executes.</summary>
    private static bool IsBlocked(string effectiveDirective, List<string> sourceList, UrlRecord url, RequestDestination destination, string nonce, string integrity, bool parserInserted, OriginRecord selfOrigin, int redirectCount)
    {
        switch (effectiveDirective)
        {
            case "script-src-elem" or "worker-src":
                // https://w3c.github.io/webappsec-csp/#script-pre-request (the pre-request check of worker-src is also the
                // one of the script directives): the nonce, the integrity metadata and 'strict-dynamic' apply to the
                // script-like destinations
                if (destination is RequestDestination.Script or RequestDestination.PaintWorklet or RequestDestination.Worker or RequestDestination.SharedWorker or RequestDestination.ServiceWorker)
                {
                    if (DoesNonceMatchSourceList(nonce, sourceList) || DoesIntegrityMetadataMatchSourceList(integrity, sourceList))
                        return false;

                    if (sourceList.Exists(expression => InfraAscii.EqualsIgnoreAsciiCase(expression, "'strict-dynamic'")))
                        return parserInserted;
                }

                break;

            case "style-src-elem":
                // https://w3c.github.io/webappsec-csp/#style-src-elem-pre-request
                if (DoesNonceMatchSourceList(nonce, sourceList))
                    return false;

                break;
        }

        return !DoesUrlMatchSourceList(url, sourceList, selfOrigin, redirectCount);
    }

    /// <summary>https://w3c.github.io/webappsec-csp/#match-nonce-to-source-list</summary>
    private static bool DoesNonceMatchSourceList(string nonce, List<string> sourceList)
    {
        if (nonce.Length == 0)
            return false;

        foreach (var expression in sourceList)
        {
            if (TryGetPrefixedBase64Value(expression, "'nonce-", out var value) && (value == nonce || nonce == AnyNonce))
                return true;
        }

        return false;
    }

    /// <summary>https://w3c.github.io/webappsec-csp/#match-integrity-metadata-to-source-list</summary>
    private static bool DoesIntegrityMetadataMatchSourceList(string integrityMetadata, List<string> sourceList)
    {
        var integrityExpressions = new List<(string Algorithm, string Value)>();
        foreach (var expression in sourceList)
        {
            foreach (var algorithm in (ReadOnlySpan<string>)["sha256", "sha384", "sha512"])
            {
                if (TryGetPrefixedBase64Value(expression, "'" + algorithm + "-", out var value))
                {
                    integrityExpressions.Add((algorithm, value));
                }
            }
        }

        if (integrityExpressions.Count == 0)
            return false;

        // https://w3c.github.io/webappsec-subresource-integrity/#parse-metadata: the options of an item are ignored, and the
        // items with an unknown algorithm are skipped
        var integritySources = 0;
        foreach (var item in InfraString.SplitOnAsciiWhitespace(integrityMetadata))
        {
            var separator = item.IndexOf('-', StringComparison.Ordinal);
            if (separator < 0)
                continue;

            var algorithm = InfraAscii.ToAsciiLowercase(item[..separator]);
            if (algorithm is not ("sha256" or "sha384" or "sha512"))
                continue;

            var value = item[(separator + 1)..];
            var options = value.IndexOf('?', StringComparison.Ordinal);
            if (options >= 0)
            {
                value = value[..options];
            }

            integritySources++;
            if (!integrityExpressions.Contains((algorithm, value)))
                return false;
        }

        return integritySources > 0;
    }

    /// <summary>Returns the base64-value of a source expression like <c>'nonce-value'</c> or <c>'sha256-value'</c> (https://w3c.github.io/webappsec-csp/#grammardef-base64-value).</summary>
    private static bool TryGetPrefixedBase64Value(string expression, string prefix, out string value)
    {
        value = "";
        if (expression.Length <= prefix.Length + 1 || !expression.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || expression[^1] != '\'')
            return false;

        var candidate = expression[prefix.Length..^1];
        var padding = 0;
        foreach (var c in candidate)
        {
            if (c == '=')
            {
                padding++;
                continue;
            }

            if (padding > 0 || !(char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '-' or '_'))
                return false;
        }

        if (padding > 2 || padding == candidate.Length)
            return false;

        value = candidate;
        return true;
    }

    /// <summary>https://w3c.github.io/webappsec-csp/#match-url-to-source-list</summary>
    private static bool DoesUrlMatchSourceList(UrlRecord url, List<string> sourceList, OriginRecord origin, int redirectCount)
    {
        if (sourceList.Count == 0 || (sourceList.Count == 1 && InfraAscii.EqualsIgnoreAsciiCase(sourceList[0], "'none'")))
            return false;

        foreach (var expression in sourceList)
        {
            if (DoesUrlMatchExpression(url, expression, origin, redirectCount))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a string is a scheme-source or a host-source (https://w3c.github.io/webappsec-csp/#grammardef-scheme-source,
    /// https://w3c.github.io/webappsec-csp/#grammardef-host-source): the permissions-source-expressions of Permissions Policy.
    /// </summary>
    internal static bool IsSchemeOrHostSource(string expression)
        => TryParseSchemeSource(expression, out _) || TryParseHostSource(expression, out _, out _, out _, out _);

    /// <summary>https://w3c.github.io/webappsec-csp/#match-url-to-source-expression, without redirect (used by the allowlists of Permissions Policy).</summary>
    internal static bool DoesUrlMatchSourceExpression(UrlRecord url, string expression, OriginRecord origin)
        => DoesUrlMatchExpression(url, expression, origin, redirectCount: 0);

    /// <summary>https://w3c.github.io/webappsec-csp/#match-url-to-source-expression</summary>
    private static bool DoesUrlMatchExpression(UrlRecord url, string expression, OriginRecord origin, int redirectCount)
    {
        if (expression == "*")
            return url.Scheme is "http" or "https" || url.Scheme == origin.Scheme;

        if (TryParseSchemeSource(expression, out var schemeSource))
            return SchemePartMatches(schemeSource, url.Scheme);

        if (TryParseHostSource(expression, out var scheme, out var host, out var port, out var path))
        {
            if (scheme is not null && !SchemePartMatches(scheme, url.Scheme))
                return false;

            if (url.Host is not { } urlHost)
                return false;

            if (scheme is null && (origin.Scheme is null || !SchemePartMatches(origin.Scheme, url.Scheme)))
                return false;

            if (!HostPartMatches(host, urlHost))
                return false;

            if (!PortPartMatches(port, url))
                return false;

            if (path.Length > 0 && redirectCount == 0 && !PathPartMatches(path, url.HasOpaquePath ? url.OpaquePath! : url.SerializePath()))
                return false;

            return true;
        }

        if (InfraAscii.EqualsIgnoreAsciiCase(expression, "'self'"))
        {
            if (url.Scheme == "blob")
                return false;

            if (OriginRecord.IsSameOrigin(origin, OriginRecord.FromUrl(url)))
                return true;

            if (origin.IsOpaque || url.Host is not { } selfHost || origin.Host != selfHost)
                return false;

            // The ports are the same, or the default ports of their schemes (the port of a URL or an origin is null when it is
            // the default port)
            return origin.Port == url.Port && (url.Scheme is "https" or "wss" || (origin.Scheme == "http" && url.Scheme is "http" or "ws"));
        }

        return false;
    }

    /// <summary>scheme-source = scheme-part ":" (https://w3c.github.io/webappsec-csp/#grammardef-scheme-source)</summary>
    private static bool TryParseSchemeSource(string expression, out string scheme)
    {
        scheme = "";
        if (expression.Length < 2 || expression[^1] != ':' || !IsScheme(expression.AsSpan(0, expression.Length - 1)))
            return false;

        scheme = expression[..^1];
        return true;
    }

    /// <summary>host-source = [ scheme-part "://" ] host-part [ ":" port-part ] [ path-part ] (https://w3c.github.io/webappsec-csp/#grammardef-host-source)</summary>
    private static bool TryParseHostSource(string expression, out string? scheme, out string host, out string? port, out string path)
    {
        scheme = null;
        host = "";
        port = null;
        path = "";
        var rest = expression.AsSpan();
        var schemeSeparator = rest.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator >= 0)
        {
            if (!IsScheme(rest[..schemeSeparator]))
                return false;

            scheme = rest[..schemeSeparator].ToString();
            rest = rest[(schemeSeparator + 3)..];
        }

        // host-part = "*" / [ "*." ] 1*host-char *( "." 1*host-char ) [ "." ]
        var hostEnd = rest.IndexOfAny(':', '/');
        var hostPart = hostEnd < 0 ? rest : rest[..hostEnd];
        if (!IsHostPart(hostPart))
            return false;

        host = hostPart.ToString();
        rest = hostEnd < 0 ? [] : rest[hostEnd..];
        if (rest.Length > 0 && rest[0] == ':')
        {
            var portEnd = rest.IndexOf('/');
            var portPart = portEnd < 0 ? rest[1..] : rest[1..portEnd];
            if (portPart.Length == 0 || (portPart is not "*" && portPart.ContainsAnyExceptInRange('0', '9')))
                return false;

            port = portPart.ToString();
            rest = portEnd < 0 ? [] : rest[portEnd..];
        }

        // path-part = path-absolute (but not including ";" or ",")
        if (rest.Length > 0)
        {
            if (rest[0] != '/' || rest.ContainsAny(';', ','))
                return false;

            path = rest.ToString();
        }

        return true;

        static bool IsHostPart(ReadOnlySpan<char> value)
        {
            if (value is "*")
                return true;

            if (value.StartsWith("*."))
            {
                value = value[2..];
            }

            if (value.EndsWith('.'))
            {
                value = value[..^1];
            }

            if (value.Length == 0)
                return false;

            foreach (var label in value.ToString().Split('.'))
            {
                if (label.Length == 0)
                    return false;

                foreach (var c in label)
                {
                    if (!char.IsAsciiLetterOrDigit(c) && c != '-')
                        return false;
                }
            }

            return true;
        }
    }

    /// <summary>scheme = ALPHA *( ALPHA / DIGIT / "+" / "-" / "." ) (RFC 3986)</summary>
    private static bool IsScheme(ReadOnlySpan<char> value)
    {
        if (value.Length == 0 || !char.IsAsciiLetter(value[0]))
            return false;

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('+' or '-' or '.'))
                return false;
        }

        return true;
    }

    /// <summary>https://w3c.github.io/webappsec-csp/#scheme-part-match</summary>
    private static bool SchemePartMatches(string a, string b)
    {
        return InfraAscii.EqualsIgnoreAsciiCase(a, b)
            || (InfraAscii.EqualsIgnoreAsciiCase(a, "http") && InfraAscii.EqualsIgnoreAsciiCase(b, "https"))
            || (InfraAscii.EqualsIgnoreAsciiCase(a, "ws") && (InfraAscii.EqualsIgnoreAsciiCase(b, "wss") || InfraAscii.EqualsIgnoreAsciiCase(b, "http") || InfraAscii.EqualsIgnoreAsciiCase(b, "https")))
            || (InfraAscii.EqualsIgnoreAsciiCase(a, "wss") && InfraAscii.EqualsIgnoreAsciiCase(b, "https"));
    }

    /// <summary>https://w3c.github.io/webappsec-csp/#host-part-match</summary>
    private static bool HostPartMatches(string pattern, UrlHost host)
    {
        if (host.Kind != UrlHostKind.Domain)
            return false;

        if (pattern == "*")
            return true;

        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            var remaining = InfraAscii.ToAsciiLowercase(pattern[1..]);
            return InfraAscii.ToAsciiLowercase(host.Serialized).EndsWith(remaining, StringComparison.Ordinal);
        }

        return InfraAscii.EqualsIgnoreAsciiCase(pattern, host.Serialized);
    }

    /// <summary>https://w3c.github.io/webappsec-csp/#port-part-matches</summary>
    private static bool PortPartMatches(string? input, UrlRecord url)
    {
        if (input == "*")
            return true;

        // A port larger than the maximum never equals the port of a URL
        int? normalizedInput = input is null ? null : int.TryParse(input, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : int.MaxValue;
        int? urlPort = url.Port < 0 ? null : url.Port;
        if (normalizedInput == urlPort)
            return true;

        if (urlPort is null)
        {
            var defaultPort = UrlSchemes.GetDefaultPort(url.Scheme);
            if (defaultPort >= 0 && normalizedInput == defaultPort)
                return true;
        }

        return false;
    }

    /// <summary>https://w3c.github.io/webappsec-csp/#path-part-match</summary>
    private static bool PathPartMatches(string pathA, string pathB)
    {
        if (pathA.Length == 0)
            return true;

        if (pathA == "/" && pathB.Length == 0)
            return true;

        var exactMatch = pathA[^1] != '/';
        var pathListA = pathA.Split('/').ToList();
        var pathListB = pathB.Split('/');
        if (pathListA.Count > pathListB.Length)
            return false;

        if (exactMatch && pathListA.Count != pathListB.Length)
            return false;

        if (!exactMatch)
        {
            pathListA.RemoveAt(pathListA.Count - 1);
        }

        for (var i = 0; i < pathListA.Count; i++)
        {
            if (!PercentEncoding.PercentDecode(pathListA[i]).AsSpan().SequenceEqual(PercentEncoding.PercentDecode(pathListB[i])))
                return false;
        }

        return true;
    }
}

/// <summary>The type of an inline check (https://w3c.github.io/webappsec-csp/#should-block-inline).</summary>
internal enum ContentSecurityPolicyInlineType
{
    /// <summary>"script": an inline script.</summary>
    Script,

    /// <summary>"script attribute": an event handler content attribute.</summary>
    ScriptAttribute,

    /// <summary>"style": an inline style sheet (a <c>style</c> element).</summary>
    Style,

    /// <summary>"style attribute": a <c>style</c> attribute.</summary>
    StyleAttribute,

    /// <summary>"navigation": the navigation to a <c>javascript:</c> URL.</summary>
    Navigation,
}
