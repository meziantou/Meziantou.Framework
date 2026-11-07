using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The policy object of a document or of an <c>iframe</c> element (https://w3c.github.io/webappsec-permissions-policy/#the-policy-object,
/// <c>document.permissionsPolicy</c> and <c>iframe.permissionsPolicy</c>): the introspection of the permissions policy of the
/// document, or of the policy that a document loaded into the iframe would inherit.
/// </summary>
/// <seealso href="https://w3c.github.io/webappsec-permissions-policy/#permissionspolicy"/>
[IdlInterface("PermissionsPolicy", "https://w3c.github.io/webappsec-permissions-policy/#permissionspolicy")]
public sealed class PermissionsPolicy
{
    // The associated node: a document, or an iframe element
    private readonly Document? _document;
    private readonly HtmlIFrameElement? _iframe;

    internal PermissionsPolicy(Document document) => _document = document;

    internal PermissionsPolicy(HtmlIFrameElement iframe) => _iframe = iframe;

    /// <summary>
    /// Returns whether a feature is allowed by the policy for an origin (https://w3c.github.io/webappsec-permissions-policy/#dom-permissionspolicy-allowsfeature).
    /// </summary>
    /// <param name="feature">The token of the feature (e.g. <c>fullscreen</c>).</param>
    /// <param name="origin">The serialization of an origin, or null for the origin of the document (the declared origin of the iframe).</param>
    /// <seealso href="https://w3c.github.io/webappsec-permissions-policy/#dom-permissionspolicy-allowsfeature"/>
    [IdlMember("allowsFeature", "https://w3c.github.io/webappsec-permissions-policy/#dom-permissionspolicy-allowsfeature")]
    public bool AllowsFeature(string feature, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (PolicyControlledFeature.Get(feature) is not { } policyControlledFeature)
            return false;

        if (origin is null)
            return IsAllowed(policyControlledFeature, GetDefaultOrigin());

        // An origin that cannot be parsed allows nothing
        return UrlParser.Parse(origin, baseUrl: null) is { } url && IsAllowed(policyControlledFeature, OriginRecord.FromUrl(url));
    }

    /// <summary>Returns the tokens of the supported features (https://w3c.github.io/webappsec-permissions-policy/#dom-permissionspolicy-features).</summary>
    /// <seealso href="https://w3c.github.io/webappsec-permissions-policy/#dom-permissionspolicy-features"/>
    [IdlMember("features", "https://w3c.github.io/webappsec-permissions-policy/#dom-permissionspolicy-features")]
    public IReadOnlyList<string> Features()
    {
        var supported = PolicyControlledFeature.Supported;
        var result = new string[supported.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = supported[i].Name;
        }

        return result;
    }

    /// <summary>
    /// Returns the tokens of the features allowed by the policy for the origin of the document, or for the declared origin
    /// of the iframe (https://w3c.github.io/webappsec-permissions-policy/#dom-permissionspolicy-allowedfeatures).
    /// </summary>
    /// <seealso href="https://w3c.github.io/webappsec-permissions-policy/#dom-permissionspolicy-allowedfeatures"/>
    [IdlMember("allowedFeatures", "https://w3c.github.io/webappsec-permissions-policy/#dom-permissionspolicy-allowedfeatures")]
    public IReadOnlyList<string> AllowedFeatures()
    {
        var result = new List<string>();
        foreach (var feature in PolicyControlledFeature.Supported)
        {
            // The declared origin of a sandboxed iframe is a new opaque origin for each feature
            if (IsAllowed(feature, GetDefaultOrigin()))
            {
                result.Add(feature.Name);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the allowlist of a feature (https://w3c.github.io/webappsec-permissions-policy/#dom-permissionspolicy-getallowlistforfeature):
    /// <c>*</c>, or the serialization of the origins and the source expressions of the declared policy of the document (of the
    /// <c>allow</c> attribute of the iframe). It is empty when the feature is not allowed. A feature that is not declared has
    /// its default allowlist: <c>*</c>, or the origin of the document (the declared origin of the iframe).
    /// </summary>
    /// <param name="feature">The token of the feature.</param>
    /// <seealso href="https://w3c.github.io/webappsec-permissions-policy/#dom-permissionspolicy-getallowlistforfeature"/>
    [IdlMember("getAllowlistForFeature", "https://w3c.github.io/webappsec-permissions-policy/#dom-permissionspolicy-getallowlistforfeature")]
    public IReadOnlyList<string> GetAllowlistForFeature(string feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        var origin = GetDefaultOrigin();
        if (PolicyControlledFeature.Get(feature) is not { } policyControlledFeature || !IsAllowed(policyControlledFeature, origin))
            return [];

        PermissionsPolicyAllowlist? allowlist = null;
        if (_document is not null)
        {
            _document.PermissionsPolicyRecord.Declared.Declarations.TryGetValue(policyControlledFeature, out allowlist);
        }
        else
        {
            PermissionsPolicyAlgorithms.ProcessPolicyAttributes(_iframe!)?.TryGetValue(policyControlledFeature, out allowlist);
        }

        // The specification only defines the allowlist of a declared feature: the others have their default allowlist
        if (allowlist is null)
            return policyControlledFeature.IsAllowedForAllByDefault ? ["*"] : origin.IsOpaque ? [] : [origin.Serialize()];

        if (allowlist.IsAll)
            return ["*"];

        var result = new List<string>();
        if (allowlist.SelfOrigin is { IsOpaque: false } selfOrigin)
        {
            result.Add(selfOrigin.Serialize());
        }

        if (allowlist.SrcOrigin is { IsOpaque: false } srcOrigin && !result.Contains(srcOrigin.Serialize()))
        {
            result.Add(srcOrigin.Serialize());
        }

        foreach (var expression in allowlist.Expressions)
        {
            if (!result.Contains(expression))
            {
                result.Add(expression);
            }
        }

        return result;
    }

    /// <summary>https://w3c.github.io/webappsec-permissions-policy/#default-origin</summary>
    private OriginRecord GetDefaultOrigin() => _document is not null ? _document.Origin : PermissionsPolicyAlgorithms.GetDeclaredOrigin(_iframe!);

    /// <summary>
    /// Whether a feature is allowed by the observable policy of the associated node for an origin
    /// (https://w3c.github.io/webappsec-permissions-policy/#observable-policy): the permissions policy of the document, or the
    /// policy that a document of that origin would inherit in the iframe.
    /// </summary>
    private bool IsAllowed(PolicyControlledFeature feature, OriginRecord origin)
    {
        if (_document is not null)
            return PermissionsPolicyAlgorithms.CheckPermissionsPolicy(_document.PermissionsPolicyRecord, feature, origin, _document.Origin);

        return PermissionsPolicyAlgorithms.DefineInheritedPolicy(feature, _iframe!, origin);
    }
}
