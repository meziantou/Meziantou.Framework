using System.Diagnostics;
using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#interface-element</summary>
/// <seealso href="https://dom.spec.whatwg.org/#element"/>
[IdlInterface("Element", "https://dom.spec.whatwg.org/#element")]
public partial class Element : Node
{
    /// <summary>An attribute of the element. The <see cref="Attr"/> node is only created when requested.</summary>
    private struct AttributeSlot
    {
        public string LocalName;
        public string? Namespace;
        public string? Prefix;
        public string Value;
        public Attr? Node;
    }

    private AttributeSlot[]? _attributes;
    private int _attributeCount;
    private string? _id;

    /// <summary>
    /// The index of the element among the element children of its parent, cached for :nth-child() and :nth-last-child():
    /// it is valid when <see cref="ElementIndexStamp"/> is the <see cref="Node.ChildrenVersion"/> of the parent plus one.
    /// </summary>
    internal int ElementIndex { get; set; }

    /// <summary>The <see cref="Node.ChildrenVersion"/> of the parent plus one when <see cref="ElementIndex"/> was computed, or 0.</summary>
    internal int ElementIndexStamp { get; set; }

    internal Element(Document document, string localName, string? @namespace, string? prefix)
        : base(document)
    {
        LocalName = localName;
        NamespaceUri = @namespace;
        Prefix = prefix;
        TagId = @namespace == Namespaces.Html || @namespace == Namespaces.MathML ? TagNames.GetTagId(localName) : TagId.Unknown;
    }

    /// <summary>The tag id of the local name, for elements in the HTML and MathML namespaces.</summary>
    internal TagId TagId { get; }

    /// <summary>https://dom.spec.whatwg.org/#concept-element-is-value</summary>
    internal string? IsValue
    {
        get => (RareDataOrNull as ElementRareData)?.IsValue;
        set
        {
            if (value is not null || RareDataOrNull is not null)
            {
                ElementRareData.IsValue = value;
            }
        }
    }

    internal ElementRareData ElementRareData => (ElementRareData)RareData;

    private protected override NodeRareData CreateRareData() => new ElementRareData();

    private ShadowRoot? ShadowRootStorage
    {
        get => (RareDataOrNull as ElementRareData)?.ShadowRoot;
        set => ElementRareData.ShadowRoot = value;
    }

    // The custom element state (3 bits) and how the custom element registry is stored (2 bits), packed in a single byte
    // so that the elements do not get bigger
    private byte _customElementData;

    /// <summary>https://dom.spec.whatwg.org/#concept-element-custom-element-state</summary>
    internal CustomElementState CustomElementState
    {
        get => (CustomElementState)(_customElementData & 0b111);
        set => _customElementData = (byte)((_customElementData & ~0b111) | (int)value);
    }

    /// <summary>How the custom element registry of the element is stored (see <see cref="CustomElementRegistry"/>).</summary>
    private ElementRegistryKind RegistryKind
    {
        get => (ElementRegistryKind)(_customElementData >> 3);
        set => _customElementData = (byte)((_customElementData & 0b111) | ((int)value << 3));
    }

