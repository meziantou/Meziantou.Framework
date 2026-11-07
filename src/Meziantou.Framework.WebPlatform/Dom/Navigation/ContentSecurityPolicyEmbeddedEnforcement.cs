using System.Globalization;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

// CSP Embedded Enforcement (https://w3c.github.io/webappsec-cspee/): the required CSP of the iframe elements, the
// Sec-Required-CSP request header, the Allow-CSP-From response header and the subsumption of the policies
internal sealed partial class ContentSecurityPolicy
{
    /// <summary>The maximum length of a valid <c>csp</c> attribute (https://w3c.github.io/webappsec-cspee/#csp-attribute).</summary>
    private const int MaxRequiredPolicyLength = 4096;

    /// <summary>
    /// The directives whose source lists are compared by the subsumption of the policies (as in Chromium, the only
    /// implementation): a directive that the required policy gets from its fallback list (e.g. <c>default-src</c> for
    /// <c>img-src</c>) is compared with the directives of the response for the same directive.
    /// </summary>
    private static readonly string[] SubsumptionDirectives = ["script-src", "script-src-elem", "script-src-attr", "style-src", "style-src-elem", "style-src-attr", "worker-src", "base-uri", "object-src", "frame-src", "img-src", "media-src", "font-src", "form-action", "connect-src", "manifest-src", "frame-ancestors"];

    /// <summary>The keywords that the subsumption compares (the other keywords, e.g. <c>'report-sample'</c>, allow nothing).</summary>
    private static readonly string[] SubsumptionKeywords = ["'unsafe-eval'", "'wasm-unsafe-eval'", "'unsafe-hashes'", "'strict-dynamic'", "'inline-speculation-rules'", "'trusted-types-eval'"];

    /// <summary>
    /// https://w3c.github.io/webappsec-cspee/#iframe-csp-valid-attribute-value: whether <paramref name="value"/> is a valid value of the
    /// <c>csp</c> attribute of an iframe element of <paramref name="containerDocument"/>: a non-empty serialized policy of at
    /// most 4096 characters, without <c>report-uri</c> nor <c>report-to</c> directive, that the required CSP of the
    /// container document subsumes. As in Chromium, a value that contains a line break (which could inject a header in the
    /// request) is invalid.
    /// </summary>
    public static bool IsValidRequiredPolicy(string value, Document containerDocument)
    {
        if (value.Length is 0 or > MaxRequiredPolicyLength || value.Contains('\r', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal) || !MatchesSerializedPolicyGrammar(value))
            return false;

        var policy = Parse(value, reportOnly: false, fromMeta: false);
        if (policy.ContainsDirective("report-uri") || policy.ContainsDirective("report-to"))
            return false;

        return containerDocument.RequiredContentSecurityPolicy is not { } containerRequiredPolicy
            || Subsumes(Parse(containerRequiredPolicy, reportOnly: false, fromMeta: false), [policy], containerDocument.ContentSecurityPolicySelfOrigin);
    }

