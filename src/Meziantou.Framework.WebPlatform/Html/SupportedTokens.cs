using System.Collections.Frozen;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>
/// The supported tokens of the DOMTokenList attributes (https://dom.spec.whatwg.org/#concept-supported-tokens), used by
/// <c>supports()</c>: the keywords that the library processes.
/// </summary>
internal static class SupportedTokens
{
    /// <summary>
    /// https://html.spec.whatwg.org/multipage/semantics.html#dom-link-rellist: the link types allowed on link elements that
    /// impact the processing model and that the library implements (the other ones, e.g. <c>preconnect</c> or
    /// <c>dns-prefetch</c>, are not processed: the loader has no connection to open in advance). The icons and the manifest
    /// are only fetched when the user agent deems it necessary, which it never does (https://html.spec.whatwg.org/multipage/links.html#rel-icon).
    /// </summary>
    public static readonly FrozenSet<string> LinkRel = FrozenSet.Create(StringComparer.Ordinal, "alternate", "icon", "manifest", "modulepreload", "prefetch", "preload", "stylesheet");

    /// <summary>https://html.spec.whatwg.org/multipage/text-level-semantics.html#dom-a-rellist: the a, area and form elements.</summary>
    public static readonly FrozenSet<string> HyperlinkRel = FrozenSet.Create(StringComparer.Ordinal, "noopener", "noreferrer", "opener");

    /// <summary>https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-iframe-sandbox</summary>
    public static readonly FrozenSet<string> Sandbox = FrozenSet.Create(
        StringComparer.Ordinal,
        "allow-downloads",
        "allow-forms",
        "allow-modals",
        "allow-orientation-lock",
        "allow-pointer-lock",
        "allow-popups",
        "allow-popups-to-escape-sandbox",
        "allow-presentation",
        "allow-same-origin",
        "allow-scripts",
        "allow-top-navigation",
        "allow-top-navigation-by-user-activation",
        "allow-top-navigation-to-custom-protocols");

    /// <summary>https://html.spec.whatwg.org/multipage/urls-and-fetching.html#blocking-attribute: the link, script and style elements.</summary>
    public static readonly FrozenSet<string> Blocking = FrozenSet.Create(StringComparer.Ordinal, "render");
}
