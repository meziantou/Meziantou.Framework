using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The flags of a sandboxing flag set (https://html.spec.whatwg.org/multipage/browsers.html#sandboxing-flag-set).</summary>
[Flags]
internal enum SandboxingFlags
{
    None = 0,

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#sandboxed-origin-browsing-context-flag: the document has an opaque origin.</summary>
    Origin = 1 << 0,

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#sandboxed-document.domain-browsing-context-flag</summary>
    DocumentDomain = 1 << 1,

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#sandboxed-navigation-browsing-context-flag: the document cannot navigate the other navigables (except its descendants).</summary>
    Navigation = 1 << 2,

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#sandboxed-auxiliary-navigation-browsing-context-flag: the document cannot create auxiliary browsing contexts (popups).</summary>
    AuxiliaryNavigation = 1 << 3,

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#sandboxed-top-level-navigation-without-user-activation-browsing-context-flag</summary>
    TopLevelNavigationWithoutUserActivation = 1 << 4,

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#sandboxed-top-level-navigation-with-user-activation-browsing-context-flag</summary>
    TopLevelNavigationWithUserActivation = 1 << 5,

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#sandboxed-plugins-browsing-context-flag</summary>
    Plugins = 1 << 6,

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#sandboxed-forms-browsing-context-flag</summary>
    Forms = 1 << 7,

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#sandboxed-pointer-lock-browsing-context-flag</summary>
    PointerLock = 1 << 8,

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#sandboxed-scripts-browsing-context-flag</summary>
    Scripts = 1 << 9,

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#sandboxed-automatic-features-browsing-context-flag</summary>
    AutomaticFeatures = 1 << 10,

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#sandbox-propagates-to-auxiliary-browsing-contexts-flag</summary>
    PropagatesToAuxiliaryBrowsingContexts = 1 << 11,

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#sandboxed-modals-flag</summary>
    Modals = 1 << 12,

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#sandboxed-orientation-lock-browsing-context-flag</summary>
    OrientationLock = 1 << 13,

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#sandboxed-presentation-browsing-context-flag</summary>
    Presentation = 1 << 14,

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#sandboxed-downloads-browsing-context-flag</summary>
    Downloads = 1 << 15,

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#sandboxed-custom-protocols-navigation-browsing-context-flag</summary>
    CustomProtocolsNavigation = 1 << 16,

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#sandboxed-storage-access-by-user-activation-flag</summary>
    StorageAccessByUserActivation = 1 << 17,
}

internal static class Sandboxing
{
    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsers.html#parse-a-sandboxing-directive: all the flags, except the ones
    /// whose keyword is in the unordered set of unique space-separated tokens (ASCII case-insensitive).
    /// </summary>
    public static SandboxingFlags ParseSandboxingDirective(string tokens)
    {
        // The tokens are ASCII-lowercased: an ordinal comparison (StringComparer.OrdinalIgnoreCase would match "allow-\u017Fcripts")
        var keywords = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in tokens.Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            keywords.Add(InfraAscii.ToAsciiLowercase(token));
        }

        var flags = SandboxingFlags.Navigation | SandboxingFlags.Plugins | SandboxingFlags.DocumentDomain;
        void SetUnless(SandboxingFlags flag, params string[] allowKeywords)
        {
            if (!allowKeywords.Any(keywords.Contains))
            {
                flags |= flag;
            }
        }

        SetUnless(SandboxingFlags.AuxiliaryNavigation, "allow-popups");
        SetUnless(SandboxingFlags.TopLevelNavigationWithoutUserActivation, "allow-top-navigation");
        SetUnless(SandboxingFlags.TopLevelNavigationWithUserActivation, "allow-top-navigation-by-user-activation", "allow-top-navigation");
        SetUnless(SandboxingFlags.Origin, "allow-same-origin");
        SetUnless(SandboxingFlags.Forms, "allow-forms");
        SetUnless(SandboxingFlags.PointerLock, "allow-pointer-lock");
        SetUnless(SandboxingFlags.Scripts, "allow-scripts");
        SetUnless(SandboxingFlags.AutomaticFeatures, "allow-scripts");
        SetUnless(SandboxingFlags.PropagatesToAuxiliaryBrowsingContexts, "allow-popups-to-escape-sandbox");
        SetUnless(SandboxingFlags.Modals, "allow-modals");
        SetUnless(SandboxingFlags.OrientationLock, "allow-orientation-lock");
        SetUnless(SandboxingFlags.Presentation, "allow-presentation");
        SetUnless(SandboxingFlags.Downloads, "allow-downloads");
        SetUnless(SandboxingFlags.CustomProtocolsNavigation, "allow-popups", "allow-top-navigation", "allow-top-navigation-by-user-activation", "allow-top-navigation-to-custom-protocols");
        SetUnless(SandboxingFlags.StorageAccessByUserActivation, "allow-storage-access-by-user-activation");
        return flags;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#allowed-to-navigate: whether the navigable of a document
    /// whose sandboxing flags are <paramref name="sourceFlags"/> can navigate (or traverse) <paramref name="target"/>.
    /// </summary>
    public static bool IsAllowedToNavigate(BrowsingContext source, BrowsingContext target, SandboxingFlags sourceFlags, bool hasTransientActivation)
    {
        if (ReferenceEquals(source, target) || IsAncestor(source, target))
            return true;

        if (IsAncestor(target, source))
        {
            // The algorithm allows any ancestor that is not a top-level traversable, but the sandboxed navigation browsing
            // context flag "prevents content from navigating browsing contexts other than the sandboxed browsing context
            // itself (or browsing contexts further nested inside it)", as in the browsers (WPT iframe_sandbox_navigate_ancestor-1.html)
            if (target.Parent is not null)
                return (sourceFlags & SandboxingFlags.Navigation) == SandboxingFlags.None;

            if (hasTransientActivation && (sourceFlags & SandboxingFlags.TopLevelNavigationWithUserActivation) != SandboxingFlags.None)
                return false;

            if (!hasTransientActivation && (sourceFlags & SandboxingFlags.TopLevelNavigationWithoutUserActivation) != SandboxingFlags.None)
                return false;

            return true;
        }

        // A top-level traversable: the one permitted sandboxed navigator of a popup (the sandboxed navigable that opened it) can navigate it
        if (target.Parent is null && (ReferenceEquals(target.OnePermittedSandboxedNavigator, source) || ReferenceEquals(target.Opener, source)))
            return true;

        return (sourceFlags & SandboxingFlags.Navigation) == SandboxingFlags.None;

        static bool IsAncestor(BrowsingContext ancestor, BrowsingContext navigable)
        {
            for (var current = navigable.Parent; current is not null; current = current.Parent)
            {
                if (ReferenceEquals(current, ancestor))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// The CSP-derived sandboxing flags (https://w3c.github.io/webappsec-csp/#sandbox-init) of the enforced policies of a
    /// <c>Content-Security-Policy</c> header: the flags of its <c>sandbox</c> directives.
    /// </summary>
    public static SandboxingFlags ParseContentSecurityPolicy(string serializedPolicyList)
    {
        var flags = SandboxingFlags.None;
        foreach (var policy in serializedPolicyList.Split(','))
        {
            // https://w3c.github.io/webappsec-csp/#parse-serialized-policy: the directives are separated by ";", the name is the first token
            foreach (var directive in policy.Split(';'))
            {
                var trimmed = directive.Trim(' ', '\t', '\n', '\f', '\r');
                var nameEnd = trimmed.IndexOfAny([' ', '\t', '\n', '\f', '\r']);
                var name = nameEnd < 0 ? trimmed : trimmed[..nameEnd];
                if (InfraAscii.EqualsIgnoreAsciiCase(name, "sandbox"))
                {
                    flags |= ParseSandboxingDirective(nameEnd < 0 ? "" : trimmed[nameEnd..]);
                }
            }
        }

        return flags;
    }
}
