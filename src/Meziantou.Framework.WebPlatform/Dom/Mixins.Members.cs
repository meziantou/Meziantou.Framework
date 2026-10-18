namespace Meziantou.Framework.WebPlatform.Dom;

// Members of the ParentNode, ChildNode, NonDocumentTypeChildNode and NonElementParentNode mixins.

public partial class Element : IParentNode, IChildNode, INonDocumentTypeChildNode
{
    /// <summary>Gets the live collection of the child elements.</summary>
    [IdlMember("children", "https://dom.spec.whatwg.org/#dom-parentnode-children")]
    public HtmlCollection Children => RareData.Children ??= new HtmlCollection(this, static _ => true, childrenOnly: true, structural: true);

    /// <summary>Gets the first child that is an element, or null when there is none.</summary>
    [IdlMember("firstElementChild", "https://dom.spec.whatwg.org/#dom-parentnode-firstelementchild")]
    public Element? FirstElementChild => FirstElementChildCore();

    /// <summary>Gets the last child that is an element, or null when there is none.</summary>
    [IdlMember("lastElementChild", "https://dom.spec.whatwg.org/#dom-parentnode-lastelementchild")]
    public Element? LastElementChild => LastElementChildCore();

    /// <summary>Gets the number of child elements.</summary>
    [IdlMember("childElementCount", "https://dom.spec.whatwg.org/#dom-parentnode-childelementcount")]
    public uint ChildElementCount => ChildElementCountCore();

    /// <summary>Inserts the nodes before the first child, converting the strings into text nodes.</summary>
    [IdlMember("prepend", "https://dom.spec.whatwg.org/#dom-parentnode-prepend")]
    public void Prepend(params ReadOnlySpan<NodeOrString> nodes) => PrependCore(nodes);

    /// <summary>Inserts the nodes after the last child, converting the strings into text nodes.</summary>
    [IdlMember("append", "https://dom.spec.whatwg.org/#dom-parentnode-append")]
    public void Append(params ReadOnlySpan<NodeOrString> nodes) => AppendCore(nodes);

    /// <summary>Replaces all the children with the given nodes, converting the strings into text nodes.</summary>
    [IdlMember("replaceChildren", "https://dom.spec.whatwg.org/#dom-parentnode-replacechildren")]
    public void ReplaceChildren(params ReadOnlySpan<NodeOrString> nodes) => ReplaceChildrenCore(nodes);

    /// <summary>
    /// Moves the node, preserving its state, before the given child (or to the end when the child is null).
    /// </summary>
    [IdlMember("moveBefore", "https://dom.spec.whatwg.org/#dom-parentnode-movebefore")]
    public void MoveBefore(Node node, Node? child) => MoveBeforeCore(node, child);

    /// <summary>Returns the first descendant element, in tree order, that matches the selectors, or null.</summary>
    [IdlMember("querySelector", "https://dom.spec.whatwg.org/#dom-parentnode-queryselector")]
    public Element? QuerySelector(string selectors) => QuerySelectorCore(selectors);

    /// <summary>Returns the descendant elements, in tree order, that match the selectors.</summary>
    [IdlMember("querySelectorAll", "https://dom.spec.whatwg.org/#dom-parentnode-queryselectorall")]
    public NodeList QuerySelectorAll(string selectors) => QuerySelectorAllCore(selectors);

    /// <summary>
    /// Returns the first descendant element, in tree order, that matches the parsed selector list, or null.
    /// </summary>
    [NotInIdl("Overload taking a parsed selector")]
    public Element? QuerySelector(Css.Selectors.SelectorList selectors) => QuerySelectorCore(selectors);

    /// <summary>Returns the descendant elements, in tree order, that match the parsed selector list.</summary>
    [NotInIdl("Overload taking a parsed selector")]
    public NodeList QuerySelectorAll(Css.Selectors.SelectorList selectors) => QuerySelectorAllCore(selectors);

