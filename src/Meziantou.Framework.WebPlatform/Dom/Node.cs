using System.Diagnostics;
using System.Text;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#interface-node</summary>
/// <seealso href="https://dom.spec.whatwg.org/#node"/>
[IdlInterface("Node", "https://dom.spec.whatwg.org/#node")]
public abstract partial class Node : EventTarget
{
    /// <summary>The node type of an element.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-element_node"/>
    [IdlMember("ELEMENT_NODE", "https://dom.spec.whatwg.org/#dom-node-element_node")]
    public const ushort ElementNode = 1;

    /// <summary>The node type of an attribute.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-attribute_node"/>
    [IdlMember("ATTRIBUTE_NODE", "https://dom.spec.whatwg.org/#dom-node-attribute_node")]
    public const ushort AttributeNode = 2;

    /// <summary>The node type of a text node.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-text_node"/>
    [IdlMember("TEXT_NODE", "https://dom.spec.whatwg.org/#dom-node-text_node")]
    public const ushort TextNode = 3;

    /// <summary>The node type of a CDATA section.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-cdata_section_node"/>
    [IdlMember("CDATA_SECTION_NODE", "https://dom.spec.whatwg.org/#dom-node-cdata_section_node")]
    public const ushort CDataSectionNode = 4;

    /// <summary>The legacy node type of an entity reference (never created).</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-entity_reference_node"/>
    [IdlMember("ENTITY_REFERENCE_NODE", "https://dom.spec.whatwg.org/#dom-node-entity_reference_node")]
    public const ushort EntityReferenceNode = 5;

    /// <summary>The legacy node type of an entity (never created).</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-entity_node"/>
    [IdlMember("ENTITY_NODE", "https://dom.spec.whatwg.org/#dom-node-entity_node")]
    public const ushort EntityNode = 6;

    /// <summary>The node type of a processing instruction.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-processing_instruction_node"/>
    [IdlMember("PROCESSING_INSTRUCTION_NODE", "https://dom.spec.whatwg.org/#dom-node-processing_instruction_node")]
    public const ushort ProcessingInstructionNode = 7;

    /// <summary>The node type of a comment.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-comment_node"/>
    [IdlMember("COMMENT_NODE", "https://dom.spec.whatwg.org/#dom-node-comment_node")]
    public const ushort CommentNode = 8;

    /// <summary>The node type of a document.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-document_node"/>
    [IdlMember("DOCUMENT_NODE", "https://dom.spec.whatwg.org/#dom-node-document_node")]
    public const ushort DocumentNode = 9;

    /// <summary>The node type of a document type.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-document_type_node"/>
    [IdlMember("DOCUMENT_TYPE_NODE", "https://dom.spec.whatwg.org/#dom-node-document_type_node")]
    public const ushort DocumentTypeNode = 10;

    /// <summary>The node type of a document fragment.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-document_fragment_node"/>
    [IdlMember("DOCUMENT_FRAGMENT_NODE", "https://dom.spec.whatwg.org/#dom-node-document_fragment_node")]
    public const ushort DocumentFragmentNode = 11;

    /// <summary>The legacy node type of a notation (never created).</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-notation_node"/>
    [IdlMember("NOTATION_NODE", "https://dom.spec.whatwg.org/#dom-node-notation_node")]
    public const ushort NotationNode = 12;

    /// <summary>The bit set by <c>compareDocumentPosition()</c> when the nodes are not in the same tree.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-document_position_disconnected"/>
    [IdlMember("DOCUMENT_POSITION_DISCONNECTED", "https://dom.spec.whatwg.org/#dom-node-document_position_disconnected")]
    public const ushort DocumentPositionDisconnected = 0x01;

    /// <summary>The bit set by <c>compareDocumentPosition()</c> when the other node precedes the node.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-document_position_preceding"/>
    [IdlMember("DOCUMENT_POSITION_PRECEDING", "https://dom.spec.whatwg.org/#dom-node-document_position_preceding")]
    public const ushort DocumentPositionPreceding = 0x02;

    /// <summary>The bit set by <c>compareDocumentPosition()</c> when the other node follows the node.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-document_position_following"/>
    [IdlMember("DOCUMENT_POSITION_FOLLOWING", "https://dom.spec.whatwg.org/#dom-node-document_position_following")]
    public const ushort DocumentPositionFollowing = 0x04;

    /// <summary>
    /// The bit set by <c>compareDocumentPosition()</c> when the other node is an ancestor of the node.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-document_position_contains"/>
    [IdlMember("DOCUMENT_POSITION_CONTAINS", "https://dom.spec.whatwg.org/#dom-node-document_position_contains")]
    public const ushort DocumentPositionContains = 0x08;

