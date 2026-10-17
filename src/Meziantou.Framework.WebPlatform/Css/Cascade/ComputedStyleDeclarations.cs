using System.Text;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// The declarations of a computed style declaration block returned by getComputedStyle()
/// (https://drafts.csswg.org/cssom/#dom-window-getcomputedstyle): the resolved values of all the longhands, in
/// lexicographical order. The declarations are live: they reflect the current state of the document.
/// </summary>
internal sealed class ComputedStyleDeclarations
{
    /// <summary>The supported longhands: the standard properties in lexicographical order, then the vendor-prefixed properties.</summary>
    private static readonly Lazy<string[]> LonghandNames = new(() =>
        [.. CssPropertyRegistry.Properties
            .Where(p => p.AliasOf is null && !p.IsShorthand)
            .Select(p => p.Name)
            .OrderBy(name => name.StartsWith('-', StringComparison.Ordinal) ? 1 : 0)
            .ThenBy(name => name, StringComparer.Ordinal)]);

    private readonly Element _element;
    private readonly string? _pseudoElement;
    private readonly bool _isEmpty;

    private ComputedStyleDeclarations(Element element, string? pseudoElement, bool isEmpty)
    {
        _element = element;
        _pseudoElement = pseudoElement;
        _isEmpty = isEmpty;
    }

    /// <summary>Creates the declarations of getComputedStyle(elt, pseudoElt).</summary>
    public static ComputedStyleDeclarations Create(Element element, string? pseudoElement)
    {
        // https://drafts.csswg.org/cssom/#dom-window-getcomputedstyle: a value that does not start with a colon is ignored
        if (string.IsNullOrEmpty(pseudoElement) || pseudoElement[0] != ':')
            return new ComputedStyleDeclarations(element, pseudoElement: null, isEmpty: false);

        var name = ParsePseudoElement(pseudoElement);

        // A select rendered as a list box (https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2: its multiple
        // attribute is present or its display size is greater than 1) has no select popover, so it has no ::picker(select)
        if (name == "picker(select)" && element is HtmlSelectElement select && (select.IsMultiple || select.DisplaySize > 1))
            return new ComputedStyleDeclarations(element, name, isEmpty: true);

        // https://drafts.csswg.org/css-overflow-5/#scroll-buttons: the argument designates one of the four scroll buttons of the
        // element (a physical direction, the button of that direction in the writing mode of the element)
        if (name is not null && ScrollControls.IsScrollButton(name))
            return new ComputedStyleDeclarations(element, name, isEmpty: ScrollControls.Canonicalize(name, static () => default) is null);

        return new ComputedStyleDeclarations(element, name, isEmpty: name is null);
    }

    /// <summary>Parses a &lt;pseudo-element-selector&gt;; returns its name (with its argument, e.g. "highlight(name)") or null when it is not a supported pseudo-element.</summary>
    internal static string? ParsePseudoElement(string text)
    {
        // The whole value must be the pseudo-element (whitespace or any other token around it is invalid)
        var values = CssParser.ParseListOfComponentValues(text);
        if (values.Count == 0 || values[0] is CssPreservedToken { Type: CssTokenType.Whitespace } || values[^1] is CssPreservedToken { Type: CssTokenType.Whitespace })
            return null;

        var selectors = SelectorParser.ParseSelector(values);
        if (selectors is not { Selectors: [{ Compounds: [{ Selectors: [PseudoElementSelector pseudo] }] }] })
            return null;

        // The pseudo-elements whose argument does not designate a single pseudo-element
        if (pseudo.IsUniversalHighlight)
            return null;

        if (pseudo.Name.StartsWith("view-transition-", StringComparison.Ordinal) && pseudo.ArgumentText is null or "*")
            return null;

        // The legacy single-colon syntax is only valid for the pseudo-elements of CSS 2
        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith("::", StringComparison.Ordinal) && pseudo.Name is not ("before" or "after" or "first-line" or "first-letter"))
            return null;

        if (pseudo.Name is "slotted" or "part")
            return null;

        // The -webkit- prefixed aliases (e.g. ::-webkit-file-upload-button) are only supported in the selectors, as in browsers
        if (trimmed.TrimStart(':').StartsWith("-webkit-", StringComparison.OrdinalIgnoreCase))
            return null;

