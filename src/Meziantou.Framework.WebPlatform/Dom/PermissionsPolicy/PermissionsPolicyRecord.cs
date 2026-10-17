using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// An allowlist of a permissions policy (https://w3c.github.io/webappsec-permissions-policy/#allowlists): the special value
/// <c>*</c>, or the source expressions, the self-origin and the src-origin that the keywords of its text referred to.
/// </summary>
internal sealed class PermissionsPolicyAllowlist
{
    /// <summary>The special value <c>*</c>, which represents every origin.</summary>
    public static PermissionsPolicyAllowlist All { get; } = new() { IsAll = true };

    public bool IsAll { get; private init; }

    /// <summary>The permissions-source-expressions (scheme sources and host sources), in order.</summary>
    public List<string> Expressions { get; } = [];

    /// <summary>The origin that <c>self</c> referred to, or null.</summary>
    public OriginRecord? SelfOrigin { get; set; }

    /// <summary>
    /// The origin that <c>'src'</c> referred to (the declared origin of the iframe), or null. The declared origin of a
    /// sandboxed iframe (or of a <c>data:</c> URL) is a new opaque origin, which is same origin with no document: as in
    /// Chromium, and as the web-platform-tests expect, it matches the opaque origin of the documents of the iframe, so that
    /// the <c>allow</c> attribute can delegate a feature to a sandboxed iframe.
    /// </summary>
    public OriginRecord? SrcOrigin { get; set; }

    /// <summary>https://w3c.github.io/webappsec-permissions-policy/#matches</summary>
    public bool Matches(OriginRecord origin)
    {
        if (IsAll)
            return true;

        if (SelfOrigin is not null && OriginRecord.IsSameOriginDomain(SelfOrigin, origin))
            return true;

        if (SrcOrigin is not null && (OriginRecord.IsSameOriginDomain(SrcOrigin, origin) || (SrcOrigin.IsOpaque && origin.IsOpaque)))
            return true;

        if (origin.IsOpaque || Expressions.Count == 0 || UrlParser.Parse(origin.Serialize(), baseUrl: null) is not { } url)
            return false;

        foreach (var expression in Expressions)
        {
            if (ContentSecurityPolicy.DoesUrlMatchSourceExpression(url, expression, origin))
                return true;
        }

        return false;
    }
}

/// <summary>
/// A declared policy (https://w3c.github.io/webappsec-permissions-policy/#declared-policy): the allowlists of the features
/// declared by a <c>Permissions-Policy</c> (or <c>Permissions-Policy-Report-Only</c>) header, and the names of the reporting
/// endpoints of their <c>report-to</c> parameters.
/// </summary>
internal sealed class DeclaredPermissionsPolicy
{
    public static DeclaredPermissionsPolicy Empty { get; } = new();

    public Dictionary<PolicyControlledFeature, PermissionsPolicyAllowlist> Declarations { get; } = [];

    public Dictionary<PolicyControlledFeature, string> ReportingConfiguration { get; } = [];
}

/// <summary>
/// A permissions policy (https://w3c.github.io/webappsec-permissions-policy/#permissions-policy): the inherited policy of a
/// document (whether each supported feature is enabled by its ancestors and by the container policy of its navigable), and
/// its declared policy. See <see cref="PermissionsPolicyAlgorithms"/>.
/// </summary>
internal sealed class PermissionsPolicyRecord
{
    private readonly HashSet<PolicyControlledFeature>? _disabledByInheritance;

    public PermissionsPolicyRecord(HashSet<PolicyControlledFeature>? disabledByInheritance, DeclaredPermissionsPolicy declared)
    {
        _disabledByInheritance = disabledByInheritance is { Count: > 0 } ? disabledByInheritance : null;
        Declared = declared;
    }

    /// <summary>
    /// The empty permissions policy (https://w3c.github.io/webappsec-permissions-policy/#empty-permissions-policy): every
    /// supported feature is enabled by inheritance, and nothing is declared.
    /// </summary>
    public static PermissionsPolicyRecord Empty { get; } = new(null, DeclaredPermissionsPolicy.Empty);

    public DeclaredPermissionsPolicy Declared { get; }