    /// <summary>
    /// The bit set by <c>compareDocumentPosition()</c> when the other node is a descendant of the node.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-document_position_contained_by"/>
    [IdlMember("DOCUMENT_POSITION_CONTAINED_BY", "https://dom.spec.whatwg.org/#dom-node-document_position_contained_by")]
    public const ushort DocumentPositionContainedBy = 0x10;

    /// <summary>
    /// The bit set by <c>compareDocumentPosition()</c> when the order of the disconnected nodes is implementation-
    /// specific.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-document_position_implementation_specific"/>
    [IdlMember("DOCUMENT_POSITION_IMPLEMENTATION_SPECIFIC", "https://dom.spec.whatwg.org/#dom-node-document_position_implementation_specific")]
    public const ushort DocumentPositionImplementationSpecific = 0x20;

    private Document _nodeDocument;
    private Node? _parent;
    private Node? _previousSibling;
    private Node? _nextSibling;
    private Node? _firstChild;
    private Node? _lastChild;
    private int _childCount;
    private bool _isConnected;

    /// <summary>Whether the root of the connected node is a shadow root (maintained with the connected flag).</summary>
    private bool _isInShadowTree;

    private protected Node(Document? nodeDocument)
    {
        // A Document is its own node document; it passes null here and sets it in its constructor.
        _nodeDocument = nodeDocument!;
    }

    /// <summary>The node document (https://dom.spec.whatwg.org/#concept-node-document).</summary>
    internal Document NodeDocument
    {
        get => _nodeDocument;
        set => _nodeDocument = value;
    }

    internal int ChildCount => _childCount;

    /// <summary>
    /// Incremented whenever the children of this node change (used by live collection caches). The version is stored in the
    /// rare data, which is created when the version is requested: the children of the nodes without rare data are not compared.
    /// </summary>
    internal int ChildrenVersion => RareData.ChildrenVersion;

    /// <summary>Gets the type of the node, one of the node type constants.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-nodetype"/>
    [IdlMember("nodeType", "https://dom.spec.whatwg.org/#dom-node-nodetype")]
    public abstract ushort NodeType { get; }

    internal override Window? RelevantGlobal => NodeDocument.RelevantWindow;

    /// <summary>The realms where the nodes adopted by a document of another realm were created.</summary>
    private static readonly ConditionalWeakTable<Node, Window> CreationRealms = [];

    /// <summary>
    /// The global object of the realm where the node was created (https://dom.spec.whatwg.org/#concept-create-node): the
    /// realm of its node document, unless it was adopted from a document of another realm. The JavaScript object of the node
    /// is created in this realm, even when it is first accessed after the adoption.
    /// </summary>
    internal Window? CreationRealmWindow => CreationRealms.TryGetValue(this, out var window) ? window : NodeDocument.RealmWindow;

    /// <summary>Records the realm where the node was created, before it is adopted by a document of another realm.</summary>
    internal void KeepCreationRealm(Window window) => CreationRealms.TryAdd(this, window);

    /// <summary>Gets the name of the node, which depends on its type.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-nodename"/>
    [IdlMember("nodeName", "https://dom.spec.whatwg.org/#dom-node-nodename")]
    public abstract string NodeName { get; }

    /// <summary>https://dom.spec.whatwg.org/#dom-node-baseuri</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-baseuri"/>
    [IdlMember("baseURI", "https://dom.spec.whatwg.org/#dom-node-baseuri")]
    public string BaseUri => NodeDocument.DocumentBaseUrl.Serialize();

    /// <summary>Gets a value indicating whether the shadow-including root of the node is a document.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-isconnected"/>
    [IdlMember("isConnected", "https://dom.spec.whatwg.org/#dom-node-isconnected")]
    public bool IsConnected => _isConnected || this is Document;

    /// <summary>Whether the node is in a document tree (https://dom.spec.whatwg.org/#in-a-document-tree), a document excluded: its root is a document.</summary>
    internal bool IsInDocumentTree => _isConnected && !_isInShadowTree;

    /// <summary>Sets the connected flag of the shadow-including inclusive descendants (maintained on insertion and removal).</summary>
    internal void SetConnected(bool connected)
    {
        if (_firstChild is null && this is not Element { AttachedShadowRoot: not null })
        {
            _isConnected = connected;
            _isInShadowTree = connected && IsInShadowTreeOfParent(this);
            return;
        }

        // The nodes are visited in shadow-including tree order: the flag of the parent is set before the flag of its children
        foreach (var node in WalkShadowIncludingInclusiveDescendants())
        {
            node._isConnected = connected;
            node._isInShadowTree = connected && IsInShadowTreeOfParent(node);
        }

        static bool IsInShadowTreeOfParent(Node node) => node is ShadowRoot || node._parent is ShadowRoot || node._parent is { _isInShadowTree: true };
    }

