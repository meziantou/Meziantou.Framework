using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

// The checks of the content security policies and of X-Frame-Options on the navigations
public sealed partial class BrowsingContext
{
    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#create-navigation-params-by-fetching: whether the request of
    /// a navigation to <paramref name="url"/> is blocked by the policies of its source (a network error): the pre-navigation
    /// checks (<see cref="ContentSecurityPolicy.ShouldBlockNavigationRequest"/>, the <c>form-action</c> directive), and the
    /// fetch directive of the navigation of a child navigable (<c>frame-src</c>) for the <c>data:</c> and <c>blob:</c> URLs,
    /// which are not fetched by the fetch algorithm (https://fetch.spec.whatwg.org/#main-fetch, step 7; the <c>http:</c> and
    /// <c>https:</c> URLs are checked by <see cref="Fetching"/>, and the resources of the <c>object</c> and <c>embed</c>
    /// elements by their fetch). As in the browsers, the <c>about:</c> URLs are not blocked by <c>frame-src</c>.
    /// </summary>
    private bool IsNavigationRequestBlockedByContentSecurityPolicy(UrlRecord url, Document? sourceDocument, bool isFormSubmission)
    {
        if (sourceDocument is not { ContentSecurityPoliciesOrNull: not null })
            return false;

        if (ContentSecurityPolicy.ShouldBlockNavigationRequest(sourceDocument, url, url, isFormSubmission))
            return true;

        var destination = GetNavigationDestination();
        return url.Scheme is "data" or "blob" && destination is RequestDestination.Iframe or RequestDestination.Frame
            && ContentSecurityPolicy.ShouldBlockRequest(sourceDocument, url, destination, initiator: Container);
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#attempt-to-populate-the-history-entry's-document: whether the
    /// response of a navigation of a child navigable is blocked (an error document is created instead), in the order of the
    /// specification: by the <c>frame-ancestors</c> directives of the policies of the response
    /// (https://w3c.github.io/webappsec-csp/#should-block-navigation-response), by the embedder policy of the container
    /// document (<see cref="IsBlockedByEmbedderPolicy"/>, whose violations are reported), or by its <c>X-Frame-Options</c>
    /// header (https://html.spec.whatwg.org/multipage/document-lifecycle.html#the-x-frame-options-header).
    /// </summary>
    private bool IsNavigationResponseBlocked(UrlRecord responseUrl, IReadOnlyList<KeyValuePair<string, string>> headers, int status, bool checkResourcePolicy)
    {
        if (Parent is null || Container?.NodeDocument is not { } containerDocument)
            return false;

        if (responseUrl.Scheme is "about" or "blob" or "data")
            return IsBlockedByEmbedderPolicy(responseUrl, headers, checkResourcePolicy);

        var policies = ContentSecurityPolicy.ParseResponsePolicies(headers);
        var responseOrigin = OriginRecord.FromUrl(responseUrl);
        if (policies is not null && ContentSecurityPolicy.ShouldBlockNavigationResponse(policies, responseOrigin, responseUrl, status, this, containerDocument))
            return true;

        if (IsBlockedByEmbedderPolicy(responseUrl, headers, checkResourcePolicy))
            return true;

        return !CheckXFrameOptions(headers, policies, responseOrigin);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/document-lifecycle.html#check-a-navigation-response's-adherence-to-x-frame-options</summary>
    private bool CheckXFrameOptions(IReadOnlyList<KeyValuePair<string, string>> headers, List<ContentSecurityPolicy>? policies, OriginRecord destinationOrigin)
    {
        // An enforced frame-ancestors directive overrides X-Frame-Options
        if (policies is not null && policies.Exists(policy => !policy.ReportOnly && policy.ContainsDirective("frame-ancestors")))
            return true;

        if (HeaderList.GetDecodeAndSplit(headers, "X-Frame-Options") is not { Count: > 0 } rawXFrameOptions)
            return true;

        var xFrameOptions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in rawXFrameOptions)
        {
            xFrameOptions.Add(Infra.InfraAscii.ToAsciiLowercase(value));
        }

        // The values that try to do something valid but appear confused block the response, the other invalid values are ignored
        if (xFrameOptions.Count > 1)
            return !(xFrameOptions.Contains("deny") || xFrameOptions.Contains("allowall") || xFrameOptions.Contains("sameorigin"));

        var option = xFrameOptions.First();
        if (option == "deny")
            return false;

        if (option == "sameorigin")
        {
            for (var containerDocument = Container?.NodeDocument; containerDocument is not null; containerDocument = containerDocument.BrowsingContext?.Container?.NodeDocument)
            {
                if (!OriginRecord.IsSameOrigin(containerDocument.Origin, destinationOrigin))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The CSP-derived sandboxing flags of an inherited policy container (https://w3c.github.io/webappsec-csp/#sandbox-init):
    /// the flags of the <c>sandbox</c> directives of its enforced policies.
    /// </summary>
    private static SandboxingFlags GetContentSecurityPolicySandboxingFlags(PolicyContainer policyContainer)
    {
        var flags = SandboxingFlags.None;
        foreach (var policy in policyContainer.ContentSecurityPolicies ?? [])
        {
            if (!policy.ReportOnly && policy.GetDirectiveValue("sandbox") is { } value)
            {
                flags |= Sandboxing.ParseSandboxingDirective(string.Join(' ', value));
            }
        }

        return flags;
    }
}
