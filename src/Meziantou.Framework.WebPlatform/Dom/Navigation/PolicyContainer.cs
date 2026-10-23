namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A snapshot of the policy container of a document (https://html.spec.whatwg.org/multipage/browsers.html#policy-containers),
/// as cloned by "clone a policy container": the policies that a document of a local scheme inherits from its initiator, its
/// parent or its creator, and that the session history keeps for the URLs that require storing the policy container in
/// history. It holds the CSP list (and its self-origin) and the Trusted Types directives of its policies; the referrer
/// policy and the embedder policies of the documents are still inherited by the navigations separately
/// (<see cref="Document.ReferrerPolicy"/>, <see cref="Document.EmbedderPolicy"/>). The other policies that are inherited
/// with the policy container (e.g. the insecure requests policy) can be added to it.
/// </summary>
/// <remarks>The policies are immutable, so a clone shares them with the original (a copy of a policy is the policy).</remarks>
internal sealed class PolicyContainer
{
    /// <summary>A policy container without policy.</summary>
    public static PolicyContainer Empty { get; } = new();

    /// <summary>The CSP list (https://w3c.github.io/webappsec-csp/#csp-list), or null when it is empty.</summary>
    public IReadOnlyList<ContentSecurityPolicy>? ContentSecurityPolicies { get; init; }

    /// <summary>The self-origin of the CSP list (https://w3c.github.io/webappsec-csp/#csp-list-self-origin), or null for the origin of the document.</summary>
    public OriginRecord? ContentSecurityPolicySelfOrigin { get; init; }

    /// <summary>The Trusted Types directives of the policies of the CSP list (see <see cref="Document.TrustedTypesDirectives"/>), or null when there are none.</summary>
    public IReadOnlyList<TrustedTypesDirectives>? TrustedTypesDirectives { get; init; }

    /// <summary>
    /// The required CSP of the policy container (https://w3c.github.io/webappsec-cspee/#policy-container-required-csp): the
    /// serialized policy that the documents of the child navigables of the document must enforce, or null.
    /// </summary>
    public string? RequiredContentSecurityPolicy { get; init; }

    /// <summary>Whether the container holds no policy.</summary>
    public bool IsEmpty => ContentSecurityPolicies is null && TrustedTypesDirectives is null;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsers.html#clone-a-policy-container: a clone of the policy container of a
    /// document (or of the environment of a worker), whose self-origin is the self-origin of the CSP list of the document.
    /// </summary>
    public static PolicyContainer Clone(Document document)
    {
        var policies = document.ContentSecurityPoliciesOrNull;
        var trustedTypesDirectives = document.TrustedTypesDirectivesOrNull;
        return new PolicyContainer
        {
            ContentSecurityPolicies = policies is { Count: > 0 } ? [.. policies] : null,
            ContentSecurityPolicySelfOrigin = document.ContentSecurityPolicySelfOrigin,
            TrustedTypesDirectives = trustedTypesDirectives is { Count: > 0 } ? [.. trustedTypesDirectives] : null,
            RequiredContentSecurityPolicy = document.RequiredContentSecurityPolicy,
        };
    }

    /// <summary>A clone of the policy container of a document (see <see cref="Clone(Document)"/>), or null when there is no document.</summary>
    public static PolicyContainer? CloneOrNull(Document? document) => document is null ? null : Clone(document);
}