    /// <summary>Whether the inherited policy for a feature is "Enabled" (https://w3c.github.io/webappsec-permissions-policy/#inherited-policy-for-a-feature).</summary>
    public bool IsEnabledByInheritance(PolicyControlledFeature feature) => _disabledByInheritance?.Contains(feature) != true;

    /// <summary>The same inherited policy, with another declared policy.</summary>
    public PermissionsPolicyRecord WithDeclared(DeclaredPermissionsPolicy declared) => new(_disabledByInheritance, declared);
}

/// <summary>
/// The algorithms of Permissions Policy (https://w3c.github.io/webappsec-permissions-policy/#algorithms): the policy of a
/// document is created when the document is (<see cref="CreateFromResponse"/>) from the policy of the document of its
/// container, the <c>allow</c> attribute of the container and the <c>Permissions-Policy</c> header of its response. The
/// features check it with <see cref="IsAllowedToUse"/>.
/// </summary>
internal static class PermissionsPolicyAlgorithms
{
    private static readonly char[] AsciiWhitespace = [' ', '\t', '\n', '\f', '\r'];

    /// <summary>https://w3c.github.io/webappsec-permissions-policy/#process-response-policy</summary>
    public static DeclaredPermissionsPolicy ProcessResponsePolicy(IReadOnlyList<KeyValuePair<string, string>>? headers, OriginRecord origin, bool reportOnly)
    {
        if (headers is null || StructuredFields.GetDictionary(headers, reportOnly ? "Permissions-Policy-Report-Only" : "Permissions-Policy") is not { Count: > 0 } dictionary)
            return DeclaredPermissionsPolicy.Empty;

        return ConstructPolicy(dictionary, origin);
    }