    /// <summary>Gets the document that owns the node, or null when the node is a document.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-ownerdocument"/>
    [IdlMember("ownerDocument", "https://dom.spec.whatwg.org/#dom-node-ownerdocument")]
    public Document? OwnerDocument => this is Document ? null : NodeDocument;

    /// <summary>
    /// Returns the root of the tree that contains the node, or its shadow-including root when <c>composed</c> is set.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-getrootnode"/>
    [IdlMember("getRootNode", "https://dom.spec.whatwg.org/#dom-node-getrootnode")]
    public Node GetRootNode(GetRootNodeOptions? options = null) => options?.Composed == true ? GetShadowIncludingRoot() : GetRoot();

    /// <summary>Gets the parent of the node, or null.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-parentnode"/>
    [IdlMember("parentNode", "https://dom.spec.whatwg.org/#dom-node-parentnode")]
    public Node? ParentNode => _parent;

    /// <summary>Gets the parent of the node when it is an element; otherwise null.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-parentelement"/>
    [IdlMember("parentElement", "https://dom.spec.whatwg.org/#dom-node-parentelement")]
    public Element? ParentElement => _parent as Element;

    /// <summary>Returns whether the node has at least one child.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-haschildnodes"/>
    [IdlMember("hasChildNodes", "https://dom.spec.whatwg.org/#dom-node-haschildnodes")]
    public bool HasChildNodes() => _firstChild is not null;

    /// <summary>Gets the live list of the children of the node.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-childnodes"/>
    [IdlMember("childNodes", "https://dom.spec.whatwg.org/#dom-node-childnodes")]
    public NodeList ChildNodes => RareData.ChildNodes ??= new ChildNodeList(this);

    /// <summary>Enumerates the children of the node: unlike <see cref="ChildNodes"/>, it allocates neither the rare data nor an enumerator.</summary>
    internal NodeEnumerable EnumerateChildNodes() => new(_firstChild);

    /// <summary>The data that most nodes do not need, allocated on first use to keep nodes small.</summary>
    internal NodeRareData RareData => (NodeRareData)(ListenersOrRareData ??= CreateRareData());

    internal NodeRareData? RareDataOrNull => (NodeRareData?)ListenersOrRareData;

    private protected virtual NodeRareData CreateRareData() => new();

    internal override Dictionary<string, EventHandlerAttribute>? GetEventHandlerMap(bool create)
    {
        if (!create)
            return RareDataOrNull?.EventHandlers;

        return RareData.EventHandlers ??= new Dictionary<string, EventHandlerAttribute>(StringComparer.Ordinal);
    }

    /// <summary>Gets the first child of the node, or null.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-firstchild"/>
    [IdlMember("firstChild", "https://dom.spec.whatwg.org/#dom-node-firstchild")]
    public Node? FirstChild => _firstChild;

    /// <summary>Gets the last child of the node, or null.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-lastchild"/>
    [IdlMember("lastChild", "https://dom.spec.whatwg.org/#dom-node-lastchild")]
    public Node? LastChild => _lastChild;

    /// <summary>Gets the previous sibling of the node, or null.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-previoussibling"/>
    [IdlMember("previousSibling", "https://dom.spec.whatwg.org/#dom-node-previoussibling")]
    public Node? PreviousSibling => _previousSibling;

    /// <summary>Gets the next sibling of the node, or null.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-nextsibling"/>
    [IdlMember("nextSibling", "https://dom.spec.whatwg.org/#dom-node-nextsibling")]
    public Node? NextSibling => _nextSibling;

    /// <summary>
    /// Gets or sets the value of the node: the data of the character data nodes and attributes, null for the other
    /// nodes.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-nodevalue"/>
    [IdlMember("nodeValue", "https://dom.spec.whatwg.org/#dom-node-nodevalue")]
    public virtual string? NodeValue
    {
        get => null;
        set
        {
            // Setting nodeValue on a node without a value does nothing
        }
    }