    /// <summary>Returns the first descendant, in tree order, that is a <typeparamref name="T"/> and matches the selector.</summary>
    [NotInIdl("LINQ helper")]
    public T? QuerySelector<T>(string selectors) where T : Element => QuerySelectorCore<T>(selectors);

    /// <summary>Returns the first descendant, in tree order, that is a <typeparamref name="T"/> and matches the parsed selector.</summary>
    [NotInIdl("LINQ helper")]
    public T? QuerySelector<T>(Css.Selectors.SelectorList selectors) where T : Element => QuerySelectorCore<T>(selectors);

    /// <summary>Returns the descendants, in tree order, that are a <typeparamref name="T"/> and match the selector.</summary>
    [NotInIdl("LINQ helper")]
    public IReadOnlyList<T> QuerySelectorAll<T>(string selectors) where T : Element => QuerySelectorAllCore<T>(selectors);

    /// <summary>Returns the descendants, in tree order, that are a <typeparamref name="T"/> and match the parsed selector.</summary>
    [NotInIdl("LINQ helper")]
    public IReadOnlyList<T> QuerySelectorAll<T>(Css.Selectors.SelectorList selectors) where T : Element => QuerySelectorAllCore<T>(selectors);

    /// <summary>Inserts the nodes just before this node, converting the strings into text nodes.</summary>
    [IdlMember("before", "https://dom.spec.whatwg.org/#dom-childnode-before")]
    public void Before(params ReadOnlySpan<NodeOrString> nodes) => BeforeCore(nodes);

    /// <summary>Inserts the nodes just after this node, converting the strings into text nodes.</summary>
    [IdlMember("after", "https://dom.spec.whatwg.org/#dom-childnode-after")]
    public void After(params ReadOnlySpan<NodeOrString> nodes) => AfterCore(nodes);

    /// <summary>
    /// Replaces this node with the given nodes in its parent, converting the strings into text nodes.
    /// </summary>
    [IdlMember("replaceWith", "https://dom.spec.whatwg.org/#dom-childnode-replacewith")]
    public void ReplaceWith(params ReadOnlySpan<NodeOrString> nodes) => ReplaceWithCore(nodes);

    /// <summary>Removes this node from its parent, if any.</summary>
    [IdlMember("remove", "https://dom.spec.whatwg.org/#dom-childnode-remove")]
    public void Remove() => RemoveFromParent();

    /// <summary>Gets the closest preceding sibling that is an element, or null when there is none.</summary>
    [IdlMember("previousElementSibling", "https://dom.spec.whatwg.org/#dom-nondocumenttypechildnode-previouselementsibling")]
    public Element? PreviousElementSibling => PreviousElementSiblingCore();

    /// <summary>Gets the closest following sibling that is an element, or null when there is none.</summary>
    [IdlMember("nextElementSibling", "https://dom.spec.whatwg.org/#dom-nondocumenttypechildnode-nextelementsibling")]
    public Element? NextElementSibling => NextElementSiblingCore();
}

public partial class Document : IParentNode, INonElementParentNode
{
    /// <summary>Gets the live collection of the child elements.</summary>
    [IdlMember("children", "https://dom.spec.whatwg.org/#dom-parentnode-children")]
    public HtmlCollection Children => RareData.Children ??= new HtmlCollection(this, static _ => true, childrenOnly: true, structural: true);

    /// <summary>Gets the first child that is an element, or null when there is none.</summary>
    [IdlMember("firstElementChild", "https://dom.spec.whatwg.org/#dom-parentnode-firstelementchild")]
    public Element? FirstElementChild => FirstElementChildCore();

    /// <summary>Gets the last child that is an element, or null when there is none.</summary>
    [IdlMember("lastElementChild", "https://dom.spec.whatwg.org/#dom-parentnode-lastelementchild")]
    public Element? LastElementChild => LastElementChildCore();

    /// <summary>Gets the number of child elements.</summary>
    [IdlMember("childElementCount", "https://dom.spec.whatwg.org/#dom-parentnode-childelementcount")]
    public uint ChildElementCount => ChildElementCountCore();