    /// <summary>https://w3c.github.io/webappsec-permissions-policy/#construct-policy</summary>
    public static DeclaredPermissionsPolicy ConstructPolicy(List<KeyValuePair<string, StructuredFieldMember>> dictionary, OriginRecord origin)
    {
        var policy = new DeclaredPermissionsPolicy();
        foreach (var (featureName, member) in dictionary)
        {
            if (PolicyControlledFeature.Get(featureName) is not { } feature)
                continue;

            if (member.GetParameter("report-to") is StructuredFieldToken endpoint)
            {
                policy.ReportingConfiguration[feature] = endpoint.Value;
            }

            // A member value that is not a token, a string or an inner list (e.g. a boolean: a member without value) is
            // ignored with its member (https://w3c.github.io/webappsec-permissions-policy/#structured-header-serialization)
            IReadOnlyList<StructuredFieldItem> values;
            if (member.InnerList is { } innerList)
            {
                values = innerList;
            }
            else if (member.Item is { Value: StructuredFieldToken or string } item)
            {
                values = [item];
            }
            else
            {
                policy.ReportingConfiguration.Remove(feature);
                continue;
            }

            var allowlist = new PermissionsPolicyAllowlist();
            foreach (var value in values)
            {
                if (value.Value is StructuredFieldToken { Value: "*" })
                {
                    allowlist = PermissionsPolicyAllowlist.All;
                    break;
                }

                if (value.Value is StructuredFieldToken { Value: "self" })
                {
                    allowlist.SelfOrigin = origin;
                }
                else if (value.Value is string expression && ContentSecurityPolicy.IsSchemeOrHostSource(expression))
                {
                    allowlist.Expressions.Add(expression);
                }
            }

            policy.Declarations[feature] = allowlist;
        }

        return policy;
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-permissions-policy/#parse-policy-directive: the policy directive of an <c>allow</c>
    /// attribute. <paramref name="targetOrigin"/> is the origin that <c>'src'</c> (the default allowlist) refers to.
    /// </summary>
    public static Dictionary<PolicyControlledFeature, PermissionsPolicyAllowlist> ParsePolicyDirective(string value, OriginRecord containerOrigin, OriginRecord? targetOrigin)
    {
        var directive = new Dictionary<PolicyControlledFeature, PermissionsPolicyAllowlist>();
        foreach (var serializedDeclaration in value.Split(';'))
        {
            var tokens = serializedDeclaration.Split(AsciiWhitespace, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0 || PolicyControlledFeature.Get(tokens[0]) is not { } feature)
                continue;

            var targetList = tokens.AsSpan(1);
            var allowlist = new PermissionsPolicyAllowlist();
            if (targetList.Contains("*"))
            {
                allowlist = PermissionsPolicyAllowlist.All;
            }
            else
            {
                if (targetList.IsEmpty && targetOrigin is not null)
                {
                    allowlist.SrcOrigin = targetOrigin;
                }

                foreach (var element in targetList)
                {
                    if (InfraAscii.EqualsIgnoreAsciiCase(element, "'self'"))
                    {
                        allowlist.SelfOrigin = containerOrigin;
                    }
                    else if (targetOrigin is not null && InfraAscii.EqualsIgnoreAsciiCase(element, "'src'"))
                    {
                        allowlist.SrcOrigin = targetOrigin;
                    }
                    else if (UrlParser.Parse(element, baseUrl: null) is { } url && OriginRecord.FromUrl(url) is { IsOpaque: false } target && !target.Host.Serialized.Contains('*', StringComparison.Ordinal))
                    {
                        // The serialization of an origin is a source expression that only matches that origin. The URL
                        // parser accepts "*" in a host, but such an origin would be a wildcard source expression: the
                        // wildcards are only supported in the header, as in Chromium (the only implementation of them)
                        allowlist.Expressions.Add(target.Serialize());
                    }
                }
            }

            directive[feature] = allowlist;
        }

        return directive;
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-permissions-policy/#process-policy-attributes: the container policy of a navigable
    /// container, which only an <c>iframe</c> element has (its <c>allow</c> and <c>allowfullscreen</c> attributes).
    /// </summary>
    public static Dictionary<PolicyControlledFeature, PermissionsPolicyAllowlist>? ProcessPolicyAttributes(Element element)
    {
        if (element is not HtmlIFrameElement)
            return null;

        var allow = element.GetAttributeValue("allow");
        var allowFullscreen = element.HasAttribute("allowfullscreen");
        if (allow is null && !allowFullscreen)
            return null;

        var containerPolicy = ParsePolicyDirective(allow ?? "", element.NodeDocument.Origin, GetDeclaredOrigin(element));
        if (allowFullscreen && PolicyControlledFeature.Get("fullscreen") is { } fullscreen)
        {
            containerPolicy.TryAdd(fullscreen, PermissionsPolicyAllowlist.All);
        }

        return containerPolicy;
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-permissions-policy/#declared-origin: the origin of the document that the embedding
    /// page intends to load into an iframe (a new opaque origin for a sandboxed iframe, see <see cref="PermissionsPolicyAllowlist.SrcOrigin"/>).
    /// </summary>
    public static OriginRecord GetDeclaredOrigin(Element node)
    {
        var document = node.NodeDocument;
        if ((document.ActiveSandboxingFlags & SandboxingFlags.Origin) != SandboxingFlags.None)
            return OriginRecord.CreateOpaque();

        if (node.GetAttributeValue("sandbox") is { } sandbox && (Sandboxing.ParseSandboxingDirective(sandbox) & SandboxingFlags.Origin) != SandboxingFlags.None)
            return OriginRecord.CreateOpaque();

        if (node.HasAttribute("srcdoc"))
            return document.Origin;

        if (node.GetAttributeValue("src") is { } src && Reflection.EncodingParseUrl(document, src) is { } url)
            return OriginRecord.FromUrl(url);

        return document.Origin;
    }

    /// <summary>https://w3c.github.io/webappsec-permissions-policy/#create-for-navigable</summary>
    public static PermissionsPolicyRecord CreateForNavigable(Element? container, OriginRecord origin)
    {
        if (container is null)
            return PermissionsPolicyRecord.Empty;

        // The container policy is the same for every feature: it is processed once
        var containerPolicy = ProcessPolicyAttributes(container);
        HashSet<PolicyControlledFeature>? disabled = null;
        foreach (var feature in PolicyControlledFeature.Supported)
        {
            if (!DefineInheritedPolicy(feature, container, containerPolicy, origin, reportOnly: false))
            {
                (disabled ??= []).Add(feature);
            }
        }

        return disabled is null ? PermissionsPolicyRecord.Empty : new PermissionsPolicyRecord(disabled, DeclaredPermissionsPolicy.Empty);
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-permissions-policy/#create-from-response: the policy of a navigable
    /// (<see cref="CreateForNavigable"/>) with the declarations of the <c>Permissions-Policy</c> header of a response, or of its
    /// <c>Permissions-Policy-Report-Only</c> header for the report-only policy of the document.
    /// </summary>
    public static PermissionsPolicyRecord CreateFromResponse(PermissionsPolicyRecord policy, OriginRecord origin, IReadOnlyList<KeyValuePair<string, string>>? headers, bool reportOnly)
    {
        var declared = ProcessResponsePolicy(headers, origin, reportOnly);
        if (ReferenceEquals(declared, DeclaredPermissionsPolicy.Empty))
            return policy;

        // A document cannot declare a feature that its inherited policy disables
        foreach (var feature in declared.Declarations.Keys.ToArray())
        {
            if (!policy.IsEnabledByInheritance(feature))
            {
                declared.Declarations.Remove(feature);
            }
        }

        return policy.WithDeclared(declared);
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-permissions-policy/#define-inherited-policy-in-container: whether the inherited policy
    /// for a feature is "Enabled" for a document of <paramref name="origin"/> in the navigable of a container.
    /// </summary>
    public static bool DefineInheritedPolicy(PolicyControlledFeature feature, Element container, OriginRecord origin, bool reportOnly = false)
        => DefineInheritedPolicy(feature, container, ProcessPolicyAttributes(container), origin, reportOnly);

    private static bool DefineInheritedPolicy(PolicyControlledFeature feature, Element container, Dictionary<PolicyControlledFeature, PermissionsPolicyAllowlist>? containerPolicy, OriginRecord origin, bool reportOnly)
    {
        var containerDocument = container.NodeDocument;
        if (!GetFeatureValueForOrigin(feature, containerDocument, containerDocument.Origin, reportOnly))
            return false;

        if (!GetFeatureValueForOrigin(feature, containerDocument, origin, reportOnly))
            return false;

        if (containerPolicy is not null && containerPolicy.TryGetValue(feature, out var allowlist))
            return allowlist.Matches(origin);

        return feature.IsAllowedForAllByDefault || OriginRecord.IsSameOrigin(origin, containerDocument.Origin);
    }

    /// <summary>https://w3c.github.io/webappsec-permissions-policy/#get-feature-value-for-origin</summary>
    private static bool GetFeatureValueForOrigin(PolicyControlledFeature feature, Document document, OriginRecord origin, bool reportOnly)
    {
        var policy = reportOnly ? document.ReportOnlyPermissionsPolicyRecord : document.PermissionsPolicyRecord;
        if (!policy.IsEnabledByInheritance(feature))
            return false;

        return !policy.Declared.Declarations.TryGetValue(feature, out var allowlist) || allowlist.Matches(origin);
    }

    /// <summary>https://w3c.github.io/webappsec-permissions-policy/#check-permissions-policy</summary>
    public static bool CheckPermissionsPolicy(PermissionsPolicyRecord policy, PolicyControlledFeature feature, OriginRecord origin, OriginRecord documentOrigin)
    {
        if (!policy.IsEnabledByInheritance(feature))
            return false;

        if (policy.Declared.Declarations.TryGetValue(feature, out var allowlist))
            return allowlist.Matches(origin);

        return feature.IsAllowedForAllByDefault || OriginRecord.IsSameOrigin(origin, documentOrigin);
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-permissions-policy/#is-feature-enabled: whether a feature is enabled in a document
    /// for an origin. With <paramref name="report"/> (an attempt to use the feature, not a query of its state), a violation
    /// of the policy, or of the report-only policy, is reported.
    /// </summary>
    public static bool IsFeatureEnabled(PolicyControlledFeature feature, Document document, OriginRecord origin, bool report = true)
    {
        var policy = document.PermissionsPolicyRecord;
        var result = CheckPermissionsPolicy(policy, feature, origin, document.Origin);
        if (!report)
            return result;

        if (!result)
        {
            ReportViolation(feature, document, "enforce", policy, potentialViolationOf: null);
        }
        else if (document.ReportOnlyPermissionsPolicyRecord is var reportOnlyPolicy && reportOnlyPolicy.Declared.Declarations.Count > 0 && !CheckPermissionsPolicy(reportOnlyPolicy, feature, origin, document.Origin))
        {
            ReportViolation(feature, document, "report", reportOnlyPolicy, potentialViolationOf: null);
        }

        return result;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/iframe-embed-object.html#allowed-to-use: whether a document is allowed to use a
    /// policy-controlled feature. Every feature of the library is checked by this method. A document without browsing context
    /// or that is not fully active is not allowed to use any feature.
    /// </summary>
    /// <param name="document">The document that uses the feature.</param>
    /// <param name="feature">The feature.</param>
    /// <param name="report">false when the state of the feature is queried: no violation is reported.</param>
    public static bool IsAllowedToUse(Document document, PolicyControlledFeature feature, bool report = true)
    {
        if (document.BrowsingContext is null || !document.IsFullyActive)
            return false;

        return IsFeatureEnabled(feature, document, document.Origin, report);
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-permissions-policy/#check-potential-violation-in-container: reports the features that
    /// the policy of the document of an iframe, or its report-only policy, disables for the declared origin of the iframe
    /// (run by the iframe load event steps). Only the features that the <c>allow</c> attribute delegates are reported, as in
    /// Chromium: the features that are disabled in a cross-origin iframe by their default allowlist are not violations.
    /// </summary>
    public static void CheckPotentialViolationInContainer(HtmlIFrameElement container)
    {
        var document = container.NodeDocument;
        if (document.RelevantWindow is null || ProcessPolicyAttributes(container) is not { Count: > 0 } containerPolicy)
            return;

        var hasReportOnlyPolicy = document.ReportOnlyPermissionsPolicyRecord.Declared.Declarations.Count > 0;
        foreach (var feature in containerPolicy.Keys)
        {
            // The declared origin of a sandboxed iframe is a new opaque origin each time: it is computed for each check
            if (!DefineInheritedPolicy(feature, container, containerPolicy, GetDeclaredOrigin(container), reportOnly: false))
            {
                ReportViolation(feature, document, "enforce", document.PermissionsPolicyRecord, container);
            }
            else if (hasReportOnlyPolicy && !DefineInheritedPolicy(feature, container, containerPolicy, GetDeclaredOrigin(container), reportOnly: true))
            {
                ReportViolation(feature, document, "report", document.ReportOnlyPermissionsPolicyRecord, container);
            }
        }
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-permissions-policy/#report-permissions-policy-violation and
    /// https://w3c.github.io/webappsec-permissions-policy/#report-potential-permissions-policy-violation: generates and queues
    /// a report for the window of the document, for the endpoint that the policy names for the feature (a report without
    /// endpoint is only given to the reporting observers).
    /// </summary>
    private static void ReportViolation(PolicyControlledFeature feature, Document document, string disposition, PermissionsPolicyRecord policy, HtmlIFrameElement? potentialViolationOf)
    {
        if (document.RelevantWindow is not { } window)
            return;

        // The location of the running script, when the feature is used by a script of the window (the engine is only
        // queried from the thread of the event loop of the window)
        var location = GlobalScope.CurrentWorkerGlobal is null ? window.ScriptEngine?.GetCurrentScriptLocation() : null;
        var body = new PermissionsPolicyViolationReportBody
        {
            FeatureId = feature.Name,
            SourceFile = location is not { } scriptLocation ? null : UrlParser.Parse(scriptLocation.Url, baseUrl: null) is { } sourceUrl ? ContentSecurityPolicyViolation.StripUrlForUseInReports(sourceUrl) : scriptLocation.Url,
            LineNumber = location?.LineNumber,
            ColumnNumber = location?.ColumnNumber,
            Disposition = disposition,
            IsPotentialViolation = potentialViolationOf is not null,
            AllowAttribute = potentialViolationOf?.GetAttributeValue("allow"),
            SrcAttribute = potentialViolationOf?.GetAttributeValue("src"),
        };

        window.QueueReport(body, potentialViolationOf is null ? "permissions-policy-violation" : "potential-permissions-policy-violation", policy.Declared.ReportingConfiguration.GetValueOrDefault(feature));
    }
}