    /// <summary>Gets or sets the text content of the node and its descendants.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-textcontent"/>
    [IdlMember("textContent", "https://dom.spec.whatwg.org/#dom-node-textcontent")]
    public virtual string? TextContent
    {
        get => null;
        set
        {
            // Setting textContent on a document or doctype does nothing
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-descendant-text-content</summary>
    internal string GetDescendantTextContent()
    {
        // Fast path: a single Text child
        if (_firstChild is Text onlyText && onlyText._nextSibling is null)
            return onlyText.Data;

        var builder = new StringBuilder();
        for (var node = _firstChild; node is not null; node = node.GetNextInTreeOrder(this))
        {
            if (node is Text text)
            {
                builder.Append(text.Data);
            }
        }

        return builder.ToString();
    }

    /// <summary>https://dom.spec.whatwg.org/#string-replace-all</summary>
    /// <summary>https://dom.spec.whatwg.org/#concept-child-text-content</summary>
    internal string GetChildTextContent()
    {
        var builder = new System.Text.StringBuilder();
        for (var child = FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is Text text)
            {
                builder.Append(text.Data);
            }
        }

        return builder.ToString();
    }

    internal void StringReplaceAll(string value)
    {
        Node? node = null;
        if (value.Length > 0)
        {
            node = new Text(NodeDocument, value);
        }

        ReplaceAll(node);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-node-normalize</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-normalize"/>
    [IdlMember("normalize", "https://dom.spec.whatwg.org/#dom-node-normalize")]
    public void Normalize()
    {
        using var ceReactions = Html.CEReactionsScope.Enter();
        // Collect the exclusive Text nodes first as the loop mutates the tree
        var textNodes = new List<Text>();
        for (var node = _firstChild; node is not null; node = node.GetNextInTreeOrder(this))
        {
            if (node is Text text and not CDataSection)
            {
                textNodes.Add(text);
            }
        }

        foreach (var node in textNodes)
        {
            // The node may have been removed as a contiguous Text node of a previous node
            if (node._parent is null || !IsAncestorOf(node))
                continue;

            var length = node.Length;
            if (length == 0)
            {
                node.RemoveFromParent();
                continue;
            }

            StringBuilder? data = null;
            for (var sibling = node._nextSibling; sibling is Text siblingText and not CDataSection; sibling = sibling._nextSibling)
            {
                data ??= new StringBuilder();
                data.Append(siblingText.Data);
            }

            if (data is null)
                continue;

            node.ReplaceData(length, 0, data.ToString());

            var currentNode = node._nextSibling;
            var document = NodeDocument;
            while (currentNode is Text currentText and not CDataSection)
            {
                if (document.HasLiveRanges)
                {
                    var currentIndex = currentNode.GetIndex();
                    foreach (var range in document.GetLiveRangesSnapshot())
                    {
                        range.OnNormalize(node, currentText, currentNode._parent!, currentIndex, length);
                    }
                }

                length += currentText.Length;
                currentNode = currentNode._nextSibling;
            }

            var current = node._nextSibling;
            while (current is Text && current is not CDataSection)
            {
                var next = current._nextSibling;
                current.RemoveFromParent();
                current = next;
            }
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-node-clonenode</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-clonenode"/>
    [IdlMember("cloneNode", "https://dom.spec.whatwg.org/#dom-node-clonenode")]
    public Node CloneNode(bool subtree = false)
    {
        if (this is ShadowRoot)
            throw DomException.NotSupported("Shadow roots cannot be cloned");

        using var scope = CEReactionsScope.Enter();
        return Clone(document: null, subtree, parent: null, fallbackRegistry: null);
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-node-clone</summary>
    internal Node Clone(Document? document, bool subtree, Node? parent, CustomElementRegistry? fallbackRegistry)
    {
        document ??= NodeDocument;
        Node copy;
        if (this is Element element)
        {
            copy = element.CloneElement(document, fallbackRegistry);
        }
        else
        {
            copy = CloneSingleNode(document);
            if (copy is Document copyDocument)
            {
                if (this is Document { CustomElementRegistryStorage.IsScoped: true } original)
                {
                    copyDocument.CustomElementRegistryStorage = original.CustomElementRegistryStorage;
                }

                document = copyDocument;
            }
        }

        CloningSteps(copy, subtree);

        if (parent is not null)
        {
            parent.AppendChildCore(copy);
        }

        if (subtree)
        {
            for (var child = _firstChild; child is not null; child = child._nextSibling)
            {
                child.Clone(document, subtree: true, copy, fallbackRegistry);
            }
        }

        if (this is Element { AttachedShadowRoot: { Clonable: true } shadowRoot } && copy is Element copyElement)
        {
            var shadowRootRegistry = shadowRoot.CustomElementRegistryStorage;
            if (CustomElementRegistry.IsGlobal(shadowRootRegistry))
            {
                shadowRootRegistry = CustomElementRegistry.GetEffectiveGlobal(document.CustomElementRegistryStorage);
            }

            var copyShadow = copyElement.AttachShadowCore(shadowRoot.Mode, shadowRoot.Clonable, shadowRoot.Serializable, shadowRoot.DelegatesFocus, shadowRoot.SlotAssignment, shadowRootRegistry);
            copyShadow.Declarative = shadowRoot.Declarative;
            copyShadow.KeepCustomElementRegistryNull = shadowRoot.KeepCustomElementRegistryNull;
            for (var child = shadowRoot._firstChild; child is not null; child = child._nextSibling)
            {
                child.Clone(document, subtree: true, copyShadow, fallbackRegistry: null);
            }
        }

        return copy;
    }

    /// <summary>Creates a copy of this node without its children (https://dom.spec.whatwg.org/#clone-a-single-node).</summary>
    internal abstract Node CloneSingleNode(Document document);

    /// <summary>The cloning steps of a node (https://dom.spec.whatwg.org/#concept-node-clone-ext).</summary>
    internal virtual void CloningSteps(Node copy, bool subtree)
    {
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-node-isequalnode</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-isequalnode"/>
    [IdlMember("isEqualNode", "https://dom.spec.whatwg.org/#dom-node-isequalnode")]
    public bool IsEqualNode(Node? otherNode)
    {
        if (otherNode is null)
            return false;

        if (NodeType != otherNode.NodeType)
            return false;

        if (!IsEqualNodeCore(otherNode))
            return false;

        if (_childCount != otherNode._childCount)
            return false;

        var child = _firstChild;
        var otherChild = otherNode._firstChild;
        while (child is not null)
        {
            if (!child.IsEqualNode(otherChild))
                return false;

            child = child._nextSibling;
            otherChild = otherChild!._nextSibling;
        }

        return true;
    }

    /// <summary>Compares the node-specific properties for <see cref="IsEqualNode"/> (children are compared by the caller).</summary>
    private protected abstract bool IsEqualNodeCore(Node otherNode);

    /// <summary>Returns whether the given node is this node.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-issamenode"/>
    [IdlMember("isSameNode", "https://dom.spec.whatwg.org/#dom-node-issamenode")]
    public bool IsSameNode(Node? otherNode) => ReferenceEquals(this, otherNode);

    /// <summary>https://dom.spec.whatwg.org/#dom-node-comparedocumentposition</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-comparedocumentposition"/>
    [IdlMember("compareDocumentPosition", "https://dom.spec.whatwg.org/#dom-node-comparedocumentposition")]
    public ushort CompareDocumentPosition(Node other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(this, other))
            return 0;

        Node? node1 = other;
        Node? node2 = this;
        Attr? attr1 = null;
        Attr? attr2 = null;
        if (node1 is Attr otherAttr)
        {
            attr1 = otherAttr;
            node1 = otherAttr.OwnerElement;
        }

        if (node2 is Attr thisAttr)
        {
            attr2 = thisAttr;
            node2 = thisAttr.OwnerElement;
            if (attr1 is not null && node1 is not null && ReferenceEquals(node2, node1))
            {
                foreach (var attr in ((Element)node2!).GetAttributeNodesForComparison())
                {
                    if (ReferenceEquals(attr, attr1))
                        return DocumentPositionImplementationSpecific | DocumentPositionPreceding;

                    if (ReferenceEquals(attr, attr2))
                        return DocumentPositionImplementationSpecific | DocumentPositionFollowing;
                }
            }
        }

        if (node1 is null || node2 is null || !ReferenceEquals(node1.GetRoot(), node2.GetRoot()))
        {
            // Consistent but arbitrary ordering
            var preceding = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(node1 ?? (Node)attr1!) < System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(node2 ?? (Node)attr2!);
            return (ushort)(DocumentPositionDisconnected | DocumentPositionImplementationSpecific | (preceding ? DocumentPositionPreceding : DocumentPositionFollowing));
        }

        if ((node1.IsAncestorOf(node2) && attr1 is null) || (ReferenceEquals(node1, node2) && attr2 is not null))
            return DocumentPositionContains | DocumentPositionPreceding;

        if ((node2.IsAncestorOf(node1) && attr2 is null) || (ReferenceEquals(node1, node2) && attr1 is not null))
            return DocumentPositionContainedBy | DocumentPositionFollowing;

        return node1.IsPreceding(node2) ? DocumentPositionPreceding : DocumentPositionFollowing;
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-node-contains</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-contains"/>
    [IdlMember("contains", "https://dom.spec.whatwg.org/#dom-node-contains")]
    public bool Contains(Node? other) => other is not null && IsInclusiveAncestorOf(other);

    /// <summary>https://dom.spec.whatwg.org/#dom-node-lookupprefix</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-lookupprefix"/>
    [IdlMember("lookupPrefix", "https://dom.spec.whatwg.org/#dom-node-lookupprefix")]
    public string? LookupPrefix(string? @namespace)
    {
        if (string.IsNullOrEmpty(@namespace))
            return null;

        return this switch
        {
            Element element => element.LocateNamespacePrefix(@namespace),
            Document document => document.DocumentElement?.LocateNamespacePrefix(@namespace),
            DocumentType or DocumentFragment => null,
            Attr attr => attr.OwnerElement?.LocateNamespacePrefix(@namespace),
            _ => ParentElement?.LocateNamespacePrefix(@namespace),
        };
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-node-lookupnamespaceuri</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-lookupnamespaceuri"/>
    [IdlMember("lookupNamespaceURI", "https://dom.spec.whatwg.org/#dom-node-lookupnamespaceuri")]
    public string? LookupNamespaceUri(string? prefix)
    {
        if (prefix is { Length: 0 })
        {
            prefix = null;
        }

        return LocateNamespace(prefix);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-node-isdefaultnamespace</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-isdefaultnamespace"/>
    [IdlMember("isDefaultNamespace", "https://dom.spec.whatwg.org/#dom-node-isdefaultnamespace")]
    public bool IsDefaultNamespace(string? @namespace)
    {
        if (@namespace is { Length: 0 })
        {
            @namespace = null;
        }

        return LocateNamespace(prefix: null) == @namespace;
    }

    /// <summary>https://dom.spec.whatwg.org/#locate-a-namespace</summary>
    internal string? LocateNamespace(string? prefix)
    {
        switch (this)
        {
            case Element element:
                return element.LocateNamespaceCore(prefix);
            case Document document:
                return document.DocumentElement?.LocateNamespace(prefix);
            case DocumentType or DocumentFragment:
                return null;
            case Attr attr:
                return attr.OwnerElement?.LocateNamespace(prefix);
            default:
                return ParentElement?.LocateNamespace(prefix);
        }
    }

    /// <summary>
    /// Inserts the node before the given child (or after the last child when it is null) and returns it.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-insertbefore"/>
    [IdlMember("insertBefore", "https://dom.spec.whatwg.org/#dom-node-insertbefore")]
    public Node InsertBefore(Node node, Node? child)
    {
        ArgumentNullException.ThrowIfNull(node);
        return PreInsert(node, child);
    }

    /// <summary>Inserts the node after the last child and returns it.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-appendchild"/>
    [IdlMember("appendChild", "https://dom.spec.whatwg.org/#dom-node-appendchild")]
    public Node AppendChild(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return PreInsert(node, child: null);
    }

    /// <summary>Replaces the child with the node and returns the replaced child.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-replacechild"/>
    [IdlMember("replaceChild", "https://dom.spec.whatwg.org/#dom-node-replacechild")]
    public Node ReplaceChild(Node node, Node child)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(child);
        return Replace(child, node);
    }

    /// <summary>Removes the child and returns it.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-node-removechild"/>
    [IdlMember("removeChild", "https://dom.spec.whatwg.org/#dom-node-removechild")]
    public Node RemoveChild(Node child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return PreRemove(child);
    }

    /// <summary>https://dom.spec.whatwg.org/#get-the-parent: the assigned slot, if any; otherwise the parent.</summary>
    internal override EventTarget? GetTheParent(Event @event) => Slottable?.AssignedSlot ?? (EventTarget?)ParentNode;

    /// <summary>https://dom.spec.whatwg.org/#concept-shadow-including-inclusive-ancestor</summary>
    internal bool IsShadowIncludingInclusiveAncestorOf(Node other)
    {
        for (Node? node = other; node is not null; node = node.ParentNode ?? (node as ShadowRoot)?.Host)
        {
            if (ReferenceEquals(node, this))
                return true;
        }

        return false;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Tree
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://dom.spec.whatwg.org/#concept-tree-root</summary>
    internal Node GetRoot()
    {
        // The root of a connected node of the document tree is its document: the ancestors are not visited (styles, selectors
        // and many algorithms get the root of each element, which would be quadratic in the depth of the tree)
        if (_isConnected && !_isInShadowTree)
            return _nodeDocument;

        var node = this;
        while (node._parent is not null)
        {
            node = node._parent;
        }

        return node;
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-shadow-including-root</summary>
    internal Node GetShadowIncludingRoot()
    {
        var root = GetRoot();
        while (root is ShadowRoot shadowRoot)
        {
            root = shadowRoot.Host.GetRoot();
        }

        return root;
    }

    internal bool IsAncestorOf(Node other)
    {
        for (var node = other._parent; node is not null; node = node._parent)
        {
            if (ReferenceEquals(node, this))
                return true;
        }

        return false;
    }

    internal bool IsInclusiveAncestorOf(Node other) => ReferenceEquals(this, other) || IsAncestorOf(other);

    /// <summary>https://dom.spec.whatwg.org/#concept-tree-host-including-inclusive-ancestor</summary>
    internal bool IsHostIncludingInclusiveAncestorOf(Node other)
    {
        // A node without children is only an inclusive ancestor of itself, and is only the host of a fragment when it is a
        // shadow host or a template. This avoids walking the ancestors of other (quadratic when parsing deep documents).
        if (_firstChild is null && this is not Element { AttachedShadowRoot: not null })
        {
            if (this is not HtmlTemplateElement template)
                return ReferenceEquals(this, other);

            // A template with empty contents is only the host of its contents (e.g. the nested templates of the parser)
            if (template.TemplateContents.FirstChild is null)
                return ReferenceEquals(this, other) || ReferenceEquals(template.TemplateContents, other);
        }

        if (IsInclusiveAncestorOf(other))
            return true;

        var root = other.GetRoot();
        if (root is DocumentFragment { Host: { } host })
            return IsHostIncludingInclusiveAncestorOf(host);

        return false;
    }

    /// <summary>Returns true when this node precedes <paramref name="other"/> in tree order (both must share the same root).</summary>
    internal bool IsPreceding(Node other)
    {
        // Find the ancestors of each node, then compare the children of the common ancestor.
        var thisDepth = GetDepth();
        var otherDepth = other.GetDepth();
        var a = this;
        var b = other;
        for (var depth = thisDepth; depth > otherDepth; depth--)
        {
            a = a._parent!;
        }

        for (var depth = otherDepth; depth > thisDepth; depth--)
        {
            b = b._parent!;
        }

        if (ReferenceEquals(a, b))
        {
            // One node is an ancestor of the other; the ancestor precedes its descendants
            return thisDepth < otherDepth;
        }

        while (!ReferenceEquals(a._parent, b._parent))
        {
            a = a._parent!;
            b = b._parent!;
        }

        for (var sibling = a._nextSibling; sibling is not null; sibling = sibling._nextSibling)
        {
            if (ReferenceEquals(sibling, b))
                return true;
        }

        return false;
    }

    private int GetDepth()
    {
        var depth = 0;
        for (var node = _parent; node is not null; node = node._parent)
        {
            depth++;
        }

        return depth;
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-tree-index</summary>
    internal int GetIndex()
    {
        var index = 0;
        for (var sibling = _previousSibling; sibling is not null; sibling = sibling._previousSibling)
        {
            index++;
        }

        return index;
    }

    /// <summary>Returns the next node in tree order within the subtree rooted at <paramref name="root"/>, or null.</summary>
    internal Node? GetNextInTreeOrder(Node root)
    {
        if (_firstChild is not null)
            return _firstChild;

        return GetNextInTreeOrderSkippingChildren(root);
    }

    /// <summary>Returns the next node in tree order that is not a descendant of this node, within <paramref name="root"/>.</summary>
    internal Node? GetNextInTreeOrderSkippingChildren(Node root)
    {
        var node = this;
        while (!ReferenceEquals(node, root))
        {
            if (node._nextSibling is not null)
                return node._nextSibling;

            node = node._parent;
            if (node is null)
                return null;
        }

        return null;
    }

    /// <summary>Returns the previous node in tree order within the subtree rooted at <paramref name="root"/>, or null.</summary>
    internal Node? GetPreviousInTreeOrder(Node root)
    {
        if (ReferenceEquals(this, root))
            return null;

        var node = _previousSibling;
        if (node is null)
            return _parent;

        while (node._lastChild is not null)
        {
            node = node._lastChild;
        }

        return node;
    }

    /// <summary>Enumerates the descendants of this node in tree order. The enumeration is lazy.</summary>
    [NotInIdl("LINQ helper")]
    public IEnumerable<Node> Descendants()
    {
        for (var node = _firstChild; node is not null; node = node.GetNextInTreeOrder(this))
        {
            yield return node;
        }
    }

    /// <summary>Enumerates the descendants of this node of type <typeparamref name="T"/>, in tree order. The enumeration is lazy.</summary>
    [NotInIdl("LINQ helper")]
    public IEnumerable<T> Descendants<T>() where T : Node
    {
        for (var node = _firstChild; node is not null; node = node.GetNextInTreeOrder(this))
        {
            if (node is T result)
                yield return result;
        }
    }

    /// <summary>Enumerates this node and its descendants in tree order (the inclusive descendants). The enumeration is lazy.</summary>
    [NotInIdl("LINQ helper")]
    public IEnumerable<Node> DescendantsAndSelf()
    {
        for (Node? node = this; node is not null; node = node.GetNextInTreeOrder(this))
        {
            yield return node;
        }
    }

    /// <summary>Enumerates this node and its descendants of type <typeparamref name="T"/>, in tree order. The enumeration is lazy.</summary>
    [NotInIdl("LINQ helper")]
    public IEnumerable<T> DescendantsAndSelf<T>() where T : Node
    {
        for (Node? node = this; node is not null; node = node.GetNextInTreeOrder(this))
        {
            if (node is T result)
                yield return result;
        }
    }

    /// <summary>Enumerates the ancestors of this node, from the parent to the root. The enumeration is lazy and does not cross shadow roots.</summary>
    [NotInIdl("LINQ helper")]
    public IEnumerable<Node> Ancestors()
    {
        for (var node = _parent; node is not null; node = node._parent)
        {
            yield return node;
        }
    }

    /// <summary>Enumerates the ancestors of this node of type <typeparamref name="T"/>, from the parent to the root. The enumeration is lazy and does not cross shadow roots.</summary>
    [NotInIdl("LINQ helper")]
    public IEnumerable<T> Ancestors<T>() where T : Node
    {
        for (var node = _parent; node is not null; node = node._parent)
        {
            if (node is T result)
                yield return result;
        }
    }

    /// <summary>Enumerates this node and its ancestors, from this node to the root (the inclusive ancestors). The enumeration is lazy and does not cross shadow roots.</summary>
    [NotInIdl("LINQ helper")]
    public IEnumerable<Node> AncestorsAndSelf()
    {
        for (Node? node = this; node is not null; node = node._parent)
        {
            yield return node;
        }
    }

    /// <summary>Enumerates this node and its ancestors of type <typeparamref name="T"/>, from this node to the root. The enumeration is lazy and does not cross shadow roots.</summary>
    [NotInIdl("LINQ helper")]
    public IEnumerable<T> AncestorsAndSelf<T>() where T : Node
    {
        for (Node? node = this; node is not null; node = node._parent)
        {
            if (node is T result)
                yield return result;
        }
    }
}

/// <summary>The options of <c>getRootNode()</c>.</summary>
/// <seealso href="https://dom.spec.whatwg.org/#dictdef-getrootnodeoptions"/>
[IdlInterface("GetRootNodeOptions", "https://dom.spec.whatwg.org/#dictdef-getrootnodeoptions")]
public sealed class GetRootNodeOptions
{
    /// <summary>Gets a value indicating whether the shadow-including root is returned instead of the root.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-getrootnodeoptions-composed"/>
    [IdlMember("composed", "https://dom.spec.whatwg.org/#dom-getrootnodeoptions-composed")]
    public bool Composed { get; init; }
}

/// <summary>
/// The children of a node, enumerated through their siblings, or a list of nodes (e.g. the assigned nodes of a slot), without
/// allocation.
/// </summary>
internal readonly struct NodeEnumerable
{
    private readonly Node? _firstChild;
    private readonly List<Node>? _list;

    public NodeEnumerable(Node? firstChild) => _firstChild = firstChild;

    public NodeEnumerable(List<Node> list) => _list = list;

    public Enumerator GetEnumerator() => new(_firstChild, _list);

    public struct Enumerator
    {
        private readonly List<Node>? _list;
        private Node? _next;
        private int _index;

        internal Enumerator(Node? firstChild, List<Node>? list)
        {
            _next = firstChild;
            _list = list;
            Current = null!;
        }

        public Node Current { get; private set; }

        public bool MoveNext()
        {
            if (_list is not null)
            {
                if (_index >= _list.Count)
                    return false;

                Current = _list[_index++];
                return true;
            }

            if (_next is null)
                return false;

            // The next sibling is read before the current node is returned, so the enumeration continues when it is removed
            Current = _next;
            _next = _next.NextSibling;
            return true;
        }
    }
}