        return pseudo.ArgumentText is { } argument ? pseudo.Name + "(" + argument + ")" : pseudo.Name;
    }

    /// <summary>
    /// Returns the style of the element, or null when the declarations are empty (the element is not rendered). The layout is
    /// computed first when the style may depend on the size of a size container (see <see cref="ContainerQueries.GetStyleWithContainerLayout"/>).
    /// </summary>
    internal ComputedStyle? GetStyle()
    {
        if (_isEmpty || !IsRendered(_element))
            return null;

        var pseudoElement = _pseudoElement;
        if (ScrollControls.IsScrollButton(pseudoElement))
        {
            // The declarations are live: the button of a physical direction follows the writing mode of the element
            var element = _element;
            pseudoElement = ScrollControls.Canonicalize(pseudoElement!, () => WritingDirection.FromStyle(element.NodeDocument.GetStyleEngine().GetStyle(element).Layout));

            // The style of a button depends on whether the element can be scrolled in its direction (:enabled and :disabled),
            // which the layout determines
            if (element.NodeDocument.BrowsingContext is not null)
            {
                element.NodeDocument.GetLayoutEngine().EnsureLayout();
            }
        }
        else if (pseudoElement == ScrollControls.Marker)
        {
            // https://drafts.csswg.org/css-overflow-5/#scroll-marker-pseudo: a ::scroll-marker only exists in the
            // ::scroll-marker-group of its nearest ancestor scroll container (as in Chrome, the declarations are empty otherwise);
            // its style depends on the active marker of its group (:target-current)
            if (_element.NodeDocument.BrowsingContext is null || !_element.NodeDocument.GetLayoutEngine().HasScrollMarker(_element, pseudoElement!))
                return null;
        }

        return ContainerQueries.GetStyleWithContainerLayout(_element, pseudoElement);
    }

    /// <summary>
    /// Returns whether the element is connected, part of the flat tree, and its shadow-including root has a browsing context
    /// whose container (if any) is being rendered (https://drafts.csswg.org/cssom/#dom-window-getcomputedstyle).
    /// </summary>
    internal static bool IsRendered(Element element)
    {
        if (!element.IsConnected || !IsInFlatTree(element))
            return false;

        if (element.GetShadowIncludingRoot() is not Document document)
            return false;

        // The document of an SVG image is rendered in the image (https://svgwg.org/svg2-draft/conform.html#secure-animated-mode)
        if (document.BrowsingContext is not { } browsingContext)
            return document.SvgImage is not null;

        if (browsingContext.Container is not { } container)
            return true;

        // The container is being rendered: it is rendered and none of its ancestors has display: none
        if (!IsRendered(container))
            return false;

        var engine = container.NodeDocument.GetStyleEngine();
        for (Element? ancestor = container; ancestor is not null; ancestor = StyleEngine.GetInheritanceParent(ancestor))
        {
            if (engine.GetStyle(ancestor).GetComputedValue("display") == "none")
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns whether the element is being rendered (https://html.spec.whatwg.org/multipage/rendering.html#being-rendered):
    /// it is rendered (see <see cref="IsRendered"/>) and it generates boxes (see <see cref="BoxGeneration"/>): neither it nor
    /// one of its ancestors in the flat tree has <c>display: none</c>, it does not have <c>display: contents</c>, and it is
    /// not in the contents of a replaced element or in a never-rendered SVG element.
    /// </summary>
    internal static bool IsBeingRendered(Element element) => BoxGeneration.Get(element).IsBeingRendered;

    /// <summary>
    /// Returns whether the element is in the flat tree (https://drafts.csswg.org/css-scoping/#flat-tree): the children of a
    /// shadow host are in the flat tree only when they are assigned to a slot (which must be in the flat tree), and the
    /// children of a slot (its fallback content) only when no node is assigned to the slot.
    /// </summary>
    private static bool IsInFlatTree(Element element)
    {
        Node node = element;
        while (true)
        {
            if (node.Slottable?.AssignedSlot is { } slot)
            {
                node = slot;
                continue;
            }

            switch (node.ParentNode)
            {
                case null:
                    return true;
                case Element { AttachedShadowRoot: not null }:
                case HtmlSlotElement { AssignedNodesList.Count: > 0 }:
                    return false;
                case ShadowRoot shadowRoot:
                    node = shadowRoot.Host;
                    break;
                case var parent:
                    node = parent;
                    break;
            }
        }
    }

    /// <summary>The number of declarations: the longhands, then the custom properties.</summary>
    public int Count => GetStyle() is { } style ? LonghandNames.Value.Length + style.GetCustomPropertyNames().Count : 0;

    public string GetName(int index)
    {
        if (GetStyle() is not { } style)
            return "";

        var longhands = LonghandNames.Value;
        return index < longhands.Length ? longhands[index] : style.GetCustomPropertyNames()[index - longhands.Length];
    }

    public IEnumerable<string> Names => GetStyle() is { } style ? LonghandNames.Value.Concat(style.GetCustomPropertyNames()) : [];

    /// <summary>Returns the resolved value of a property (a longhand, a shorthand or a custom property), or the empty string.</summary>
    public string GetPropertyValue(string property)
    {
        if (GetStyle() is not { } style)
            return "";

        if (CssPropertyRegistry.IsCustomProperty(property))
            return style.GetResolvedValue(property);

        if (CssPropertyRegistry.Get(property) is not { } definition)
            return "";

        if (definition.Name == "font")
            return SerializeFont(style);

        // The resolved value of animation-duration: auto (0s) is not a value the shorthand can set, so the shorthand is not checked by parsing it back
        if (definition.Name == "animation")
            return CssPropertyRules.SerializeResolvedAnimation(style.GetResolvedValue);

        // outline is serialized with its width, its style and its color, as in the browsers
        if (definition.Name == "outline")
            return style.GetResolvedValue("outline-width") + " " + style.GetResolvedValue("outline-style") + " " + style.GetResolvedValue("outline-color");

        // text-emphasis is serialized with its style and its color (https://drafts.csswg.org/css-text-decor-4/#text-emphasis-property)
        if (definition.Name == "text-emphasis")
            return style.GetResolvedValue("text-emphasis-style") + " " + style.GetResolvedValue("text-emphasis-color");

        if (definition.IsShorthand)
            return SerializeShorthand(style, definition);

        return style.GetResolvedValue(definition.Name);
    }

    // The closure is created in its own method: it would otherwise be allocated on each call of GetPropertyValue
    private static string SerializeShorthand(ComputedStyle style, CssPropertyDefinition definition)
    {
        // grid-template is serialized with the computed track lists (the used sizes of the tracks cannot represent the template
        // areas, whose strings name the tracks of the explicit grid only), but a track list of none, whose resolved value is the
        // sizes of the implicit tracks, as in Blink (WPT css/css-grid/grid-layout-properties.html)
        if (definition.Name == "grid-template")
        {
            return CssShorthands.Serialize(definition, name => new CssLonghandValue(name, name != "grid-template-areas" && style.GetComputedValue(name) is var computed and not "none" ? computed : style.GetResolvedValue(name)));
        }

        // The color of text-decoration is omitted when it is currentcolor, as in the browsers
        return CssShorthands.Serialize(definition, name => new CssLonghandValue(name, definition.Name == "text-decoration" && name == "text-decoration-color" ? style.GetComputedValue(name) : style.GetResolvedValue(name)));
    }

    private static readonly string[] FontVariantLonghands = ["font-variant-ligatures", "font-variant-alternates", "font-variant-numeric", "font-variant-east-asian", "font-variant-position", "font-variant-emoji"];

    // https://drafts.csswg.org/css-fonts/#font-stretch-prop: the <font-width-css3> keywords of the font shorthand
    private static readonly Dictionary<string, string> FontWidthKeywords = new(StringComparer.Ordinal)
    {
        ["50%"] = "ultra-condensed",
        ["62.5%"] = "extra-condensed",
        ["75%"] = "condensed",
        ["87.5%"] = "semi-condensed",
        ["100%"] = "normal",
        ["112.5%"] = "semi-expanded",
        ["125%"] = "expanded",
        ["150%"] = "extra-expanded",
        ["200%"] = "ultra-expanded",
    };

    /// <summary>
    /// Serializes the font shorthand from the resolved values of its longhands (https://drafts.csswg.org/cssom/#serialize-a-css-value):
    /// the values equal to normal are omitted, like the browsers. The empty string is returned when the longhands cannot
    /// be represented by the shorthand (a font-variant other than small-caps, a font width that is not a keyword, a longhand
    /// that the shorthand resets but cannot set whose value is not the initial value).
    /// </summary>
    private static string SerializeFont(ComputedStyle style)
    {
        foreach (var longhand in CssPropertyRegistry.GetResetOnlyLonghands("font"))
        {
            if (style.GetResolvedValue(longhand) != CssPropertyRegistry.Get(longhand)?.Initial)
                return "";
        }

        foreach (var longhand in FontVariantLonghands)
        {
            if (style.GetResolvedValue(longhand) != "normal")
                return "";
        }

        var caps = style.GetResolvedValue("font-variant-caps");
        if (caps is not ("normal" or "small-caps"))
            return "";

        if (!FontWidthKeywords.TryGetValue(style.GetResolvedValue("font-width"), out var width))
            return "";

        var result = new StringBuilder();
        void Append(string value)
        {
            if (result.Length > 0)
            {
                result.Append(' ');
            }

            result.Append(value);
        }

        if (style.GetResolvedValue("font-style") is var fontStyle and not "normal")
        {
            Append(fontStyle);
        }

        if (caps != "normal")
        {
            Append(caps);
        }

        if (style.GetResolvedValue("font-weight") is var weight and not ("400" or "normal"))
        {
            Append(weight);
        }

        if (width != "normal")
        {
            Append(width);
        }

        Append(style.GetResolvedValue("font-size"));
        if (style.GetResolvedValue("line-height") is var lineHeight and not "normal")
        {
            result.Append(" / ").Append(lineHeight);
        }

        Append(style.GetResolvedValue("font-family"));
        return result.ToString();
    }
}