    /// <summary>
    /// https://dom.spec.whatwg.org/#element-custom-element-registry. Most elements use the registry of their node document,
    /// which is not stored in each element: only the other registries (scoped registries) are stored in the rare data.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-customelementregistry"/>
    [IdlMember("customElementRegistry", "https://dom.spec.whatwg.org/#dom-element-customelementregistry")]
    public CustomElementRegistry? CustomElementRegistry
    {
        get => RegistryKind switch
        {
            ElementRegistryKind.Document => NodeDocument.CustomElementRegistryStorage,
            ElementRegistryKind.Explicit => ElementRareData.CustomElementRegistry,
            _ => null,
        };
        internal set
        {
            if (value is null)
            {
                RegistryKind = ElementRegistryKind.None;
            }
            else if (ReferenceEquals(value, NodeDocument.CustomElementRegistryStorage))
            {
                RegistryKind = ElementRegistryKind.Document;
            }
            else
            {
                RegistryKind = ElementRegistryKind.Explicit;
            }

            if (RegistryKind == ElementRegistryKind.Explicit || RareDataOrNull is ElementRareData { CustomElementRegistry: not null })
            {
                ElementRareData.CustomElementRegistry = RegistryKind == ElementRegistryKind.Explicit ? value : null;
            }
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-element-custom-element-definition</summary>
    internal CustomElementDefinition? CustomElementDefinition
    {
        get => (RareDataOrNull as ElementRareData)?.CustomElementDefinition;
        set
        {
            if (value is not null || RareDataOrNull is not null)
            {
                ElementRareData.CustomElementDefinition = value;
            }
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-element-custom</summary>
    internal bool IsCustom => CustomElementState == CustomElementState.Custom;

    /// <summary>Gets the node type of an element, which is <c>1</c> (<c>ELEMENT_NODE</c>).</summary>
    public override ushort NodeType => ElementNode;

    /// <summary>Gets the node name of the element, which is its tag name.</summary>
    public override string NodeName => TagName;

    /// <summary>Gets the namespace of the element, or null when it has none.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-namespaceuri"/>
    [IdlMember("namespaceURI", "https://dom.spec.whatwg.org/#dom-element-namespaceuri")]
    public string? NamespaceUri { get; }

    /// <summary>Gets the namespace prefix of the element, or null when it has none.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-prefix"/>
    [IdlMember("prefix", "https://dom.spec.whatwg.org/#dom-element-prefix")]
    public string? Prefix { get; internal set; }

    /// <summary>Gets the local name of the element.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-localname"/>
    [IdlMember("localName", "https://dom.spec.whatwg.org/#dom-element-localname")]
    public string LocalName { get; }

    /// <summary>https://dom.spec.whatwg.org/#element-qualified-name</summary>
    internal string QualifiedName => Prefix is null ? LocalName : Prefix + ":" + LocalName;

    /// <summary>https://dom.spec.whatwg.org/#element-html-uppercased-qualified-name</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-tagname"/>
    [IdlMember("tagName", "https://dom.spec.whatwg.org/#dom-element-tagname")]
    public string TagName
    {
        get
        {
            if (IsHtmlNamespace && NodeDocument.IsHtmlDocument)
            {
                // The uppercased names of the well-known elements are not allocated on each call
                if (Prefix is null && TagId != TagId.Unknown)
                    return TagNames.GetUppercaseName(TagId);

                return InfraAscii.ToAsciiUppercase(QualifiedName);
            }

            return QualifiedName;
        }
    }

    internal bool IsHtmlNamespace
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ReferenceEquals(NamespaceUri, Namespaces.Html);
    }

    /// <summary>Returns whether this element is an HTML element with the given tag id.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsHtml(TagId tagId) => TagId == tagId && IsHtmlNamespace;

    /// <summary>Gets or sets the value of the <c>id</c> attribute.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-id"/>
    [IdlMember("id", "https://dom.spec.whatwg.org/#dom-element-id")]
    public string Id
    {
        get => _id ?? "";
        set => SetAttribute("id", value);
    }

    /// <summary>The ID of the element (https://dom.spec.whatwg.org/#concept-id), or null.</summary>
    internal string? IdValue => _id;

    /// <summary>Gets or sets the <c>elementtiming</c> attribute: the identifier of the element timing entries of the element (https://w3c.github.io/element-timing/#sec-elements-exposed).</summary>
    /// <seealso href="https://w3c.github.io/element-timing/#dom-element-elementtiming"/>
    [IdlMember("elementTiming", "https://w3c.github.io/element-timing/#dom-element-elementtiming")]
    public string ElementTiming
    {
        get => GetAttributeValue("elementtiming") ?? "";
        set => SetAttribute("elementtiming", value);
    }

    /// <summary>Gets or sets the value of the <c>slot</c> attribute.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-slot"/>
    [IdlMember("slot", "https://dom.spec.whatwg.org/#dom-element-slot")]
    public string Slot
    {
        get => GetAttributeValue("slot") ?? "";
        set => SetAttribute("slot", value);
    }

    /// <summary>
    /// Gets the slot the element is assigned to, or null when it is not assigned to an open shadow tree slot.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-slotable-assignedslot"/>
    [IdlMember("assignedSlot", "https://dom.spec.whatwg.org/#dom-slotable-assignedslot")]
    public HtmlSlotElement? AssignedSlot => Slots.FindASlot(this, open: true);

    /// <summary>Gets or sets the value of the <c>class</c> attribute.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-classname"/>
    [IdlMember("className", "https://dom.spec.whatwg.org/#dom-element-classname")]
    public string ClassName
    {
        get => GetAttributeValue("class") ?? "";
        set => SetAttribute("class", value);
    }

    /// <summary>
    /// Gets or sets the concatenation of the text of the descendant text nodes; setting it replaces all the children.
    /// </summary>
    public override string? TextContent
    {
        get => GetDescendantTextContent();
        set => StringReplaceAll(value ?? "");
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Attributes
    // ----------------------------------------------------------------------------------------------------------------

    internal int AttributeCount => _attributeCount;

    internal string GetAttributeLocalName(int index) => _attributes![index].LocalName;

    internal string? GetAttributeNamespace(int index) => _attributes![index].Namespace;

    internal string? GetAttributePrefix(int index) => _attributes![index].Prefix;

    internal string GetAttributeValue(int index) => _attributes![index].Value;

    /// <summary>Returns the qualified name of the attribute at <paramref name="index"/>.</summary>
    internal string GetAttributeQualifiedName(int index)
    {
        ref var slot = ref _attributes![index];
        return slot.Prefix is null ? slot.LocalName : slot.Prefix + ":" + slot.LocalName;
    }

    /// <summary>Returns the value of the attribute with the given local name and no namespace, or null.</summary>
    internal string? GetAttributeValue(string localName)
    {
        var index = FindAttribute(null, localName);
        return index < 0 ? null : _attributes![index].Value;
    }

    /// <summary>Returns the value of the attribute with the given namespace and local name, or null.</summary>
    internal string? GetAttributeValue(string? @namespace, string localName)
    {
        var index = FindAttribute(@namespace, localName);
        return index < 0 ? null : _attributes![index].Value;
    }

    /// <summary>
    /// Returns the animated value of an attribute of an SVG element (https://svgwg.org/specs/animations/#TermAnimatedValue): the
    /// value of the SMIL animations in effect, or the value of the attribute with the given local name and no namespace.
    /// </summary>
    internal string? GetAnimatedAttributeValue(string localName)
    {
        if (NodeDocument.SmilOrNull is { } smil && smil.GetAttributeValue(this, localName) is { } animated)
            return animated;

        return GetAttributeValue(localName);
    }

    /// <summary>Returns the animated value of an attribute for the SVG DOM (<c>animVal</c>): the SMIL animations are sampled first when something changed.</summary>
    internal string? GetSampledAnimatedAttributeValue(string localName)
    {
        if (NodeDocument.SmilOrNull is not { } smil)
            return GetAttributeValue(localName);

        smil.Flush();
        return smil.GetAttributeValue(this, localName) ?? GetAttributeValue(localName);
    }

    internal bool HasAttributeWithLocalName(string localName) => FindAttribute(null, localName) >= 0;

    /// <summary>Finds an attribute by namespace and local name (https://dom.spec.whatwg.org/#concept-element-attributes-get-by-namespace).</summary>
    internal int FindAttribute(string? @namespace, string localName)
    {
        if (@namespace is { Length: 0 })
        {
            @namespace = null;
        }

        var attributes = _attributes;
        for (var i = 0; i < _attributeCount; i++)
        {
            ref var slot = ref attributes![i];
            if (slot.LocalName == localName && slot.Namespace == @namespace)
                return i;
        }

        return -1;
    }

    /// <summary>Finds an attribute by qualified name (https://dom.spec.whatwg.org/#concept-element-attributes-get-by-name).</summary>
    internal int FindAttributeByQualifiedName(string qualifiedName)
    {
        if (IsHtmlNamespace && NodeDocument.IsHtmlDocument)
        {
            qualifiedName = InfraAscii.ToAsciiLowercase(qualifiedName);
        }

        var attributes = _attributes;
        for (var i = 0; i < _attributeCount; i++)
        {
            ref var slot = ref attributes![i];
            if (slot.Prefix is null)
            {
                if (slot.LocalName == qualifiedName)
                    return i;
            }
            else if (qualifiedName.Length == slot.Prefix.Length + 1 + slot.LocalName.Length &&
                qualifiedName.AsSpan().StartsWith(slot.Prefix) &&
                qualifiedName[slot.Prefix.Length] == ':' &&
                qualifiedName.AsSpan(slot.Prefix.Length + 1).SequenceEqual(slot.LocalName))
            {
                return i;
            }
        }

        return -1;
    }

    internal int IndexOfAttributeNode(Attr attr)
    {
        var attributes = _attributes;
        for (var i = 0; i < _attributeCount; i++)
        {
            if (ReferenceEquals(attributes![i].Node, attr))
                return i;
        }

        return -1;
    }

    internal string GetAttributeValueForNode(Attr attr)
    {
        var index = IndexOfAttributeNode(attr);
        Debug.Assert(index >= 0);
        return _attributes![index].Value;
    }

    /// <summary>Returns the Attr node for the attribute at <paramref name="index"/>, creating it when needed.</summary>
    internal Attr GetAttributeNodeAt(int index)
    {
        ref var slot = ref _attributes![index];
        if (slot.Node is null)
        {
            slot.Node = new Attr(NodeDocument, slot.LocalName, slot.Namespace, slot.Prefix, slot.Value) { OwnerElement = this };
        }

        return slot.Node;
    }

    internal IEnumerable<Attr> GetAttributeNodesForComparison()
    {
        for (var i = 0; i < _attributeCount; i++)
        {
            yield return GetAttributeNodeAt(i);
        }
    }

    /// <summary>
    /// Appends an attribute (https://dom.spec.whatwg.org/#concept-element-attributes-append) without checking for duplicates.
    /// Used by the parsers and by <see cref="SetAttributeValue(string, string, string?, string?)"/>.
    /// </summary>
    internal void AppendAttribute(string localName, string? @namespace, string? prefix, string value, Attr? node = null)
    {
        if (_attributes is null)
        {
            _attributes = new AttributeSlot[1];
        }
        else if (_attributeCount == _attributes.Length)
        {
            Array.Resize(ref _attributes, _attributes.Length * 2);
        }

        _attributes[_attributeCount++] = new AttributeSlot { LocalName = localName, Namespace = @namespace, Prefix = prefix, Value = value, Node = node };
        if (node is not null)
        {
            node.OwnerElement = this;
        }

        HandleAttributeChanges(localName, @namespace, oldValue: null, value);
    }

    /// <summary>Allocates the storage of the attributes of an element created by a parser, which knows their number.</summary>
    internal void EnsureAttributeCapacity(int count)
    {
        if (count > 0 && _attributes is null)
        {
            _attributes = new AttributeSlot[count];
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-element-attributes-change</summary>
    internal void ChangeAttribute(Attr attr, string value)
    {
        var index = IndexOfAttributeNode(attr);
        Debug.Assert(index >= 0);
        ChangeAttributeAt(index, value);
    }

    private void ChangeAttributeAt(int index, string value)
    {
        ref var slot = ref _attributes![index];
        var oldValue = slot.Value;
        slot.Value = value;
        HandleAttributeChanges(slot.LocalName, slot.Namespace, oldValue, value);
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-element-attributes-remove</summary>
    internal Attr? RemoveAttributeAt(int index, bool createNode)
    {
        var slot = _attributes![index];
        Array.Copy(_attributes, index + 1, _attributes, index, _attributeCount - index - 1);
        _attributeCount--;
        _attributes[_attributeCount] = default;

        var node = slot.Node;
        if (node is null && createNode)
        {
            node = new Attr(NodeDocument, slot.LocalName, slot.Namespace, slot.Prefix, slot.Value);
        }

        if (node is not null)
        {
            node.DetachedValue = slot.Value;
            node.OwnerElement = null;
        }

        HandleAttributeChanges(slot.LocalName, slot.Namespace, slot.Value, newValue: null);
        return node;
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-element-attributes-remove-by-namespace (with a null namespace).</summary>
    internal void RemoveAttributeByLocalName(string localName)
    {
        var index = FindAttribute(null, localName);
        if (index >= 0)
        {
            RemoveAttributeAt(index, createNode: false);
        }
    }

    /// <summary>Whether the attribute is an event handler content attribute of the element (https://html.spec.whatwg.org/multipage/webappapis.html#event-handler-content-attributes).</summary>
    private bool IsEventHandlerContentAttribute(string localName)
    {
        if (this is not (HtmlElement or SvgElement or MathMLElement))
            return false;

        var type = localName[2..];
        if (Html.EventHandlerNames.Elements.Contains(type))
            return true;

        // https://svgwg.org/specs/animations/#InterfaceSVGAnimationElement
        if (this is SvgAnimationElement && type is "begin" or "end" or "repeat")
            return true;

        return IsHtmlNamespace && TagId is Html.TagId.Body or Html.TagId.Frameset && Html.EventHandlerNames.WindowReflecting.Contains(type);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#event-handler-attributes:concept-element-attributes-change-ext</summary>
    private void OnEventHandlerContentAttributeChanged(string type, string? value)
    {
        var target = GetEventHandlerTarget(type);
        if (target is null)
            return;

        // The inline check of the content security policies: a blocked value does not change the event handler
        if (value is not null && ContentSecurityPolicy.ShouldBlockInline(this, ContentSecurityPolicyInlineType.ScriptAttribute, value))
            return;

        var map = target.GetEventHandlerMap(create: value is not null);
        if (map is null)
            return;

        if (!map.TryGetValue(type, out var handler))
        {
            if (value is null)
                return;

            handler = new EventHandlerAttribute(target, type);
            map[type] = handler;
        }

        if (value is null)
        {
            handler.SetValue(null);
        }
        else
        {
            handler.SetUncompiled(this, value);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#determining-the-target-of-an-event-handler</summary>
    internal override EventTarget? GetEventHandlerTarget(string type)
    {
        if (!IsHtmlNamespace || TagId is not (Html.TagId.Body or Html.TagId.Frameset) || !Html.EventHandlerNames.WindowReflecting.Contains(type))
            return this;

        // The Window-reflecting body element event handlers are the event handlers of the Window of an active document
        return ReferenceEquals(NodeDocument.BrowsingContext?.Document, NodeDocument) ? NodeDocument.RelevantWindow : null;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/dom.html#the-style-attribute: the declarations of a <c>style</c> attribute
    /// whose value is blocked by the inline check of the content security policies are not applied (see
    /// <see cref="ElementRareData.BlockedStyleAttribute"/>). The values set by the CSSOM and by the cloning are not checked.
    /// </summary>
    private void CheckStyleAttribute(string? value)
    {
        var rareData = RareDataOrNull as ElementRareData;
        if (rareData is { SkipStyleAttributeCheck: true })
            return;

        if (value is not null && ContentSecurityPolicy.ShouldBlockInline(this, ContentSecurityPolicyInlineType.StyleAttribute, value))
        {
            ElementRareData.BlockedStyleAttribute = value;
        }
        else if (rareData is not null)
        {
            rareData.BlockedStyleAttribute = null;
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-element-attributes-set-value</summary>
    internal void SetAttributeValue(string localName, string value, string? prefix = null, string? @namespace = null)
    {
        var index = FindAttribute(@namespace, localName);
        if (index < 0)
        {
            AppendAttribute(localName, @namespace, prefix, value);
            return;
        }

        ChangeAttributeAt(index, value);
    }

    /// <summary>https://dom.spec.whatwg.org/#handle-attribute-changes</summary>
    private void HandleAttributeChanges(string localName, string? @namespace, string? oldValue, string? newValue)
    {
        using var scope = CEReactionsScope.Enter();
        QueueMutationRecordIfObserved("attributes", this, localName, @namespace, oldValue);
        if (IsCustom)
        {
            CustomElementReactions.EnqueueCallbackReaction(this, CustomElementCallbackKind.AttributeChanged, [localName, oldValue, newValue, @namespace]);
        }

        AttributeChangeSteps(localName, oldValue, newValue, @namespace);
        if (newValue is not null && @namespace is null && localName == "name" && IsHtmlNamespace && TagId is TagId.Embed or TagId.Form or TagId.Img or TagId.Object)
        {
            NodeDocument.HasNamedObjectElements = true;
        }

        if (@namespace is null && (localName == "name" || localName == "id") && DocumentNamedItemMap.IsCandidate(this))
        {
            DocumentNamedItemMap.OnAttributeChanged(this, localName, oldValue, newValue);
        }

        // https://html.spec.whatwg.org/multipage/links.html#link-type-expect: the link rel=expect elements respond to the
        // changes of the id attributes, and of the name attributes of the a elements, of the elements that are not on the
        // stack of open elements of the parser
        if (@namespace is null && NodeDocument.HasRenderBlockingElements && (localName == "id" || (localName == "name" && IsHtml(TagId.A))) && IsConnected && !NodeDocument.IsOnStackOfOpenElements(this))
        {
            NodeDocument.ProcessInternalResourceLinks(this);
        }

        NodeDocument.OnAttributeChangedForStyles(this, localName, @namespace, oldValue, newValue);
    }

    /// <summary>The attribute change steps (https://dom.spec.whatwg.org/#concept-element-attributes-change-ext).</summary>
    internal virtual void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        if (@namespace is not null)
            return;

        if (RareDataOrNull is ElementRareData rareData && (rareData.ExplicitlySetAttrElements is not null || rareData.ExplicitlySetAttrElementLists is not null))
        {
            Html.Reflection.OnAttributeChanged(this, localName);
        }

        if (localName == "style" && value is not null && !NodeDocument.InlineStylesDeclareAnimations
            && (value.Contains("animation", StringComparison.OrdinalIgnoreCase) || value.Contains("transition", StringComparison.OrdinalIgnoreCase)))
        {
            // The CSS animations and transitions of the document are updated at each frame
            NodeDocument.InlineStylesDeclareAnimations = true;
        }

        if (localName == "style" && value is not null && !NodeDocument.InlineStylesDeclareContentVisibility && (value.Contains("content-visibility", StringComparison.OrdinalIgnoreCase) || value.Contains("contain-intrinsic", StringComparison.OrdinalIgnoreCase)))
        {
            // The document is laid out by the rendering steps (see StyleEngine.MayHaveContentVisibility)
            NodeDocument.InlineStylesDeclareContentVisibility = true;
        }

        if (localName == "style" && value is not null && !NodeDocument.InlineStylesDeclarePositionTryFallbacks && value.Contains("position-try", StringComparison.OrdinalIgnoreCase))
        {
            // The document is laid out by the rendering steps (see StyleEngine.MayHavePositionTryFallbacks)
            NodeDocument.InlineStylesDeclarePositionTryFallbacks = true;
        }

        if (localName == "style" && this is HtmlElement or SvgElement or MathMLElement && (value is not null || RareDataOrNull is ElementRareData { BlockedStyleAttribute: not null }))
        {
            CheckStyleAttribute(value);
        }

        if (localName == "nonce" && this is HtmlElement or SvgElement or MathMLElement)
        {
            // https://html.spec.whatwg.org/multipage/urls-and-fetching.html#nonce-attributes
            CryptographicNonce = value ?? "";
        }

        if (localName.StartsWith("on", StringComparison.Ordinal) && IsEventHandlerContentAttribute(localName))
        {
            OnEventHandlerContentAttributeChanged(localName[2..], value);
        }

        if (localName == "id")
        {
            // https://dom.spec.whatwg.org/#ref-for-concept-element-attributes-change-ext
            var newId = string.IsNullOrEmpty(value) ? null : value;
            if (!string.Equals(_id, newId, StringComparison.Ordinal) && ParentNode is not null && NodeDocument.HasIdMaps && IdMap.GetForRoot(GetRoot()) is { } idMap)
            {
                if (_id is not null)
                {
                    idMap.Remove(_id, this);
                }

                if (newId is not null)
                {
                    idMap.Add(newId, this);
                }
            }

            _id = newId;
            if (IsConnected && NodeDocument.HasFormAttributeCustomElements)
            {
                NodeDocument.OnIdChangedForFormAttributeCustomElements();
            }
        }
        else if (localName == "slot" && Slots.ShouldUpdateName(oldValue, value))
        {
            // https://dom.spec.whatwg.org/#slotable-name
            var data = GetOrCreateSlottable();
            data.Name = value ?? "";
            if (data.AssignedSlot is { } assignedSlot)
            {
                Slots.AssignSlottables(assignedSlot);
            }

            Slots.AssignASlot(this);
        }
    }

    /// <summary>Returns a value indicating whether the element has at least one attribute.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-hasattributes"/>
    [IdlMember("hasAttributes", "https://dom.spec.whatwg.org/#dom-element-hasattributes")]
    public bool HasAttributes() => _attributeCount > 0;

    /// <summary>Returns the qualified names of the attributes of the element.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-getattributenames"/>
    [IdlMember("getAttributeNames", "https://dom.spec.whatwg.org/#dom-element-getattributenames")]
    public IReadOnlyList<string> GetAttributeNames()
    {
        var result = new string[_attributeCount];
        for (var i = 0; i < _attributeCount; i++)
        {
            result[i] = GetAttributeQualifiedName(i);
        }

        return result;
    }

    /// <summary>Returns the value of the attribute with the given qualified name, or null when there is none.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-getattribute"/>
    [IdlMember("getAttribute", "https://dom.spec.whatwg.org/#dom-element-getattribute")]
    public string? GetAttribute(string qualifiedName)
    {
        ArgumentNullException.ThrowIfNull(qualifiedName);
        var index = FindAttributeByQualifiedName(qualifiedName);
        return index < 0 ? null : _attributes![index].Value;
    }

    /// <summary>
    /// Returns the value of the attribute with the given namespace and local name, or null when there is none.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-getattributens"/>
    [IdlMember("getAttributeNS", "https://dom.spec.whatwg.org/#dom-element-getattributens")]
    public string? GetAttributeNS(string? @namespace, string localName)
    {
        ArgumentNullException.ThrowIfNull(localName);
        return GetAttributeValue(@namespace, localName);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-element-setattribute</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced for the attribute and the default policy did not convert the value.</exception>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-setattribute"/>
    [IdlMember("setAttribute", "https://dom.spec.whatwg.org/#dom-element-setattribute")]
    public void SetAttribute(string qualifiedName, string value)
    {
        ArgumentNullException.ThrowIfNull(qualifiedName);
        ArgumentNullException.ThrowIfNull(value);
        SetAttributeCore(qualifiedName, value);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-element-setattribute</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-setattribute"/>
    [IdlMember("setAttribute", "https://dom.spec.whatwg.org/#dom-element-setattribute")]
    public void SetAttribute(string qualifiedName, TrustedType value)
    {
        ArgumentNullException.ThrowIfNull(qualifiedName);
        ArgumentNullException.ThrowIfNull(value);
        SetAttributeCore(qualifiedName, value);
    }

    /// <param name="value">A <see cref="string"/> or a <see cref="TrustedType"/>.</param>
    private void SetAttributeCore(string qualifiedName, object value)
    {
        if (!XmlNames.IsValidAttributeLocalName(qualifiedName))
            throw DomException.InvalidCharacter($"'{qualifiedName}' is not a valid attribute name");

        if (IsHtmlNamespace && NodeDocument.IsHtmlDocument)
        {
            qualifiedName = InfraAscii.ToAsciiLowercase(qualifiedName);
        }

        var verifiedValue = TrustedTypesAlgorithms.GetCompliantAttributeValue(qualifiedName, attributeNs: null, this, value);
        var index = FindAttributeByQualifiedName(qualifiedName);
        if (index < 0)
        {
            AppendAttribute(qualifiedName, @namespace: null, prefix: null, verifiedValue);
            return;
        }

        ChangeAttributeAt(index, verifiedValue);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-element-setattributens</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced for the attribute and the default policy did not convert the value.</exception>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-setattributens"/>
    [IdlMember("setAttributeNS", "https://dom.spec.whatwg.org/#dom-element-setattributens")]
    public void SetAttributeNS(string? @namespace, string qualifiedName, string value)
    {
        ArgumentNullException.ThrowIfNull(qualifiedName);
        ArgumentNullException.ThrowIfNull(value);
        SetAttributeNSCore(@namespace, qualifiedName, value);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-element-setattributens</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-setattributens"/>
    [IdlMember("setAttributeNS", "https://dom.spec.whatwg.org/#dom-element-setattributens")]
    public void SetAttributeNS(string? @namespace, string qualifiedName, TrustedType value)
    {
        ArgumentNullException.ThrowIfNull(qualifiedName);
        ArgumentNullException.ThrowIfNull(value);
        SetAttributeNSCore(@namespace, qualifiedName, value);
    }

    /// <param name="value">A <see cref="string"/> or a <see cref="TrustedType"/>.</param>
    private void SetAttributeNSCore(string? @namespace, string qualifiedName, object value)
    {
        var (ns, prefix, localName) = XmlNames.ValidateAndExtract(@namespace, qualifiedName, isElement: false);
        var verifiedValue = TrustedTypesAlgorithms.GetCompliantAttributeValue(localName, ns, this, value);
        SetAttributeValue(localName, verifiedValue, prefix, ns);
    }

    /// <summary>Removes the attribute with the given qualified name, if any.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-removeattribute"/>
    [IdlMember("removeAttribute", "https://dom.spec.whatwg.org/#dom-element-removeattribute")]
    public void RemoveAttribute(string qualifiedName)
    {
        ArgumentNullException.ThrowIfNull(qualifiedName);
        var index = FindAttributeByQualifiedName(qualifiedName);
        if (index >= 0)
        {
            RemoveAttributeAt(index, createNode: false);
        }
    }

    /// <summary>Removes the attribute with the given namespace and local name, if any.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-removeattributens"/>
    [IdlMember("removeAttributeNS", "https://dom.spec.whatwg.org/#dom-element-removeattributens")]
    public void RemoveAttributeNS(string? @namespace, string localName)
    {
        ArgumentNullException.ThrowIfNull(localName);
        var index = FindAttribute(@namespace, localName);
        if (index >= 0)
        {
            RemoveAttributeAt(index, createNode: false);
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-element-toggleattribute</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-toggleattribute"/>
    [IdlMember("toggleAttribute", "https://dom.spec.whatwg.org/#dom-element-toggleattribute")]
    public bool ToggleAttribute(string qualifiedName, bool? force = null)
    {
        ArgumentNullException.ThrowIfNull(qualifiedName);
        if (!XmlNames.IsValidAttributeLocalName(qualifiedName))
            throw DomException.InvalidCharacter($"'{qualifiedName}' is not a valid attribute name");

        if (IsHtmlNamespace && NodeDocument.IsHtmlDocument)
        {
            qualifiedName = InfraAscii.ToAsciiLowercase(qualifiedName);
        }

        var index = FindAttributeByQualifiedName(qualifiedName);
        if (index < 0)
        {
            if (force is null or true)
            {
                AppendAttribute(qualifiedName, @namespace: null, prefix: null, "");
                return true;
            }

            return false;
        }

        if (force is null or false)
        {
            RemoveAttributeAt(index, createNode: false);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns a value indicating whether the element has an attribute with the given qualified name.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-hasattribute"/>
    [IdlMember("hasAttribute", "https://dom.spec.whatwg.org/#dom-element-hasattribute")]
    public bool HasAttribute(string qualifiedName)
    {
        ArgumentNullException.ThrowIfNull(qualifiedName);
        return FindAttributeByQualifiedName(qualifiedName) >= 0;
    }

    /// <summary>
    /// Returns a value indicating whether the element has an attribute with the given namespace and local name.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-hasattributens"/>
    [IdlMember("hasAttributeNS", "https://dom.spec.whatwg.org/#dom-element-hasattributens")]
    public bool HasAttributeNS(string? @namespace, string localName)
    {
        ArgumentNullException.ThrowIfNull(localName);
        return FindAttribute(@namespace, localName) >= 0;
    }

    /// <summary>Returns the attribute node with the given qualified name, or null when there is none.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-getattributenode"/>
    [IdlMember("getAttributeNode", "https://dom.spec.whatwg.org/#dom-element-getattributenode")]
    public Attr? GetAttributeNode(string qualifiedName)
    {
        ArgumentNullException.ThrowIfNull(qualifiedName);
        var index = FindAttributeByQualifiedName(qualifiedName);
        return index < 0 ? null : GetAttributeNodeAt(index);
    }

    /// <summary>
    /// Returns the attribute node with the given namespace and local name, or null when there is none.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-getattributenodens"/>
    [IdlMember("getAttributeNodeNS", "https://dom.spec.whatwg.org/#dom-element-getattributenodens")]
    public Attr? GetAttributeNodeNS(string? @namespace, string localName)
    {
        ArgumentNullException.ThrowIfNull(localName);
        var index = FindAttribute(@namespace, localName);
        return index < 0 ? null : GetAttributeNodeAt(index);
    }

    /// <summary>Adds the attribute node to the element and returns the attribute node it replaced, or null.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-setattributenode"/>
    [IdlMember("setAttributeNode", "https://dom.spec.whatwg.org/#dom-element-setattributenode")]
    public Attr? SetAttributeNode(Attr attr) => SetAttributeNodeCore(attr);

    /// <summary>Adds the attribute node to the element and returns the attribute node it replaced, or null.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-setattributenodens"/>
    [IdlMember("setAttributeNodeNS", "https://dom.spec.whatwg.org/#dom-element-setattributenodens")]
    public Attr? SetAttributeNodeNS(Attr attr) => SetAttributeNodeCore(attr);

    /// <summary>https://dom.spec.whatwg.org/#concept-element-attributes-set</summary>
    internal Attr? SetAttributeNodeCore(Attr attr)
    {
        ArgumentNullException.ThrowIfNull(attr);
        var verifiedValue = TrustedTypesAlgorithms.GetCompliantAttributeValue(attr.LocalName, attr.NamespaceUri, this, attr.Value);
        if (attr.OwnerElement is not null && !ReferenceEquals(attr.OwnerElement, this))
            throw DomException.InUseAttribute("The attribute is in use by another element");

        var index = FindAttribute(attr.NamespaceUri, attr.LocalName);
        if (index >= 0 && ReferenceEquals(_attributes![index].Node, attr))
            return attr;

        attr.DetachedValue = verifiedValue;

        // Adopt the attribute into the element's node document
        NodeDocument.Adopt(attr);

        if (index >= 0)
        {
            // https://dom.spec.whatwg.org/#concept-element-attributes-replace
            var oldAttr = GetAttributeNodeAt(index);
            ref var slot = ref _attributes![index];
            var oldValue = slot.Value;
            slot.Value = attr.DetachedValue;
            slot.Node = attr;
            slot.Prefix = attr.Prefix;
            attr.OwnerElement = this;
            oldAttr.DetachedValue = oldValue;
            oldAttr.OwnerElement = null;
            HandleAttributeChanges(slot.LocalName, slot.Namespace, oldValue, slot.Value);
            return oldAttr;
        }

        AppendAttribute(attr.LocalName, attr.NamespaceUri, attr.Prefix, attr.DetachedValue, attr);
        return null;
    }

    /// <summary>Removes the attribute node from the element and returns it.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-removeattributenode"/>
    [IdlMember("removeAttributeNode", "https://dom.spec.whatwg.org/#dom-element-removeattributenode")]
    public Attr RemoveAttributeNode(Attr attr)
    {
        ArgumentNullException.ThrowIfNull(attr);
        var index = IndexOfAttributeNode(attr);
        if (index < 0)
            throw DomException.NotFound("The attribute is not an attribute of this element");

        return RemoveAttributeAt(index, createNode: true)!;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Shadow DOM
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The shadow root, including closed ones (https://dom.spec.whatwg.org/#concept-element-shadow-root).</summary>
    internal ShadowRoot? AttachedShadowRoot => ShadowRootStorage;

    /// <summary>Gets the open shadow root of the element, or null when it has none or it is closed.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-shadowroot"/>
    [IdlMember("shadowRoot", "https://dom.spec.whatwg.org/#dom-element-shadowroot")]
    public ShadowRoot? ShadowRoot => ShadowRootStorage is { Mode: ShadowRootMode.Open } ? ShadowRootStorage : null;

    /// <summary>https://dom.spec.whatwg.org/#dom-element-attachshadow</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-element-attachshadow"/>
    [IdlMember("attachShadow", "https://dom.spec.whatwg.org/#dom-element-attachshadow")]
    public ShadowRoot AttachShadow(ShadowRootInit init)
    {
        ArgumentNullException.ThrowIfNull(init);
        var registry = init.HasCustomElementRegistry ? init.CustomElementRegistry : NodeDocument.CustomElementRegistryStorage;
        if (registry is { IsScoped: false } && !ReferenceEquals(registry, NodeDocument.CustomElementRegistryStorage))
            throw DomException.NotSupported("The registry is the global registry of another document");

        return AttachShadowRoot(init.Mode, init.Clonable, init.Serializable, init.DelegatesFocus, init.SlotAssignment, registry);
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-attach-a-shadow-root</summary>
    internal ShadowRoot AttachShadowRoot(ShadowRootMode mode, bool clonable, bool serializable, bool delegatesFocus, SlotAssignmentMode slotAssignment, CustomElementRegistry? registry)
    {
        if (!IsHtmlNamespace)
            throw DomException.NotSupported("Only HTML elements can host a shadow root");

        if (!CustomElements.IsValidShadowHostName(LocalName))
            throw DomException.NotSupported($"'{LocalName}' elements cannot host a shadow root");

        if ((IsValue is not null || CustomElements.IsValidCustomElementName(LocalName)) &&
            Dom.CustomElementRegistry.LookUpDefinition(CustomElementRegistry, NamespaceUri, LocalName, IsValue) is { DisableShadow: true })
        {
            throw DomException.NotSupported("The custom element definition disables the shadow root");
        }

        if (ShadowRootStorage is not null)
        {
            if (!ShadowRootStorage.Declarative || ShadowRootStorage.Mode != mode)
                throw DomException.NotSupported("The element already hosts a shadow root");

            while (ShadowRootStorage.FirstChild is { } child)
            {
                ShadowRootStorage.RemoveChildCore(child, suppressObservers: false);
            }

            ShadowRootStorage.Declarative = false;
            return ShadowRootStorage;
        }

        return AttachShadowCore(mode, clonable, serializable, delegatesFocus, slotAssignment, registry);
    }

    internal ShadowRoot AttachShadowCore(ShadowRootMode mode, bool clonable, bool serializable, bool delegatesFocus, SlotAssignmentMode slotAssignment, CustomElementRegistry? registry)
    {
        var shadow = new ShadowRoot(NodeDocument, this, mode)
        {
            DelegatesFocus = delegatesFocus,
            SlotAssignment = slotAssignment,
            Declarative = false,
            Clonable = clonable,
            Serializable = serializable,
            CustomElementRegistryStorage = registry,
        };

        if (CustomElementState is CustomElementState.Precustomized or CustomElementState.Custom)
        {
            shadow.AvailableToElementInternals = true;
        }

        ShadowRootStorage = shadow;
        NodeDocument.HasShadowRoots = true;
        if (IsConnected)
        {
            shadow.SetConnected(true);
        }

        return shadow;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Namespaces
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://dom.spec.whatwg.org/#locate-a-namespace (Element case)</summary>
    internal string? LocateNamespaceCore(string? prefix)
    {
        if (prefix == "xml")
            return Namespaces.Xml;

        if (prefix == "xmlns")
            return Namespaces.Xmlns;

        for (Element? element = this; element is not null; element = element.ParentElement)
        {
            if (element.NamespaceUri is not null && element.Prefix == prefix)
                return element.NamespaceUri;

            for (var i = 0; i < element._attributeCount; i++)
            {
                ref var slot = ref element._attributes![i];
                if (slot.Namespace != Namespaces.Xmlns)
                    continue;

                if ((slot.Prefix == "xmlns" && slot.LocalName == prefix) || (prefix is null && slot.Prefix is null && slot.LocalName == "xmlns"))
                    return slot.Value.Length == 0 ? null : slot.Value;
            }
        }

        return null;
    }

    /// <summary>https://dom.spec.whatwg.org/#locate-a-namespace-prefix</summary>
    internal string? LocateNamespacePrefix(string @namespace)
    {
        for (Element? element = this; element is not null; element = element.ParentElement)
        {
            if (element.NamespaceUri == @namespace && element.Prefix is not null)
                return element.Prefix;

            for (var i = 0; i < element._attributeCount; i++)
            {
                ref var slot = ref element._attributes![i];
                if (slot.Prefix == "xmlns" && slot.Value == @namespace)
                    return slot.LocalName;
            }
        }

        return null;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Cloning and equality
    // ----------------------------------------------------------------------------------------------------------------

    internal override Node CloneSingleNode(Document document) => CloneElement(document, fallbackRegistry: null);

    /// <summary>https://dom.spec.whatwg.org/#clone-a-single-node (Element case)</summary>
    internal Element CloneElement(Document document, CustomElementRegistry? fallbackRegistry)
    {
        var registry = CustomElementRegistry ?? fallbackRegistry;
        if (Dom.CustomElementRegistry.IsGlobal(registry))
        {
            registry = Dom.CustomElementRegistry.GetEffectiveGlobal(document.CustomElementRegistryStorage);
        }

        var copy = ElementFactory.CreateElement(document, LocalName, NamespaceUri, Prefix, IsValue, synchronousCustomElements: false, registry);

        // As in the browsers, the style attribute of the copy is not checked by the content security policies: its
        // declarations are applied unless they are blocked on the original element
        var copyStyleAttribute = _attributeCount > 0 && (RareDataOrNull is ElementRareData { BlockedStyleAttribute: not null } || ContentSecurityPolicy.GetInlineCheckDocument(this) is not null) && FindAttribute(null, "style") >= 0;
        if (copyStyleAttribute)
        {
            copy.ElementRareData.SkipStyleAttributeCheck = true;
        }

        for (var i = 0; i < _attributeCount; i++)
        {
            ref var slot = ref _attributes![i];
            copy.AppendAttribute(slot.LocalName, slot.Namespace, slot.Prefix, slot.Value);
        }

        if (copyStyleAttribute)
        {
            var copyRareData = copy.ElementRareData;
            copyRareData.SkipStyleAttributeCheck = false;
            var style = GetAttributeValue("style");
            if (RareDataOrNull is ElementRareData { BlockedStyleAttribute: { } blocked } && ReferenceEquals(blocked, style))
            {
                copyRareData.BlockedStyleAttribute = copy.GetAttributeValue("style");
            }
        }

        return copy;
    }

    private protected override bool IsEqualNodeCore(Node otherNode)
    {
        if (otherNode is not Element other)
            return false;

        if (NamespaceUri != other.NamespaceUri || Prefix != other.Prefix || LocalName != other.LocalName || _attributeCount != other._attributeCount)
            return false;

        for (var i = 0; i < _attributeCount; i++)
        {
            ref var slot = ref _attributes![i];
            var otherIndex = other.FindAttribute(slot.Namespace, slot.LocalName);
            if (otherIndex < 0 || other._attributes![otherIndex].Value != slot.Value)
                return false;
        }

        return true;
    }

    internal override void AdoptingSteps(Document oldDocument)
    {
        base.AdoptingSteps(oldDocument);
        for (var i = 0; i < _attributeCount; i++)
        {
            if (_attributes![i].Node is { } attr)
            {
                attr.NodeDocument = NodeDocument;
            }
        }
    }
}

/// <summary>How the custom element registry of an element is stored.</summary>
internal enum ElementRegistryKind : byte
{
    /// <summary>The element has no registry.</summary>
    None,

    /// <summary>The registry is the registry of the node document (which never changes once set).</summary>
    Document,

    /// <summary>The registry is stored in the rare data.</summary>
    Explicit,
}

/// <summary>https://dom.spec.whatwg.org/#concept-element-custom-element-state</summary>
internal enum CustomElementState : byte
{
    Uncustomized,
    Undefined,
    Precustomized,
    Custom,
    Failed,
}