    /// <summary>
    /// Whether a string matches the serialized-policy grammar (https://w3c.github.io/webappsec-csp/#grammardef-serialized-policy):
    /// directives separated by semicolons, whose name is made of ASCII alphanumeric characters and dashes, and whose value
    /// is made of ASCII whitespace and visible ASCII characters other than <c>,</c> and <c>;</c>.
    /// </summary>
    private static bool MatchesSerializedPolicyGrammar(string value)
    {
        var index = 0;
        while (index < value.Length)
        {
            SkipWhitespace(value, ref index);
            while (index < value.Length && value[index] != ';' && !InfraAscii.IsAsciiWhitespace(value[index]))
            {
                if (!char.IsAsciiLetterOrDigit(value[index]) && value[index] != '-')
                    return false;

                index++;
            }

            SkipWhitespace(value, ref index);
            while (index < value.Length && value[index] != ';')
            {
                if (!InfraAscii.IsAsciiWhitespace(value[index]) && value[index] is < '!' or > '~' or ',')
                    return false;

                index++;
            }

            // Skip the semicolon
            index++;
        }

        return true;

        static void SkipWhitespace(string value, ref int index)
        {
            while (index < value.Length && InfraAscii.IsAsciiWhitespace(value[index]))
            {
                index++;
            }
        }
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-cspee/#process-response: whether the response of the navigation of a
    /// child navigable (<paramref name="responseUrl"/>, <paramref name="headers"/>, and the policies of its
    /// <c>Content-Security-Policy</c> headers) is blocked by its required CSP: the response accepts it with an
    /// <c>Allow-CSP-From</c> header whose value is <c>*</c> or the serialized origin of the request
    /// (<paramref name="requestOrigin"/>), or with policies that the required policy subsumes. The responses of the local
    /// schemes are not blocked.
    /// </summary>
    public static bool IsResponseBlockedByRequiredPolicy(UrlRecord responseUrl, IReadOnlyList<KeyValuePair<string, string>> headers, IReadOnlyList<ContentSecurityPolicy>? responsePolicies, string requiredPolicy, string requestOrigin)
    {
        if (AllowsBlanketEnforcement(responseUrl, headers, requestOrigin))
            return false;

        var origin = OriginRecord.FromUrl(responseUrl);
        return !Subsumes(Parse(requiredPolicy, reportOnly: false, fromMeta: false), responsePolicies, origin);
    }

    /// <summary>https://w3c.github.io/webappsec-cspee/#origin-allowed (Does response allow blanket enforcement of policy from request?)</summary>
    private static bool AllowsBlanketEnforcement(UrlRecord responseUrl, IReadOnlyList<KeyValuePair<string, string>> headers, string requestOrigin)
    {
        if (responseUrl.Scheme is "about" or "blob" or "data")
            return true;

        return HeaderList.Get(headers, "Allow-CSP-From") is { } allowCspFrom && (allowCspFrom == "*" || allowCspFrom == requestOrigin);
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-cspee/#subsume-csp-list: whether <paramref name="required"/> subsumes the enforced
    /// policies of <paramref name="policies"/> (their intersection allows nothing that the required policy does not allow).
    /// <c>'self'</c> is the origin of the response (<paramref name="origin"/>) in both policies. As in Chromium (and the
    /// web-platform-tests), the source lists are compared for each directive of <see cref="SubsumptionDirectives"/>, with
    /// their fallback lists: a required <c>default-src</c> directive constrains every fetch directive of the response.
    /// </summary>
    public static bool Subsumes(ContentSecurityPolicy required, IReadOnlyList<ContentSecurityPolicy>? policies, OriginRecord origin)
    {
        if (required.ReportOnly || required.Directives.Count == 0)
            return true;

        var enforcedPolicies = policies?.Where(policy => !policy.ReportOnly).ToList() ?? [];
        foreach (var directive in SubsumptionDirectives)
        {
            if (GetSubsumptionSourceList(required, directive) is not { } requiredSourceList)
                continue;

            EffectiveSourceList? intersection = null;
            foreach (var policy in enforcedPolicies)
            {
                if (GetSubsumptionSourceList(policy, directive) is { } sourceList)
                {
                    var effective = EffectiveSourceList.Create(sourceList, directive, origin);
                    intersection = intersection is null ? effective : EffectiveSourceList.Intersect(intersection, effective);
                }
            }

            if (intersection is null || !EffectiveSourceList.Create(requiredSourceList, directive, origin).Subsumes(intersection))
                return false;
        }

        // https://w3c.github.io/webappsec-cspee/#subsume-policy: the sandboxing flags set by the required policy must be set by
        // the policies of the response
        if (required.GetDirectiveValue("sandbox") is { } requiredSandbox)
        {
            var requiredFlags = Sandboxing.ParseSandboxingDirective(string.Join(' ', requiredSandbox));
            var flags = SandboxingFlags.None;
            foreach (var policy in enforcedPolicies)
            {
                if (policy.GetDirectiveValue("sandbox") is { } sandbox)
                {
                    flags |= Sandboxing.ParseSandboxingDirective(string.Join(' ', sandbox));
                }
            }

            if ((requiredFlags & ~flags) != SandboxingFlags.None)
                return false;
        }

        return !required.ContainsDirective("upgrade-insecure-requests") || enforcedPolicies.Exists(policy => policy.ContainsDirective("upgrade-insecure-requests"));
    }

    /// <summary>https://w3c.github.io/webappsec-cspee/#effective-directive-value: the value of the first directive of the fallback list of a directive.</summary>
    private static List<string>? GetSubsumptionSourceList(ContentSecurityPolicy policy, string directive)
    {
        string[] fallbackList = directive switch
        {
            "script-src" or "style-src" or "img-src" or "media-src" or "font-src" or "object-src" or "connect-src" or "manifest-src" => [directive, "default-src"],
            "base-uri" or "form-action" or "frame-ancestors" => [directive],
            _ => GetFallbackList(directive),
        };

        foreach (var name in fallbackList)
        {
            if (policy.GetDirectiveValue(name) is { } value)
                return value;
        }

        return null;
    }

    /// <summary>
    /// A scheme-source or a host-source of an effective source list (https://w3c.github.io/webappsec-cspee/#effective-source-list),
    /// whose scheme is set (a host-source without scheme has the scheme of the origin). <see cref="Host"/> is null for a
    /// scheme-source; <see cref="Port"/> is null for the default port of the scheme, or <c>*</c>.
    /// </summary>
    private sealed record UrlSourceExpression(string Scheme, string? Host, string? Port, string Path)
    {
        public bool IsSchemeSource => Host is null;

        private bool HasWildcardHost => Host is ['*', ..];

        /// <summary>The port of the expression, or the default port of its scheme (-1 when the scheme has none).</summary>
        private int EffectivePort => Port is null ? UrlSchemes.GetDefaultPort(Scheme) : int.TryParse(Port, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ? port : int.MaxValue;

        /// <summary>https://w3c.github.io/webappsec-cspee/#subsume-source-expressions (with the secure upgrades of the ports of the URL matching)</summary>
        public bool Subsumes(UrlSourceExpression other)
        {
            if (!SchemePartMatches(Scheme, other.Scheme))
                return false;

            if (IsSchemeSource)
                return true;

            if (other.IsSchemeSource)
                return false;

            if (other.HasWildcardHost)
            {
                if (!HasWildcardHost)
                    return false;

                if (Host != "*" && (other.Host == "*" || !IsSameOrSubdomain(other.Host![2..], Host![2..])))
                    return false;
            }
            else if (!HostPatternMatches(Host!, other.Host!))
            {
                return false;
            }

            if (other.Port == "*")
            {
                if (Port != "*")
                    return false;
            }
            else if (Port != "*" && !PortsMatch(this, other))
            {
                return false;
            }

            return PathPartMatches(Path, other.Path);
        }

        /// <summary>
        /// The intersection of two expressions (https://w3c.github.io/webappsec-cspee/#intersection-source-expression): the
        /// most restrictive scheme, host, port and path of the two, or null when they allow no common URL.
        /// </summary>
        public static UrlSourceExpression? Intersect(UrlSourceExpression a, UrlSourceExpression b)
        {
            // The scheme that the other one matches (e.g. https: for http: and https:)
            var scheme = SchemePartMatches(a.Scheme, b.Scheme) ? b.Scheme : SchemePartMatches(b.Scheme, a.Scheme) ? a.Scheme : null;
            if (scheme is null)
                return null;

            if (a.IsSchemeSource && b.IsSchemeSource)
                return new UrlSourceExpression(scheme, Host: null, Port: null, Path: "");

            if (a.IsSchemeSource)
                return b with { Scheme = scheme };

            if (b.IsSchemeSource)
                return a with { Scheme = scheme };

            var host = IntersectHosts(a.Host!, b.Host!);
            if (host is null)
                return null;

            string? port;
            if (a.Port == "*")
            {
                port = b.Port;
            }
            else if (b.Port == "*")
            {
                port = a.Port;
            }
            else if (PortsMatch(a, b))
            {
                port = SchemePartMatches(a.Scheme, b.Scheme) ? b.Port : a.Port;
            }
            else if (PortsMatch(b, a))
            {
                port = a.Port;
            }
            else
            {
                return null;
            }

            var path = PathPartMatches(a.Path, b.Path) ? b.Path : PathPartMatches(b.Path, a.Path) ? a.Path : null;
            if (path is null)
                return null;

            return new UrlSourceExpression(scheme, host, port, path);

            static string? IntersectHosts(string a, string b)
            {
                if (InfraAscii.EqualsIgnoreAsciiCase(a, b))
                    return a;

                var wildcardA = a is ['*', ..];
                var wildcardB = b is ['*', ..];
                if (wildcardA && wildcardB)
                {
                    if (a == "*")
                        return b;

                    if (b == "*")
                        return a;

                    return IsSameOrSubdomain(b[2..], a[2..]) ? b : IsSameOrSubdomain(a[2..], b[2..]) ? a : null;
                }

                if (wildcardA)
                    return HostPatternMatches(a, b) ? b : null;

                if (wildcardB)
                    return HostPatternMatches(b, a) ? a : null;

                return null;
            }
        }

        /// <summary>
        /// Whether the port of <paramref name="a"/> allows the port of <paramref name="b"/> (neither is a wildcard): the same
        /// port (the default port of the scheme when it is not specified), or the secure upgrade of the default port of an
        /// insecure scheme (<c>http://a.com</c> allows <c>https://a.com</c>, https://w3c.github.io/webappsec-csp/#port-part-matches).
        /// </summary>
        private static bool PortsMatch(UrlSourceExpression a, UrlSourceExpression b)
        {
            var portA = a.EffectivePort;
            var portB = b.EffectivePort;
            return portA == portB || (a.Scheme is "http" or "ws" && portA == 80 && b.Scheme is "https" or "wss" && portB == 443);
        }

        /// <summary>Whether <paramref name="host"/> is <paramref name="domain"/> or one of its subdomains (ASCII case-insensitive).</summary>
        private static bool IsSameOrSubdomain(string host, string domain)
            => InfraAscii.EqualsIgnoreAsciiCase(host, domain) || InfraAscii.ToAsciiLowercase(host).EndsWith("." + InfraAscii.ToAsciiLowercase(domain), StringComparison.Ordinal);

        /// <summary>https://w3c.github.io/webappsec-csp/#host-part-match, for a host that is not a wildcard</summary>
        private static bool HostPatternMatches(string pattern, string host)
        {
            if (pattern == "*")
                return true;

            if (pattern.StartsWith("*.", StringComparison.Ordinal))
                return InfraAscii.ToAsciiLowercase(host).EndsWith(InfraAscii.ToAsciiLowercase(pattern[1..]), StringComparison.Ordinal);

            return InfraAscii.EqualsIgnoreAsciiCase(pattern, host);
        }
    }

    /// <summary>
    /// The effective source list of a directive (https://w3c.github.io/webappsec-cspee/#effective-source-list): what a
    /// source list allows, with <c>'self'</c> and <c>*</c> expanded and the ineffective expressions removed (the nonces,
    /// the hashes and the keywords of the directives that are not script or style directives, <c>'unsafe-inline'</c> in the
    /// presence of a nonce or a hash, the URL expressions of a script directive with <c>'strict-dynamic'</c>).
    /// </summary>
    private sealed class EffectiveSourceList
    {
        /// <summary>Whether the source list allows all the inline behavior (https://w3c.github.io/webappsec-csp/#allow-all-inline).</summary>
        public bool AllowsAllInline { get; private set; }

        /// <summary>The keywords of <see cref="SubsumptionKeywords"/> of the source list (lowercase).</summary>
        public HashSet<string> Keywords { get; private set; } = new(StringComparer.Ordinal);

        /// <summary>The values of the nonce-sources.</summary>
        public HashSet<string> Nonces { get; private set; } = new(StringComparer.Ordinal);

        /// <summary>The hash-sources, as the lowercase algorithm and the base64 value (e.g. <c>sha256-abc=</c>).</summary>
        public HashSet<string> Hashes { get; private set; } = new(StringComparer.Ordinal);

        public List<UrlSourceExpression> UrlExpressions { get; private set; } = [];

        /// <summary>Whether the source list allows nothing (its effective source list is 'none').</summary>
        public bool IsNone => !AllowsAllInline && Nonces.Count == 0 && Hashes.Count == 0 && UrlExpressions.Count == 0 && Keywords.All(keyword => keyword == "'strict-dynamic'");

        public static EffectiveSourceList Create(List<string> sourceList, string directive, OriginRecord origin)
        {
            var result = new EffectiveSourceList();
            var isScript = directive is "script-src" or "script-src-elem" or "script-src-attr";
            var isStyle = directive is "style-src" or "style-src-elem" or "style-src-attr";
            if (sourceList.Count == 0 || (sourceList.Count == 1 && InfraAscii.EqualsIgnoreAsciiCase(sourceList[0], "'none'")))
                return result;

            // 'strict-dynamic' makes the URL expressions of a script directive ineffective, and of worker-src, whose pre-request
            // check is the one of the script directives (https://w3c.github.io/webappsec-csp/#script-pre-request)
            var strictDynamic = (isScript || directive == "worker-src") && ContainsKeyword(sourceList, "'strict-dynamic'");
            foreach (var expression in sourceList)
            {
                if (InfraAscii.EqualsIgnoreAsciiCase(expression, "'self'"))
                {
                    // https://w3c.github.io/webappsec-cspee/#rewrite-self
                    if (!strictDynamic && !origin.IsOpaque)
                    {
                        result.UrlExpressions.Add(new UrlSourceExpression(origin.Scheme!, origin.Host.Serialized, origin.Port < 0 ? null : origin.Port.ToString(CultureInfo.InvariantCulture), Path: ""));
                    }
                }
                else if (expression == "*")
                {
                    if (!strictDynamic)
                    {
                        foreach (var scheme in (ReadOnlySpan<string>)["ftp", "http", "https", "ws", "wss"])
                        {
                            result.UrlExpressions.Add(new UrlSourceExpression(scheme, Host: null, Port: null, Path: ""));
                        }

                        if (origin.Scheme is { } originScheme)
                        {
                            result.UrlExpressions.Add(new UrlSourceExpression(originScheme, Host: null, Port: null, Path: ""));
                        }
                    }
                }
                else if (TryParseSchemeSource(expression, out var schemeSource))
                {
                    if (!strictDynamic)
                    {
                        result.UrlExpressions.Add(new UrlSourceExpression(InfraAscii.ToAsciiLowercase(schemeSource), Host: null, Port: null, Path: ""));
                    }
                }
                else if (TryParseHostSource(expression, out var scheme, out var host, out var port, out var path))
                {
                    // A host-source without scheme matches the URLs of the scheme of the origin
                    if (!strictDynamic && (scheme ?? origin.Scheme) is { } hostSourceScheme)
                    {
                        result.UrlExpressions.Add(new UrlSourceExpression(InfraAscii.ToAsciiLowercase(hostSourceScheme), host, port, path));
                    }
                }
                else if (isScript || isStyle)
                {
                    if (TryGetPrefixedBase64Value(expression, "'nonce-", out var nonce))
                    {
                        result.Nonces.Add(nonce);
                    }
                    else if (TryGetHashSource(expression) is { } hash)
                    {
                        result.Hashes.Add(hash);
                    }
                    else if (Array.Find(SubsumptionKeywords, keyword => InfraAscii.EqualsIgnoreAsciiCase(keyword, expression)) is { } keyword && (isScript || keyword != "'strict-dynamic'"))
                    {
                        result.Keywords.Add(keyword);
                    }
                }
            }

            result.AllowsAllInline = (isScript || isStyle) && DoesSourceListAllowAllInlineBehavior(sourceList, isScript ? ContentSecurityPolicyInlineType.Script : ContentSecurityPolicyInlineType.Style);
            return result;

            static string? TryGetHashSource(string expression)
            {
                foreach (var algorithm in (ReadOnlySpan<string>)["sha256", "sha384", "sha512"])
                {
                    if (TryGetPrefixedBase64Value(expression, "'" + algorithm + "-", out var value))
                        return algorithm + "-" + value.Replace('-', '+').Replace('_', '/');
                }

                return null;
            }
        }

        /// <summary>
        /// https://w3c.github.io/webappsec-cspee/#intersection-source-list: what both source lists allow (the keywords, the
        /// nonces and the hashes that both contain, and the intersections of their URL expressions).
        /// </summary>
        public static EffectiveSourceList Intersect(EffectiveSourceList a, EffectiveSourceList b)
        {
            if (a.IsNone || b.IsNone)
                return new EffectiveSourceList();

            var urlExpressions = new List<UrlSourceExpression>();
            foreach (var expressionA in a.UrlExpressions)
            {
                foreach (var expressionB in b.UrlExpressions)
                {
                    if (UrlSourceExpression.Intersect(expressionA, expressionB) is { } intersection && !urlExpressions.Contains(intersection))
                    {
                        urlExpressions.Add(intersection);
                    }
                }
            }

            return new EffectiveSourceList
            {
                AllowsAllInline = a.AllowsAllInline && b.AllowsAllInline,
                Keywords = [.. a.Keywords.Intersect(b.Keywords, StringComparer.Ordinal)],
                Nonces = [.. a.Nonces.Intersect(b.Nonces, StringComparer.Ordinal)],
                Hashes = [.. a.Hashes.Intersect(b.Hashes, StringComparer.Ordinal)],
                UrlExpressions = urlExpressions,
            };
        }

        /// <summary>
        /// https://w3c.github.io/webappsec-cspee/#subsume-source-list: whether this source list allows everything that
        /// <paramref name="other"/> allows. A nonce is subsumed by any nonce (the value is not compared, so that an embedder
        /// cannot guess the nonce of the embedded document), a hash by the same hash.
        /// </summary>
        public bool Subsumes(EffectiveSourceList other)
        {
            if (other.IsNone)
                return true;

            if (IsNone || (other.AllowsAllInline && !AllowsAllInline) || !other.Keywords.IsSubsetOf(Keywords))
                return false;

            if ((other.Nonces.Count > 0 && Nonces.Count == 0) || !other.Hashes.IsSubsetOf(Hashes))
                return false;

            return other.UrlExpressions.TrueForAll(expression => UrlExpressions.Exists(candidate => candidate.Subsumes(expression)));
        }
    }
}
