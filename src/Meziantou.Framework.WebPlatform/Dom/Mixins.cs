namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The <c>(Node or DOMString)</c> union used by the ParentNode and ChildNode mixins.</summary>
/// <seealso href="https://dom.spec.whatwg.org/#parentnode"/>
[IdlInterface("(Node or DOMString)", "https://dom.spec.whatwg.org/#parentnode")]
public readonly struct NodeOrString
{
    private NodeOrString(Node? node, string? value)
    {
        Node = node;
        StringValue = value;
    }

    /// <summary>Gets the node, or null when the value is a string.</summary>
    [NotInIdl]
    public Node? Node { get; }

    /// <summary>Gets the string, or null when the value is a node.</summary>
    [NotInIdl]
    public string? StringValue { get; }

    /// <summary>Converts a node to a union value.</summary>
    [NotInIdl]
    public static implicit operator NodeOrString(Node node) => new(node, null);

    /// <summary>Converts a string to a union value.</summary>
    [NotInIdl]
    public static implicit operator NodeOrString(string value) => new(null, value);

    /// <summary>Creates a union value holding the given node.</summary>
    [NotInIdl]
    public static NodeOrString FromNode(Node node) => new(node, null);

    /// <summary>Creates a union value holding the given string.</summary>
    [NotInIdl]
    public static NodeOrString FromString(string value) => new(null, value);
}

/// <summary>A value of a union type returned by an IDL attribute.</summary>
internal interface IUnionValue
{
    object Value { get; }
}

