using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Om;

namespace Meziantou.Framework.WebPlatform.Dom;

// https://drafts.csswg.org/cssom/#extensions-to-the-window-interface
public partial class Window
{
    /// <summary>
    /// Returns the resolved values of the properties of an element or of one of its pseudo-elements
    /// (https://drafts.csswg.org/cssom/#dom-window-getcomputedstyle). The declarations are read-only and live: they reflect
    /// the current state of the document and of its style sheets.
    /// </summary>
    /// <remarks>
    /// Nothing is rendered: the values that depend on the layout (e.g. a width in percentage or auto) are the computed values,
    /// the media queries and the viewport-relative lengths use <see cref="BrowsingContextOptions.Viewport"/>, and the
    /// declarations are empty when the element is not connected, not in the flat tree, or in a document without browsing
    /// context.
    /// </remarks>
    [IdlMember("getComputedStyle", "https://drafts.csswg.org/cssom/#dom-window-getcomputedstyle")]
    public CssStyleProperties GetComputedStyle(Element elt, string? pseudoElt = null)
    {
        ArgumentNullException.ThrowIfNull(elt);
        return new CssStyleProperties(parentRule: null) { IsReadOnly = true, Computed = ComputedStyleDeclarations.Create(elt, pseudoElt) };
    }
}

public partial class Document
{
    private StyleEngine? _styleEngine;

    /// <summary>Returns the style engine that computes the styles of the elements of the document.</summary>
    internal StyleEngine GetStyleEngine() => _styleEngine ??= new StyleEngine(this);

    /// <summary>The style engine of the document, or null when no style was computed.</summary>
    internal StyleEngine? StyleEngineOrNull => _styleEngine;

    /// <summary>The last environment of the document returned by <see cref="StyleEngine.CreateEnvironment"/>, reused while the viewport is the same.</summary>
    internal MediaEnvironment? LastMediaEnvironment { get; set; }

    /// <summary>
    /// The custom properties registered by <c>CSS.registerProperty()</c>, by name (the [[registeredPropertySet]] slot,
    /// https://drafts.css-houdini.org/css-properties-values-api/#dom-window-registeredpropertyset-slot). They take precedence
    /// over the @property rules.
    /// </summary>
    internal Dictionary<string, CustomPropertyRegistration> RegisteredPropertySet => field ??= new(StringComparer.Ordinal);
}