    /// <summary>Inserts the nodes before the first child, converting the strings into text nodes.</summary>
    [IdlMember("prepend", "https://dom.spec.whatwg.org/#dom-parentnode-prepend")]
    public void Prepend(params ReadOnlySpan<NodeOrString> nodes) => PrependCore(nodes);

    /// <summary>Inserts the nodes after the last child, converting the strings into text nodes.</summary>
    [IdlMember("append", "https://dom.spec.whatwg.org/#dom-parentnode-append")]
    public void Append(params ReadOnlySpan<NodeOrString> nodes) => AppendCore(nodes);

    /// <summary>Replaces all the children with the given nodes, converting the strings into text nodes.</summary>
    [IdlMember("replaceChildren", "https://dom.spec.whatwg.org/#dom-parentnode-replacechildren")]
    public void ReplaceChildren(params ReadOnlySpan<NodeOrString> nodes) => ReplaceChildrenCore(nodes);

    /// <summary>
    /// Moves the node, preserving its state, before the given child (or to the end when the child is null).
    /// </summary>
    [IdlMember("moveBefore", "https://dom.spec.whatwg.org/#dom-parentnode-movebefore")]
    public void MoveBefore(Node node, Node? child) => MoveBeforeCore(node, child);

    /// <summary>Returns the first descendant element, in tree order, that matches the selectors, or null.</summary>
    [IdlMember("querySelector", "https://dom.spec.whatwg.org/#dom-parentnode-queryselector")]
    public Element? QuerySelector(string selectors) => QuerySelectorCore(selectors);

    /// <summary>Returns the descendant elements, in tree order, that match the selectors.</summary>
    [IdlMember("querySelectorAll", "https://dom.spec.whatwg.org/#dom-parentnode-queryselectorall")]
    public NodeList QuerySelectorAll(string selectors) => QuerySelectorAllCore(selectors);

    /// <summary>
    /// Returns the first descendant element, in tree order, that matches the parsed selector list, or null.
    /// </summary>
    [NotInIdl("Overload taking a parsed selector")]
    public Element? QuerySelector(Css.Selectors.SelectorList selectors) => QuerySelectorCore(selectors);

    /// <summary>Returns the descendant elements, in tree order, that match the parsed selector list.</summary>
    [NotInIdl("Overload taking a parsed selector")]
    public NodeList QuerySelectorAll(Css.Selectors.SelectorList selectors) => QuerySelectorAllCore(selectors);

    /// <summary>Returns the first descendant, in tree order, that is a <typeparamref name="T"/> and matches the selector.</summary>
    [NotInIdl("LINQ helper")]
    public T? QuerySelector<T>(string selectors) where T : Element => QuerySelectorCore<T>(selectors);

    /// <summary>Returns the first descendant, in tree order, that is a <typeparamref name="T"/> and matches the parsed selector.</summary>
    [NotInIdl("LINQ helper")]
    public T? QuerySelector<T>(Css.Selectors.SelectorList selectors) where T : Element => QuerySelectorCore<T>(selectors);

    /// <summary>Returns the descendants, in tree order, that are a <typeparamref name="T"/> and match the selector.</summary>
    [NotInIdl("LINQ helper")]
    public IReadOnlyList<T> QuerySelectorAll<T>(string selectors) where T : Element => QuerySelectorAllCore<T>(selectors);

    /// <summary>Returns the descendants, in tree order, that are a <typeparamref name="T"/> and match the parsed selector.</summary>
    [NotInIdl("LINQ helper")]
    public IReadOnlyList<T> QuerySelectorAll<T>(Css.Selectors.SelectorList selectors) where T : Element => QuerySelectorAllCore<T>(selectors);
}

public partial class DocumentFragment : IParentNode, INonElementParentNode
{
    /// <summary>Gets the live collection of the child elements.</summary>
    [IdlMember("children", "https://dom.spec.whatwg.org/#dom-parentnode-children")]
    public HtmlCollection Children => RareData.Children ??= new HtmlCollection(this, static _ => true, childrenOnly: true, structural: true);

