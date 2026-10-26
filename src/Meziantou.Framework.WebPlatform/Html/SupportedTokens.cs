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
    /// impact the processing model and that the library implements, and the ones that the browsers support:
    /// <list type="bullet">
    /// <item><c>preconnect</c> and <c>dns-prefetch</c> are processed without opening a connection or resolving a host name
    /// in advance (the loader has neither a connection pool nor a DNS cache), and <c>next</c> is processed as a preconnect
    /// (https://html.spec.whatwg.org/multipage/links.html#link-type-next lets the user agent choose);</item>
    /// <item><c>prerender</c> (removed from the HTML Standard) is processed as a prefetch, as in the browsers;</item>
    /// <item><c>apple-touch-icon</c> and <c>apple-touch-icon-precomposed</c> are icons, and <c>canonical</c>
    /// (https://www.rfc-editor.org/rfc/rfc6596) has no processing model.</item>
    /// </list>
    /// The icons and the manifest are only fetched when the user agent deems it necessary, which it never does
    /// (https://html.spec.whatwg.org/multipage/links.html#rel-icon).
    /// </summary>
    public static readonly FrozenSet<string> LinkRel = FrozenSet.Create(
        StringComparer.Ordinal,
        "alternate",
        "apple-touch-icon",
        "apple-touch-icon-precomposed",
        "canonical",
        "dns-prefetch",
        "expect",
        "icon",
        "manifest",
        "modulepreload",
        "next",
        "preconnect",
        "prefetch",
        "preload",
        "prerender",
        "stylesheet");

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/text-level-semantics.html#dom-a-rellist: the a, area and form elements, and, as in
    /// the browsers, the SVG a element (SVG 2 does not define its supported tokens; it follows its hyperlink with the same
    /// noopener and noreferrer processing).
    /// </summary>
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
