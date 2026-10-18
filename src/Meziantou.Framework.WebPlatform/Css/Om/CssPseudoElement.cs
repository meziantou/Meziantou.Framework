using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>
/// A pseudo-element of an element (https://drafts.csswg.org/css-pseudo-4/#CSSPseudoElement-interface), returned by
/// <see cref="Element.Pseudo(string)"/>: a tree-abiding pseudo-element that is not element-backed (<c>::before</c>,
/// <c>::after</c>, <c>::marker</c>, <c>::backdrop</c>, the scroll navigation controls, the view transition pseudo-elements...).
/// The object exists whether or not the pseudo-element is generated, and the same object represents the same pseudo-element
/// of the same originating element.
/// </summary>
/// <remarks>
/// The sub-pseudo-elements are the <c>::marker</c> of <c>::before</c> and <c>::after</c>, and the view transition
/// pseudo-elements, whose parent is their parent in the view transition tree without nested group
/// (<c>::view-transition</c> &gt; <c>::view-transition-group()</c> &gt; <c>::view-transition-image-pair()</c> &gt;
/// <c>::view-transition-old()</c> and <c>::view-transition-new()</c>).
/// </remarks>
[IdlInterface("CSSPseudoElement", "https://drafts.csswg.org/css-pseudo-4/#CSSPseudoElement-interface")]
public sealed class CssPseudoElement : IRealmBoundPlatformObject
{
    private Dictionary<string, CssPseudoElement>? _children;

    private CssPseudoElement(Element element, CssPseudoElement? parent, string name)
    {
        OriginatingElement = element;
        ParentPseudoElement = parent;
        Name = name;
    }

    /// <summary>The ultimate originating element.</summary>
    internal Element OriginatingElement { get; }

    /// <summary>The originating pseudo-element of a sub-pseudo-element, or null.</summary>
    internal CssPseudoElement? ParentPseudoElement { get; }

    /// <summary>
    /// The name of the pseudo-element with its argument, without the colons (e.g. <c>before</c>,
    /// <c>scroll-button(inline-end)</c>, <c>view-transition-group(name)</c>): the name of its style and of its box.
    /// </summary>
    internal string Name { get; }

    /// <summary>
    /// The name of the style and of the box of the pseudo-element on <see cref="OriginatingElement"/>, or null for a
    /// <c>::marker</c> of a <c>::before</c> or <c>::after</c> (an anonymous box of its pseudo-element, which cannot be styled).
    /// </summary>
    internal string? BoxName => ParentPseudoElement is null || ParentPseudoElement.Name.StartsWith("view-transition", StringComparison.Ordinal) ? Name : null;

    Window? IRealmBoundPlatformObject.RealmWindow => OriginatingElement.NodeDocument.DefaultView;

    /// <summary>Gets the type of the pseudo-element: its name with the colons and without its argument (e.g. <c>::scroll-button</c>).</summary>
    [IdlMember("type", "https://drafts.csswg.org/css-pseudo-4/#dom-csspseudoelement-type")]
    public string Type
    {
        get
        {
            var index = Name.IndexOf('(', StringComparison.Ordinal);
            return "::" + (index < 0 ? Name : Name[..index]);
        }
    }

    /// <summary>Gets the ultimate originating element of the pseudo-element.</summary>
    [IdlMember("element", "https://drafts.csswg.org/css-pseudo-4/#dom-csspseudoelement-element")]
    public Element Element => OriginatingElement;

    /// <summary>Gets the originating element of the pseudo-element, or its originating pseudo-element for a sub-pseudo-element.</summary>
    [IdlMember("parent", "https://drafts.csswg.org/css-pseudo-4/#dom-csspseudoelement-parent")]
    public ElementOrCssPseudoElement Parent => ParentPseudoElement is { } parent ? parent : OriginatingElement;

    /// <summary>Gets the normalized selector of the pseudo-element, with its argument (e.g. <c>::scroll-button(inline-end)</c>).</summary>
    [IdlMember("selectorText", "https://drafts.csswg.org/css-pseudo-4/#dom-csspseudoelement-selectortext")]
    public string SelectorText => "::" + Name;

    /// <summary>
    /// Returns the sub-pseudo-element of the pseudo-element designated by a pseudo-element selector, or null when it cannot
    /// exist (https://drafts.csswg.org/css-pseudo-4/#dom-csspseudoelement-pseudo): the <c>::marker</c> of a <c>::before</c> or
    /// <c>::after</c>, or the child of a view transition pseudo-element of the same name.
    /// </summary>
    [IdlMember("pseudo", "https://drafts.csswg.org/css-pseudo-4/#dom-csspseudoelement-pseudo")]
    public CssPseudoElement? Pseudo(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (ComputedStyleDeclarations.ParsePseudoElement(type) is not { } name || !IsChild(Name, name))
            return null;

        return GetOrCreateChild(name);
    }