    /// <summary>Gets the first child that is an element, or null when there is none.</summary>
    [IdlMember("firstElementChild", "https://dom.spec.whatwg.org/#dom-parentnode-firstelementchild")]
    public Element? FirstElementChild => FirstElementChildCore();

    /// <summary>Gets the last child that is an element, or null when there is none.</summary>
    [IdlMember("lastElementChild", "https://dom.spec.whatwg.org/#dom-parentnode-lastelementchild")]
    public Element? LastElementChild => LastElementChildCore();

    /// <summary>Gets the number of child elements.</summary>
    [IdlMember("childElementCount", "https://dom.spec.whatwg.org/#dom-parentnode-childelementcount")]
    public uint ChildElementCount => ChildElementCountCore();

    /// <summary>Inserts the nodes before the first child, converting the strings into text nodes.</summary>
    [IdlMember("prepend", "https://dom.spec.whatwg.org/#dom-parentnode-prepend")]
    public void Prepend(params ReadOnlySpan<NodeOrString> nodes) => PrependCore(nodes);

    /// <summary>Inserts the nodes after the last child, converting the strings into text nodes.</summary>
    [IdlMember("append", "https://dom.spec.whatwg.org/#dom-parentnode-append")]
    public void Append(params ReadOnlySpan<NodeOrString> nodes) => AppendCore(nodes);

    /// <summary>Replaces all the children with the given nodes, converting the strings into text nodes.</summary>
    [IdlMember("replaceChildren", "https://dom.spec.whatwg.org/#dom-parentnode-replacechildren")]
    public void ReplaceChildren(params ReadOnlySpan<NodeOrString> nodes) => ReplaceChildrenCore(nodes);

    /// <summary>
    /// Moves the node, preserving its state, before the given child (or to the end when the child is null).
    /// </summary>
    [IdlMember("moveBefore", "https://dom.spec.whatwg.org/#dom-parentnode-movebefore")]
    public void MoveBefore(Node node, Node? child) => MoveBeforeCore(node, child);

    /// <summary>Returns the first descendant element, in tree order, that matches the selectors, or null.</summary>
    [IdlMember("querySelector", "https://dom.spec.whatwg.org/#dom-parentnode-queryselector")]
    public Element? QuerySelector(string selectors) => QuerySelectorCore(selectors);

    /// <summary>Returns the descendant elements, in tree order, that match the selectors.</summary>
    [IdlMember("querySelectorAll", "https://dom.spec.whatwg.org/#dom-parentnode-queryselectorall")]
    public NodeList QuerySelectorAll(string selectors) => QuerySelectorAllCore(selectors);

    /// <summary>
    /// Returns the first descendant element, in tree order, that matches the parsed selector list, or null.
    /// </summary>
    [NotInIdl("Overload taking a parsed selector")]
    public Element? QuerySelector(Css.Selectors.SelectorList selectors) => QuerySelectorCore(selectors);

    /// <summary>Returns the descendant elements, in tree order, that match the parsed selector list.</summary>
    [NotInIdl("Overload taking a parsed selector")]
    public NodeList QuerySelectorAll(Css.Selectors.SelectorList selectors) => QuerySelectorAllCore(selectors);

    /// <summary>Returns the first descendant, in tree order, that is a <typeparamref name="T"/> and matches the selector.</summary>
    [NotInIdl("LINQ helper")]
    public T? QuerySelector<T>(string selectors) where T : Element => QuerySelectorCore<T>(selectors);

    /// <summary>Returns the first descendant, in tree order, that is a <typeparamref name="T"/> and matches the parsed selector.</summary>
    [NotInIdl("LINQ helper")]
    public T? QuerySelector<T>(Css.Selectors.SelectorList selectors) where T : Element => QuerySelectorCore<T>(selectors);