/// <summary>The <c>(boolean or unrestricted double or DOMString)</c> union of the hidden IDL attribute.</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-hidden"/>
[IdlInterface("(boolean or unrestricted double or DOMString)", "https://html.spec.whatwg.org/multipage/interaction.html#dom-hidden")]
public readonly struct BooleanOrDoubleOrString : IUnionValue, IEquatable<BooleanOrDoubleOrString>
{
    private BooleanOrDoubleOrString(object value) => Value = value;

    /// <summary>Gets the value: a <see cref="bool"/>, a <see cref="double"/> or a <see cref="string"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Converts a boolean to a union value.</summary>
    [NotInIdl]
    public static implicit operator BooleanOrDoubleOrString(bool value) => new(value);

    /// <summary>Converts a number to a union value.</summary>
    [NotInIdl]
    public static implicit operator BooleanOrDoubleOrString(double value) => new(value);

    /// <summary>Converts a string to a union value.</summary>
    [NotInIdl]
    public static implicit operator BooleanOrDoubleOrString(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a union value holding the given boolean.</summary>
    [NotInIdl]
    public static BooleanOrDoubleOrString FromBoolean(bool value) => new(value);

    /// <summary>Creates a union value holding the given number.</summary>
    [NotInIdl]
    public static BooleanOrDoubleOrString FromDouble(double value) => new(value);

    /// <summary>Creates a union value holding the given string.</summary>
    [NotInIdl]
    public static BooleanOrDoubleOrString FromString(string value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(BooleanOrDoubleOrString other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is BooleanOrDoubleOrString other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <inheritdoc/>
    [NotInIdl]
    public override string? ToString() => Value?.ToString();

    /// <summary>Returns whether the two union values hold equal values.</summary>
    [NotInIdl]
    public static bool operator ==(BooleanOrDoubleOrString left, BooleanOrDoubleOrString right) => left.Equals(right);

    /// <summary>Returns whether the two union values hold different values.</summary>
    [NotInIdl]
    public static bool operator !=(BooleanOrDoubleOrString left, BooleanOrDoubleOrString right) => !left.Equals(right);
}

/// <summary>https://dom.spec.whatwg.org/#interface-parentnode</summary>
/// <seealso href="https://dom.spec.whatwg.org/#parentnode"/>
[IdlInterface("ParentNode", "https://dom.spec.whatwg.org/#parentnode")]
public interface IParentNode
{
    /// <summary>Gets the live collection of the child elements.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-parentnode-children"/>
    [IdlMember("children", "https://dom.spec.whatwg.org/#dom-parentnode-children")]
    HtmlCollection Children { get; }

    /// <summary>Gets the first child that is an element, or null when there is none.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-parentnode-firstelementchild"/>
    [IdlMember("firstElementChild", "https://dom.spec.whatwg.org/#dom-parentnode-firstelementchild")]
    Element? FirstElementChild { get; }

    /// <summary>Gets the last child that is an element, or null when there is none.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-parentnode-lastelementchild"/>
    [IdlMember("lastElementChild", "https://dom.spec.whatwg.org/#dom-parentnode-lastelementchild")]
    Element? LastElementChild { get; }

    /// <summary>Gets the number of child elements.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-parentnode-childelementcount"/>
    [IdlMember("childElementCount", "https://dom.spec.whatwg.org/#dom-parentnode-childelementcount")]
    uint ChildElementCount { get; }

    /// <summary>Inserts the nodes before the first child, converting the strings into text nodes.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-parentnode-prepend"/>
    [IdlMember("prepend", "https://dom.spec.whatwg.org/#dom-parentnode-prepend")]
    void Prepend(params ReadOnlySpan<NodeOrString> nodes);

    /// <summary>Inserts the nodes after the last child, converting the strings into text nodes.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-parentnode-append"/>
    [IdlMember("append", "https://dom.spec.whatwg.org/#dom-parentnode-append")]
    void Append(params ReadOnlySpan<NodeOrString> nodes);

    /// <summary>Replaces all the children with the given nodes, converting the strings into text nodes.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-parentnode-replacechildren"/>
    [IdlMember("replaceChildren", "https://dom.spec.whatwg.org/#dom-parentnode-replacechildren")]
    void ReplaceChildren(params ReadOnlySpan<NodeOrString> nodes);

    /// <summary>
    /// Moves the node, preserving its state, before the given child (or to the end when the child is null).
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-parentnode-movebefore"/>
    [IdlMember("moveBefore", "https://dom.spec.whatwg.org/#dom-parentnode-movebefore")]
    void MoveBefore(Node node, Node? child);

    /// <summary>Returns the first descendant element, in tree order, that matches the selectors, or null.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-parentnode-queryselector"/>
    [IdlMember("querySelector", "https://dom.spec.whatwg.org/#dom-parentnode-queryselector")]
    Element? QuerySelector(string selectors);

    /// <summary>Returns the descendant elements, in tree order, that match the selectors.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-parentnode-queryselectorall"/>
    [IdlMember("querySelectorAll", "https://dom.spec.whatwg.org/#dom-parentnode-queryselectorall")]
    NodeList QuerySelectorAll(string selectors);

    /// <summary>Returns the first inclusive descendant, in tree order, that matches the parsed selector (https://dom.spec.whatwg.org/#dom-parentnode-queryselector).</summary>
    [NotInIdl("Overload taking a parsed selector")]
    Element? QuerySelector(Css.Selectors.SelectorList selectors);

    /// <summary>Returns the descendants, in tree order, that match the parsed selector (https://dom.spec.whatwg.org/#dom-parentnode-queryselectorall).</summary>
    [NotInIdl("Overload taking a parsed selector")]
    NodeList QuerySelectorAll(Css.Selectors.SelectorList selectors);
}

/// <summary>https://dom.spec.whatwg.org/#interface-childnode</summary>
/// <seealso href="https://dom.spec.whatwg.org/#childnode"/>
[IdlInterface("ChildNode", "https://dom.spec.whatwg.org/#childnode")]
public interface IChildNode
{
    /// <summary>Inserts the nodes just before this node, converting the strings into text nodes.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-childnode-before"/>
    [IdlMember("before", "https://dom.spec.whatwg.org/#dom-childnode-before")]
    void Before(params ReadOnlySpan<NodeOrString> nodes);

    /// <summary>Inserts the nodes just after this node, converting the strings into text nodes.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-childnode-after"/>
    [IdlMember("after", "https://dom.spec.whatwg.org/#dom-childnode-after")]
    void After(params ReadOnlySpan<NodeOrString> nodes);

    /// <summary>
    /// Replaces this node with the given nodes in its parent, converting the strings into text nodes.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-childnode-replacewith"/>
    [IdlMember("replaceWith", "https://dom.spec.whatwg.org/#dom-childnode-replacewith")]
    void ReplaceWith(params ReadOnlySpan<NodeOrString> nodes);

    /// <summary>Removes this node from its parent, if any.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-childnode-remove"/>
    [IdlMember("remove", "https://dom.spec.whatwg.org/#dom-childnode-remove")]
    void Remove();
}

/// <summary>https://dom.spec.whatwg.org/#interface-nondocumenttypechildnode</summary>
/// <seealso href="https://dom.spec.whatwg.org/#nondocumenttypechildnode"/>
[IdlInterface("NonDocumentTypeChildNode", "https://dom.spec.whatwg.org/#nondocumenttypechildnode")]
public interface INonDocumentTypeChildNode
{
    /// <summary>Gets the closest preceding sibling that is an element, or null when there is none.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-nondocumenttypechildnode-previouselementsibling"/>
    [IdlMember("previousElementSibling", "https://dom.spec.whatwg.org/#dom-nondocumenttypechildnode-previouselementsibling")]
    Element? PreviousElementSibling { get; }

    /// <summary>Gets the closest following sibling that is an element, or null when there is none.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-nondocumenttypechildnode-nextelementsibling"/>
    [IdlMember("nextElementSibling", "https://dom.spec.whatwg.org/#dom-nondocumenttypechildnode-nextelementsibling")]
    Element? NextElementSibling { get; }
}

/// <summary>https://dom.spec.whatwg.org/#interface-nonelementparentnode</summary>
/// <seealso href="https://dom.spec.whatwg.org/#nonelementparentnode"/>
[IdlInterface("NonElementParentNode", "https://dom.spec.whatwg.org/#nonelementparentnode")]
public interface INonElementParentNode
{
    /// <summary>Returns the first descendant element, in tree order, whose ID is the given value, or null.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-nonelementparentnode-getelementbyid"/>
    [IdlMember("getElementById", "https://dom.spec.whatwg.org/#dom-nonelementparentnode-getelementbyid")]
    Element? GetElementById(string elementId);
}

public abstract partial class Node
{
    /// <summary>https://dom.spec.whatwg.org/#converting-nodes-into-a-node</summary>
    internal static Node ConvertNodesIntoNode(ReadOnlySpan<NodeOrString> nodes, Document document)
    {
        if (nodes.Length == 1)
            return ToNode(nodes[0], document);

        var fragment = new DocumentFragment(document);
        foreach (var item in nodes)
        {
            fragment.AppendNode(ToNode(item, document));
        }

        return fragment;

        static Node ToNode(NodeOrString item, Document document)
        {
            if (item.Node is { } node)
                return node;

            if (item.StringValue is { } value)
                return new Text(document, value);

            throw new ArgumentException("The node or string is null", nameof(item));
        }
    }

    private static bool Contains(ReadOnlySpan<NodeOrString> nodes, Node node)
    {
        foreach (var item in nodes)
        {
            if (ReferenceEquals(item.Node, node))
                return true;
        }

        return false;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // ParentNode
    // ----------------------------------------------------------------------------------------------------------------

    private protected Element? FirstElementChildCore()
    {
        for (var child = FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is Element element)
                return element;
        }

        return null;
    }

    private protected Element? LastElementChildCore()
    {
        for (var child = LastChild; child is not null; child = child.PreviousSibling)
        {
            if (child is Element element)
                return element;
        }

        return null;
    }

    private protected uint ChildElementCountCore()
    {
        var count = 0u;
        for (var child = FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is Element)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-parentnode-prepend</summary>
    private protected void PrependCore(ReadOnlySpan<NodeOrString> nodes)
    {
        var node = ConvertNodesIntoNode(nodes, NodeDocument);
        PreInsert(node, FirstChild);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-parentnode-append</summary>
    private protected void AppendCore(ReadOnlySpan<NodeOrString> nodes)
    {
        var node = ConvertNodesIntoNode(nodes, NodeDocument);
        AppendNode(node);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-parentnode-replacechildren</summary>
    private protected void ReplaceChildrenCore(ReadOnlySpan<NodeOrString> nodes)
    {
        var node = ConvertNodesIntoNode(nodes, NodeDocument);
        EnsurePreInsertValidity(node, child: null, excludeAllChildren: true);
        ReplaceAll(node);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-parentnode-movebefore</summary>
    private protected void MoveBeforeCore(Node node, Node? child)
    {
        ArgumentNullException.ThrowIfNull(node);
        var referenceChild = child;
        if (ReferenceEquals(referenceChild, node))
        {
            referenceChild = node.NextSibling;
        }

        Move(node, referenceChild);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-parentnode-queryselector</summary>
    private protected Element? QuerySelectorCore(string selectors) => QuerySelectorCore(Css.Selectors.SelectorCache.Parse(selectors));

    private protected Element? QuerySelectorCore(Css.Selectors.SelectorList selector) => QuerySelectorCore<Element>(selector);

    /// <summary>Returns the first descendant, in tree order, that is a <typeparamref name="T"/> and matches the selector.</summary>
    private protected T? QuerySelectorCore<T>(string selectors) where T : Element => QuerySelectorCore<T>(Css.Selectors.SelectorCache.Parse(selectors));

    private protected T? QuerySelectorCore<T>(Css.Selectors.SelectorList selector) where T : Element
    {
        ArgumentNullException.ThrowIfNull(selector);

        // Fast path: a selector whose subject has an ID can only match the elements with this ID. As the ID cache
        // returns the first element with the ID in tree order, it is the result when it matches.
        if (this is Document document && document.Mode != DocumentMode.Quirks && GetSubjectId(selector) is { } id)
        {
            if (document.GetElementById(id) is T candidate && Css.Selectors.SelectorMatcher.Matches(selector, candidate, this))
                return candidate;
        }

        return Css.Selectors.SelectorQuery.Find<T>(this, selector, Css.Selectors.SelectorContext.ForDomApi(this), result: null, seen: null);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-parentnode-queryselectorall</summary>
    private protected NodeList QuerySelectorAllCore(string selectors) => QuerySelectorAllCore(Css.Selectors.SelectorCache.Parse(selectors));

    private protected NodeList QuerySelectorAllCore(Css.Selectors.SelectorList selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var result = new List<Element>();
        AddQuerySelectorAllResults(selector, result, seen: null);
        return new StaticNodeList(result);
    }

    /// <summary>Returns the descendants, in tree order, that are a <typeparamref name="T"/> and match the selector.</summary>
    private protected IReadOnlyList<T> QuerySelectorAllCore<T>(string selectors) where T : Element => QuerySelectorAllCore<T>(Css.Selectors.SelectorCache.Parse(selectors));

    private protected IReadOnlyList<T> QuerySelectorAllCore<T>(Css.Selectors.SelectorList selector) where T : Element
    {
        ArgumentNullException.ThrowIfNull(selector);
        var result = new List<T>();
        AddQuerySelectorAllResults(selector, result, seen: null);
        return result;
    }

    /// <summary>Adds the descendants, in tree order, that are a <typeparamref name="T"/> and match the selector (<c>:scope</c> is this node), skipping the elements already in <paramref name="seen"/>.</summary>
    internal void AddQuerySelectorAllResults<T>(Css.Selectors.SelectorList selector, List<T> result, HashSet<Element>? seen) where T : Element
    {
        var context = Css.Selectors.SelectorContext.ForDomApi(this);
        if (TryAddQuerySelectorAllResultsById(selector, result, seen, context))
            return;

        Css.Selectors.SelectorQuery.Find(this, selector, context, result, seen);
    }

    /// <summary>
    /// Fast path of <see cref="AddQuerySelectorAllResults"/>: a selector whose subject has an ID can only match the elements
    /// with this ID (case-sensitive outside of quirks mode), found with the ID map of the tree instead of walking the whole
    /// tree. Returns false when the ID map cannot be used.
    /// </summary>
    private bool TryAddQuerySelectorAllResultsById<T>(Css.Selectors.SelectorList selector, List<T> result, HashSet<Element>? seen, Css.Selectors.SelectorContext context) where T : Element
    {
        if (GetSubjectId(selector) is not { } id || GetRoot() is not { } root || root.NodeDocument.Mode == DocumentMode.Quirks || IdMap.GetOrCreateForRoot(root) is not { } idMap)
            return false;

        var candidates = new List<Element>();
        idMap.AddAll(id, candidates);
        if (candidates.Count > 1)
        {
            candidates.Sort(static (a, b) => ReferenceEquals(a, b) ? 0 : a.IsPreceding(b) ? -1 : 1);
        }

        foreach (var candidate in candidates)
        {
            if (candidate is T element && (ReferenceEquals(root, this) || IsAncestorOf(element)) && Css.Selectors.SelectorMatcher.Matches(selector, element, context) && (seen is null || seen.Add(element)))
            {
                result.Add(element);
            }
        }

        return true;
    }

    /// <summary>Returns the ID the subject of the selector must have, when the selector list is a single complex selector whose subject has an ID selector.</summary>
    private static string? GetSubjectId(Css.Selectors.SelectorList selector)
    {
        if (selector.Selectors is not [{ } complex])
            return null;

        foreach (var simple in complex.Compounds[^1].Selectors)
        {
            if (simple is Css.Selectors.IdSelector idSelector)
                return idSelector.Id;
        }

        return null;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // ChildNode
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://dom.spec.whatwg.org/#dom-childnode-before</summary>
    private protected void BeforeCore(ReadOnlySpan<NodeOrString> nodes)
    {
        var parent = ParentNode;
        if (parent is null)
            return;

        var viablePreviousSibling = PreviousSibling;
        while (viablePreviousSibling is not null && Contains(nodes, viablePreviousSibling))
        {
            viablePreviousSibling = viablePreviousSibling.PreviousSibling;
        }

        var node = ConvertNodesIntoNode(nodes, NodeDocument);
        viablePreviousSibling = viablePreviousSibling is null ? parent.FirstChild : viablePreviousSibling.NextSibling;
        parent.PreInsert(node, viablePreviousSibling);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-childnode-after</summary>
    private protected void AfterCore(ReadOnlySpan<NodeOrString> nodes)
    {
        var parent = ParentNode;
        if (parent is null)
            return;

        var viableNextSibling = GetViableNextSibling(nodes);
        var node = ConvertNodesIntoNode(nodes, NodeDocument);
        parent.PreInsert(node, viableNextSibling);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-childnode-replacewith</summary>
    private protected void ReplaceWithCore(ReadOnlySpan<NodeOrString> nodes)
    {
        var parent = ParentNode;
        if (parent is null)
            return;

        var viableNextSibling = GetViableNextSibling(nodes);
        var node = ConvertNodesIntoNode(nodes, NodeDocument);
        if (ReferenceEquals(ParentNode, parent))
        {
            parent.Replace(this, node);
        }
        else
        {
            parent.PreInsert(node, viableNextSibling);
        }
    }

    private Node? GetViableNextSibling(ReadOnlySpan<NodeOrString> nodes)
    {
        var viableNextSibling = NextSibling;
        while (viableNextSibling is not null && Contains(nodes, viableNextSibling))
        {
            viableNextSibling = viableNextSibling.NextSibling;
        }

        return viableNextSibling;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // NonDocumentTypeChildNode
    // ----------------------------------------------------------------------------------------------------------------

    private protected Element? PreviousElementSiblingCore()
    {
        for (var node = PreviousSibling; node is not null; node = node.PreviousSibling)
        {
            if (node is Element element)
                return element;
        }

        return null;
    }

    private protected Element? NextElementSiblingCore()
    {
        for (var node = NextSibling; node is not null; node = node.NextSibling)
        {
            if (node is Element element)
                return element;
        }

        return null;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // NonElementParentNode
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://dom.spec.whatwg.org/#dom-nonelementparentnode-getelementbyid</summary>
    private protected Element? GetElementByIdCore(string elementId)
    {
        ArgumentNullException.ThrowIfNull(elementId);
        if (elementId.Length == 0)
            return null;

        if (IdMap.GetOrCreateForRoot(this) is { } idMap)
            return idMap.Get(elementId);

        return IdCache.GetElementById(this, elementId);
    }
}

/// <summary>
/// Caches the id to element map of a root (document or fragment). The map is only built when the tree is queried several
/// times without being modified in between, so code that alternates mutations and lookups does not pay for building it.
/// </summary>
internal sealed class IdCache
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Node, IdCache> Caches = [];

    private Document? _document;
    private int _version = -1;
    private Dictionary<string, Element>? _map;

    public static Element? GetElementById(Node root, string elementId)
    {
        var cache = Caches.GetOrCreateValue(root);
        var document = root.NodeDocument;
        var version = document.DomVersion;
        if (cache._version != version || !ReferenceEquals(cache._document, document))
        {
            // First lookup since the last mutation: search the tree without building the map
            cache._version = version;
            cache._document = document;
            cache._map = null;
            return FindElementById(root, elementId);
        }

        if (cache._map is null)
        {
            var map = new Dictionary<string, Element>(StringComparer.Ordinal);
            for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
            {
                if (node is Element { IdValue: { } id } element)
                {
                    map.TryAdd(id, element);
                }
            }

            cache._map = map;
        }

        return cache._map.GetValueOrDefault(elementId);
    }

    private static Element? FindElementById(Node root, string elementId)
    {
        for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is Element element && element.IdValue == elementId)
                return element;
        }

        return null;
    }
}
