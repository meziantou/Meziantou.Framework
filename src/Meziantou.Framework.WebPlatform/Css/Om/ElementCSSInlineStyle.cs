using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.TypedOm;

namespace Meziantou.Framework.WebPlatform.Dom;

// The ElementCSSInlineStyle mixin (https://drafts.csswg.org/cssom/#elementcssinlinestyle)
public partial class Element
{
    /// <summary>https://drafts.csswg.org/cssom/#dom-elementcssinlinestyle-style</summary>
    internal CssStyleProperties GetInlineStyle() => ElementRareData.Style ??= new CssStyleProperties(this);

    /// <summary>https://drafts.css-houdini.org/css-typed-om-1/#dom-elementcssinlinestyle-attributestylemap</summary>
    internal StylePropertyMap GetAttributeStyleMap() => ElementRareData.AttributeStyleMap ??= new StylePropertyMap(GetInlineStyle());

    /// <summary>
    /// Returns the computed values of the element as <see cref="CssStyleValue"/> objects (https://drafts.css-houdini.org/css-typed-om-1/#dom-element-computedstylemap).
    /// The map is live; it is empty when the element is not rendered (e.g. not connected).
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-element-computedstylemap"/>
    [IdlMember("computedStyleMap", "https://drafts.css-houdini.org/css-typed-om-1/#dom-element-computedstylemap")]
    public StylePropertyMapReadOnly ComputedStyleMap() => ElementRareData.ComputedStyleMap ??= new ComputedStylePropertyMap(this);
}

public partial class HtmlElement
{
    /// <summary>
    /// Gets the object that represents the <c>style</c> attribute of the element as a CSS declaration block.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-elementcssinlinestyle-style"/>
    [IdlMember("style", "https://drafts.csswg.org/cssom/#dom-elementcssinlinestyle-style")]
    public CssStyleProperties Style => GetInlineStyle();

    /// <summary>Gets the typed object model map of the <c>style</c> attribute of the element.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-elementcssinlinestyle-attributestylemap"/>
    [IdlMember("attributeStyleMap", "https://drafts.css-houdini.org/css-typed-om-1/#dom-elementcssinlinestyle-attributestylemap")]
    public StylePropertyMap AttributeStyleMap => GetAttributeStyleMap();
}

public partial class SvgElement
{
    /// <summary>
    /// Gets the object that represents the <c>style</c> attribute of the element as a CSS declaration block.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-elementcssinlinestyle-style"/>
    [IdlMember("style", "https://drafts.csswg.org/cssom/#dom-elementcssinlinestyle-style")]
    public CssStyleProperties Style => GetInlineStyle();

    /// <summary>Gets the typed object model map of the <c>style</c> attribute of the element.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-elementcssinlinestyle-attributestylemap"/>
    [IdlMember("attributeStyleMap", "https://drafts.css-houdini.org/css-typed-om-1/#dom-elementcssinlinestyle-attributestylemap")]
    public StylePropertyMap AttributeStyleMap => GetAttributeStyleMap();
}

public partial class MathMLElement
{
    /// <summary>
    /// Gets the object that represents the <c>style</c> attribute of the element as a CSS declaration block.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-elementcssinlinestyle-style"/>
    [IdlMember("style", "https://drafts.csswg.org/cssom/#dom-elementcssinlinestyle-style")]
    public CssStyleProperties Style => GetInlineStyle();

    /// <summary>Gets the typed object model map of the <c>style</c> attribute of the element.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-elementcssinlinestyle-attributestylemap"/>
    [IdlMember("attributeStyleMap", "https://drafts.css-houdini.org/css-typed-om-1/#dom-elementcssinlinestyle-attributestylemap")]
    public StylePropertyMap AttributeStyleMap => GetAttributeStyleMap();
}
