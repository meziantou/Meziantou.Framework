using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

public partial class Element
{

    /// <summary>Gets the live token list of the <c>class</c> attribute.</summary>
    [IdlMember("classList", "https://dom.spec.whatwg.org/#dom-element-classlist")]
    public DomTokenList ClassList => ElementRareData.ClassList ??= new DomTokenList(this, "class");

    /// <summary>Gets the live map of the attributes of the element.</summary>
    [IdlMember("attributes", "https://dom.spec.whatwg.org/#dom-element-attributes")]
    public NamedNodeMap Attributes => ElementRareData.AttributeMap ??= new NamedNodeMap(this);

    /// <summary>
    /// Returns the classes of the element for the selectors: the animated value of the class attribute when a SMIL animation
    /// animates it (https://svgwg.org/specs/animations/#TermAnimatedValue), as in the browsers.
    /// </summary>
    internal ClassNames GetStyleClassNames()
    {
        if (NodeDocument.SmilOrNull is { } smil && smil.GetAttributeValue(this, "class") is { } animated)
            return new ClassNames(OrderedSets.Parse(animated));

        return GetClassNames();
    }

    /// <summary>Returns the classes of the element (https://dom.spec.whatwg.org/#concept-class), cached until the attribute changes.</summary>
    internal ClassNames GetClassNames()
    {
        var index = FindAttribute(null, "class");
        if (index < 0)
            return default;

        // A single class (the most common case) is the value of the attribute: it is neither parsed nor cached
        ref var slot = ref _attributes![index];
        var value = slot.Value;
        if (value.Length > 0 && value.AsSpan().IndexOfAny(" \t\n\f\r") < 0)
            return new ClassNames(value);

        // The classes are cached in the attribute until its value changes, unless it has an Attr node (it is rarely created)
        if (slot.Extra is List<string> cached)
            return new ClassNames(cached);

        if (slot.Extra is null)
        {
            var classNames = OrderedSets.Parse(value);
            slot.Extra = classNames;
            return new ClassNames(classNames);
        }

        var rareData = ElementRareData;
        if (rareData.ClassNames is null || !ReferenceEquals(value, rareData.ClassNamesSource))
        {
            rareData.ClassNames = OrderedSets.Parse(value);
            rareData.ClassNamesSource = value;
        }

        return new ClassNames(rareData.ClassNames);
    }

    /// <summary>Returns a live collection of the descendant elements with the given qualified name.</summary>
    [IdlMember("getElementsByTagName", "https://dom.spec.whatwg.org/#dom-element-getelementsbytagname")]
    public HtmlCollection GetElementsByTagName(string qualifiedName)
    {
        ArgumentNullException.ThrowIfNull(qualifiedName);
        return HtmlCollection.ByQualifiedName(this, qualifiedName);
    }

    /// <summary>Returns a live collection of the descendant elements with the given namespace and local name.</summary>
    [IdlMember("getElementsByTagNameNS", "https://dom.spec.whatwg.org/#dom-element-getelementsbytagnamens")]
    public HtmlCollection GetElementsByTagNameNS(string? @namespace, string localName)
    {
        ArgumentNullException.ThrowIfNull(localName);
        return HtmlCollection.ByNamespaceAndLocalName(this, @namespace, localName);
    }

    /// <summary>Returns a live collection of the descendant elements that have all the given class names.</summary>
    [IdlMember("getElementsByClassName", "https://dom.spec.whatwg.org/#dom-element-getelementsbyclassname")]
    public HtmlCollection GetElementsByClassName(string classNames)
    {
        ArgumentNullException.ThrowIfNull(classNames);
        return HtmlCollection.ByClassNames(this, classNames);
    }

    /// <summary>https://dom.spec.whatwg.org/#insert-adjacent</summary>
    private Node? InsertAdjacent(string where, Node node)
    {
        if (InfraAscii.EqualsIgnoreAsciiCase(where, "beforebegin"))
            return ParentNode?.PreInsert(node, this);

        if (InfraAscii.EqualsIgnoreAsciiCase(where, "afterbegin"))
            return PreInsert(node, FirstChild);

        if (InfraAscii.EqualsIgnoreAsciiCase(where, "beforeend"))
            return PreInsert(node, child: null);

        if (InfraAscii.EqualsIgnoreAsciiCase(where, "afterend"))
            return ParentNode?.PreInsert(node, NextSibling);

        throw DomException.Syntax($"'{where}' is not a valid position");
    }