    /// <summary>
    /// Returns the quads of the boxes of the pseudo-element (https://drafts.csswg.org/cssom-view/#dom-geometryutils-getboxquads):
    /// one per box fragment, empty when the pseudo-element does not generate a box.
    /// </summary>
    [IdlMember("getBoxQuads", "https://drafts.csswg.org/cssom-view/#dom-geometryutils-getboxquads")]
    public IReadOnlyList<DomQuad> GetBoxQuads(BoxQuadOptions? options = null) => GeometryUtils.GetBoxQuads(this, options);

    /// <summary>Converts a quad from the coordinates of a node to the coordinates of the pseudo-element (https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertquadfromnode).</summary>
    [IdlMember("convertQuadFromNode", "https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertquadfromnode")]
    public DomQuad ConvertQuadFromNode(DomQuadInit quad, GeometryNode from, ConvertCoordinateOptions? options = null) => GeometryUtils.ConvertQuadFromNode(this, quad, from, options);

    /// <summary>Converts a rectangle from the coordinates of a node to the coordinates of the pseudo-element (https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertrectfromnode).</summary>
    [IdlMember("convertRectFromNode", "https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertrectfromnode")]
    public DomQuad ConvertRectFromNode(DomRectReadOnly rect, GeometryNode from, ConvertCoordinateOptions? options = null) => GeometryUtils.ConvertRectFromNode(this, rect, from, options);

    /// <summary>Converts a point from the coordinates of a node to the coordinates of the pseudo-element (https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertpointfromnode).</summary>
    [IdlMember("convertPointFromNode", "https://drafts.csswg.org/cssom-view/#dom-geometryutils-convertpointfromnode")]
    public DomPoint ConvertPointFromNode(DomPointInit point, GeometryNode from, ConvertCoordinateOptions? options = null) => GeometryUtils.ConvertPointFromNode(this, point, from, options);

    /// <inheritdoc/>
    public override string ToString() => SelectorText;

    /// <summary>
    /// https://drafts.csswg.org/css-pseudo-4/#dom-element-pseudo: returns the pseudo-element of an element designated by a
    /// pseudo-element selector, or null when the selector is invalid or does not designate a tree-abiding pseudo-element that is
    /// not element-backed. A <c>::scroll-button()</c> of a physical direction is the button of the flow-relative direction it
    /// maps to in the writing mode of the element.
    /// </summary>
    internal static CssPseudoElement? Get(Element element, string type)
    {
        if (ComputedStyleDeclarations.ParsePseudoElement(type) is not { } name)
            return null;

        if (ScrollControls.IsScrollButton(name))
        {
            name = ScrollControls.Canonicalize(name, () => GetWritingDirection(element));
            if (name is null)
                return null;
        }

        return IsSupported(name) ? GetOrCreate(element, name) : null;
    }

    /// <summary>
    /// Returns the pseudo-element of an element whose style and box have the given name (see <see cref="BoxName"/>), or null when
    /// it is not represented by a <see cref="CssPseudoElement"/> (e.g. a pseudo-element of a form control).
    /// </summary>
    internal static CssPseudoElement? FromBoxName(Element element, string name) => IsSupported(name) ? GetOrCreate(element, name) : null;

    /// <summary>Whether a pseudo-element of an element is a tree-abiding pseudo-element that is not element-backed (https://drafts.csswg.org/css-pseudo-4/#treelike).</summary>
    private static bool IsSupported(string name)
    {
        if (name is "before" or "after" or "marker" or "placeholder" or "backdrop" or "checkmark" or "picker-icon" or "column"
            or ScrollControls.Marker or ScrollControls.MarkerGroup or ViewTransitionPseudoTree.RootName)
        {
            return true;
        }

        return ScrollControls.IsScrollButton(name) || IsNamedViewTransitionPseudoElement(name);
    }

    private static bool IsNamedViewTransitionPseudoElement(string name)
        => ViewTransitionPseudoTree.TryParse(name, out var type, out _) && type is "view-transition-group" or "view-transition-group-children" or "view-transition-image-pair" or "view-transition-old" or "view-transition-new";