    /// <summary>Returns the descendants, in tree order, that are a <typeparamref name="T"/> and match the selector.</summary>
    [NotInIdl("LINQ helper")]
    public IReadOnlyList<T> QuerySelectorAll<T>(string selectors) where T : Element => QuerySelectorAllCore<T>(selectors);

    /// <summary>Returns the descendants, in tree order, that are a <typeparamref name="T"/> and match the parsed selector.</summary>
    [NotInIdl("LINQ helper")]
    public IReadOnlyList<T> QuerySelectorAll<T>(Css.Selectors.SelectorList selectors) where T : Element => QuerySelectorAllCore<T>(selectors);

    /// <summary>Returns the first descendant element, in tree order, whose ID is the given value, or null.</summary>
    [IdlMember("getElementById", "https://dom.spec.whatwg.org/#dom-nonelementparentnode-getelementbyid")]
    public Element? GetElementById(string elementId) => GetElementByIdCore(elementId);
}

public abstract partial class CharacterData : IChildNode, INonDocumentTypeChildNode
{
    /// <summary>Inserts the nodes just before this node, converting the strings into text nodes.</summary>
    [IdlMember("before", "https://dom.spec.whatwg.org/#dom-childnode-before")]
    public void Before(params ReadOnlySpan<NodeOrString> nodes) => BeforeCore(nodes);

    /// <summary>Inserts the nodes just after this node, converting the strings into text nodes.</summary>
    [IdlMember("after", "https://dom.spec.whatwg.org/#dom-childnode-after")]
    public void After(params ReadOnlySpan<NodeOrString> nodes) => AfterCore(nodes);

    /// <summary>
    /// Replaces this node with the given nodes in its parent, converting the strings into text nodes.
    /// </summary>
    [IdlMember("replaceWith", "https://dom.spec.whatwg.org/#dom-childnode-replacewith")]
    public void ReplaceWith(params ReadOnlySpan<NodeOrString> nodes) => ReplaceWithCore(nodes);

    /// <summary>Removes this node from its parent, if any.</summary>
    [IdlMember("remove", "https://dom.spec.whatwg.org/#dom-childnode-remove")]
    public void Remove() => RemoveFromParent();

    /// <summary>Gets the closest preceding sibling that is an element, or null when there is none.</summary>
    [IdlMember("previousElementSibling", "https://dom.spec.whatwg.org/#dom-nondocumenttypechildnode-previouselementsibling")]
    public Element? PreviousElementSibling => PreviousElementSiblingCore();

    /// <summary>Gets the closest following sibling that is an element, or null when there is none.</summary>
    [IdlMember("nextElementSibling", "https://dom.spec.whatwg.org/#dom-nondocumenttypechildnode-nextelementsibling")]
    public Element? NextElementSibling => NextElementSiblingCore();
}

public sealed partial class DocumentType : IChildNode
{
    /// <summary>Inserts the nodes just before this node, converting the strings into text nodes.</summary>
    [IdlMember("before", "https://dom.spec.whatwg.org/#dom-childnode-before")]
    public void Before(params ReadOnlySpan<NodeOrString> nodes) => BeforeCore(nodes);

    /// <summary>Inserts the nodes just after this node, converting the strings into text nodes.</summary>
    [IdlMember("after", "https://dom.spec.whatwg.org/#dom-childnode-after")]
    public void After(params ReadOnlySpan<NodeOrString> nodes) => AfterCore(nodes);

    /// <summary>
    /// Replaces this node with the given nodes in its parent, converting the strings into text nodes.
    /// </summary>
    [IdlMember("replaceWith", "https://dom.spec.whatwg.org/#dom-childnode-replacewith")]
    public void ReplaceWith(params ReadOnlySpan<NodeOrString> nodes) => ReplaceWithCore(nodes);

    /// <summary>Removes this node from its parent, if any.</summary>
    [IdlMember("remove", "https://dom.spec.whatwg.org/#dom-childnode-remove")]
    public void Remove() => RemoveFromParent();
}