    /// <summary>
    /// Inserts the element at the given position relative to this element and returns it, or null when it cannot be
    /// inserted.
    /// </summary>
    [IdlMember("insertAdjacentElement", "https://dom.spec.whatwg.org/#dom-element-insertadjacentelement")]
    public Element? InsertAdjacentElement(string where, Element element)
    {
        ArgumentNullException.ThrowIfNull(where);
        ArgumentNullException.ThrowIfNull(element);
        return (Element?)InsertAdjacent(where, element);
    }

    /// <summary>Inserts a text node with the given data at the given position relative to this element.</summary>
    [IdlMember("insertAdjacentText", "https://dom.spec.whatwg.org/#dom-element-insertadjacenttext")]
    public void InsertAdjacentText(string where, string data)
    {
        ArgumentNullException.ThrowIfNull(where);
        ArgumentNullException.ThrowIfNull(data);
        var text = new Text(NodeDocument, data);
        InsertAdjacent(where, text);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-element-matches</summary>
    [IdlMember("matches", "https://dom.spec.whatwg.org/#dom-element-matches")]
    public bool Matches(string selectors) => Matches(Css.Selectors.SelectorCache.Parse(selectors));

    /// <summary>Returns whether the element matches the parsed selector (https://dom.spec.whatwg.org/#dom-element-matches).</summary>
    [NotInIdl("Overload taking a parsed selector")]
    public bool Matches(Css.Selectors.SelectorList selectors)
    {
        ArgumentNullException.ThrowIfNull(selectors);
        return Css.Selectors.SelectorMatcher.Matches(selectors, this, Css.Selectors.SelectorContext.ForDomApi(this));
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-element-webkitmatchesselector</summary>
    [IdlMember("webkitMatchesSelector", "https://dom.spec.whatwg.org/#dom-element-webkitmatchesselector")]
    public bool WebkitMatchesSelector(string selectors) => Matches(selectors);

    /// <summary>https://dom.spec.whatwg.org/#dom-element-closest</summary>
    [IdlMember("closest", "https://dom.spec.whatwg.org/#dom-element-closest")]
    public Element? Closest(string selectors) => Closest(Css.Selectors.SelectorCache.Parse(selectors));

    /// <summary>Returns the closest inclusive ancestor that matches the parsed selector (https://dom.spec.whatwg.org/#dom-element-closest).</summary>
    [NotInIdl("Overload taking a parsed selector")]
    public Element? Closest(Css.Selectors.SelectorList selectors)
    {
        ArgumentNullException.ThrowIfNull(selectors);
        var context = Css.Selectors.SelectorContext.ForDomApi(this);
        for (var element = this; element is not null; element = element.ParentElement)
        {
            if (Css.Selectors.SelectorMatcher.Matches(selectors, element, context))
                return element;
        }

        return null;
    }
}

/// <summary>The classes of an element (https://dom.spec.whatwg.org/#concept-class): a single class, or the parsed list of classes.</summary>
internal readonly struct ClassNames
{
    private readonly string? _single;
    private readonly List<string>? _list;

    public ClassNames(string single) => _single = single;

    public ClassNames(List<string> list) => _list = list;

    public int Count => _single is not null ? 1 : _list?.Count ?? 0;

    public string this[int index] => _single ?? _list![index];

    /// <summary>Returns whether the class is one of the classes (case-sensitive).</summary>
    public bool Contains(string className) => _single is not null ? _single == className : _list is not null && _list.Contains(className);

    public Enumerator GetEnumerator() => new(this);

    public struct Enumerator(ClassNames names)
    {
        private int _index = -1;

        public readonly string Current => names[_index];

        public bool MoveNext() => ++_index < names.Count;
    }
}