    /// <summary>Returns the pseudo-element of an element, created with its ancestor pseudo-elements for a view transition pseudo-element.</summary>
    private static CssPseudoElement GetOrCreate(Element element, string name)
    {
        if (ViewTransitionPseudoTree.TryParse(name, out var type, out var transitionName))
        {
            // The view transition pseudo-elements are sub-pseudo-elements of ::view-transition: the same object is returned by
            // the pseudo() method of their parent
            var parentName = type switch
            {
                "view-transition-old" or "view-transition-new" => ViewTransitionPseudoTree.ImagePairPrefix + transitionName + ")",
                "view-transition-image-pair" or "view-transition-group-children" => ViewTransitionPseudoTree.GroupPrefix + transitionName + ")",
                _ => ViewTransitionPseudoTree.RootName,
            };

            return GetOrCreate(element, parentName).GetOrCreateChild(name);
        }

        var pseudoElements = element.ElementRareData.PseudoElements ??= new Dictionary<string, CssPseudoElement>(StringComparer.Ordinal);
        if (!pseudoElements.TryGetValue(name, out var result))
        {
            result = new CssPseudoElement(element, parent: null, name);
            pseudoElements.Add(name, result);
        }

        return result;
    }

    private CssPseudoElement GetOrCreateChild(string name)
    {
        _children ??= new Dictionary<string, CssPseudoElement>(StringComparer.Ordinal);
        if (!_children.TryGetValue(name, out var result))
        {
            result = new CssPseudoElement(OriginatingElement, this, name);
            _children.Add(name, result);
        }

        return result;
    }

    /// <summary>Whether a pseudo-element can be a sub-pseudo-element of another one.</summary>
    private static bool IsChild(string parent, string child)
    {
        if (parent is "before" or "after")
            return child == "marker";

        if (parent == ViewTransitionPseudoTree.RootName)
            return ViewTransitionPseudoTree.TryParse(child, out var rootChildType, out _) && rootChildType == "view-transition-group";

        if (!ViewTransitionPseudoTree.TryParse(parent, out var parentType, out var parentName) || !ViewTransitionPseudoTree.TryParse(child, out var childType, out var childName)
            || !string.Equals(parentName, childName, StringComparison.Ordinal))
        {
            return false;
        }

        return parentType switch
        {
            "view-transition-group" => childType is "view-transition-image-pair" or "view-transition-group-children",
            "view-transition-image-pair" => childType is "view-transition-old" or "view-transition-new",
            _ => false,
        };
    }

    /// <summary>The writing mode and the direction of an element (the initial ones when it has no computed style).</summary>
    private static WritingDirection GetWritingDirection(Element element)
        => ComputedStyleDeclarations.IsRendered(element) ? WritingDirection.FromStyle(element.NodeDocument.GetStyleEngine().GetStyle(element).Layout) : default;
}

/// <summary>The <c>(Element or CSSPseudoElement)</c> union: the originating element or pseudo-element of a pseudo-element.</summary>
[IdlInterface("(Element or CSSPseudoElement)", "https://drafts.csswg.org/css-pseudo-4/#dom-csspseudoelement-parent")]
public readonly struct ElementOrCssPseudoElement : IUnionValue, IEquatable<ElementOrCssPseudoElement>
{
    private ElementOrCssPseudoElement(object value) => Value = value;

    /// <summary>Gets the value: an <c>Element</c> or a <c>CSSPseudoElement</c>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the value as an <c>Element</c>, or null when the value is a pseudo-element.</summary>
    [NotInIdl]
    public Element? Element => Value as Element;

    /// <summary>Gets the value as a <c>CSSPseudoElement</c>, or null when the value is an element.</summary>
    [NotInIdl]
    public CssPseudoElement? PseudoElement => Value as CssPseudoElement;

    /// <summary>Converts an <c>Element</c> to the union.</summary>
    [NotInIdl]
    public static implicit operator ElementOrCssPseudoElement(Element value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a <c>CSSPseudoElement</c> to the union.</summary>
    [NotInIdl]
    public static implicit operator ElementOrCssPseudoElement(CssPseudoElement value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from an <c>Element</c>.</summary>
    [NotInIdl]
    public static ElementOrCssPseudoElement FromElement(Element value) => value;

    /// <summary>Creates the union from a <c>CSSPseudoElement</c>.</summary>
    [NotInIdl]
    public static ElementOrCssPseudoElement FromCssPseudoElement(CssPseudoElement value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(ElementOrCssPseudoElement other) => ReferenceEquals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ElementOrCssPseudoElement other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two unions are equal.</summary>
    [NotInIdl]
    public static bool operator ==(ElementOrCssPseudoElement left, ElementOrCssPseudoElement right) => left.Equals(right);

    /// <summary>Determines whether two unions are different.</summary>
    [NotInIdl]
    public static bool operator !=(ElementOrCssPseudoElement left, ElementOrCssPseudoElement right) => !left.Equals(right);
}
