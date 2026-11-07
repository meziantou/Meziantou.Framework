namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#interface-attr</summary>
/// <seealso href="https://dom.spec.whatwg.org/#attr"/>
[IdlInterface("Attr", "https://dom.spec.whatwg.org/#attr")]
public sealed partial class Attr : Node
{
    // The value is stored in the element's attribute list while the attribute is attached to an element
    private string _value;

    internal Attr(Document document, string localName, string? @namespace, string? prefix, string value)
        : base(document)
    {
        LocalName = localName;
        NamespaceUri = @namespace;
        Prefix = prefix;
        _value = value;
    }

    /// <summary>Gets the type of the node, which is always <c>ATTRIBUTE_NODE</c>.</summary>
    public override ushort NodeType => AttributeNode;

    /// <summary>Gets the qualified name of the attribute.</summary>
    public override string NodeName => Name;

    /// <summary>Gets the namespace of the attribute, or null when it has none.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-attr-namespaceuri"/>
    [IdlMember("namespaceURI", "https://dom.spec.whatwg.org/#dom-attr-namespaceuri")]
    public string? NamespaceUri { get; }

    /// <summary>Gets the namespace prefix of the attribute, or null when it has none.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-attr-prefix"/>
    [IdlMember("prefix", "https://dom.spec.whatwg.org/#dom-attr-prefix")]
    public string? Prefix { get; }

    /// <summary>Gets the local name of the attribute.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-attr-localname"/>
    [IdlMember("localName", "https://dom.spec.whatwg.org/#dom-attr-localname")]
    public string LocalName { get; }

    /// <summary>The qualified name (https://dom.spec.whatwg.org/#concept-attribute-qualified-name).</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-attr-name"/>
    [IdlMember("name", "https://dom.spec.whatwg.org/#dom-attr-name")]
    public string Name => Prefix is null ? LocalName : Prefix + ":" + LocalName;

    /// <summary>Gets or sets the value of the attribute.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-attr-value"/>
    [IdlMember("value", "https://dom.spec.whatwg.org/#dom-attr-value")]
    public string Value
    {
        get => OwnerElement is { } element ? element.GetAttributeValueForNode(this) : _value;
        set => SetExistingAttributeValue(value ?? "");
    }

    /// <summary>Gets or sets the value of the attribute.</summary>
    public override string? NodeValue
    {
        get => Value;
        set => SetExistingAttributeValue(value ?? "");
    }

    /// <summary>Gets or sets the value of the attribute.</summary>
    public override string? TextContent
    {
        get => Value;
        set => SetExistingAttributeValue(value ?? "");
    }

    /// <summary>Gets the element that owns the attribute, or null when the attribute is detached.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-attr-ownerelement"/>
    [IdlMember("ownerElement", "https://dom.spec.whatwg.org/#dom-attr-ownerelement")]
    public Element? OwnerElement { get; internal set; }

    /// <summary>Always true (https://dom.spec.whatwg.org/#dom-attr-specified).</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-attr-specified"/>
    [IdlMember("specified", "https://dom.spec.whatwg.org/#dom-attr-specified")]
    public bool Specified => true;

    /// <summary>The value stored in the node itself, used when the attribute is detached from its element.</summary>
    internal string DetachedValue
    {
        get => _value;
        set => _value = value;
    }

    /// <summary>https://dom.spec.whatwg.org/#set-an-existing-attribute-value</summary>
    private void SetExistingAttributeValue(string value)
    {
        if (OwnerElement is not { } element)
        {
            _value = value;
            return;
        }

        var verifiedValue = TrustedTypesAlgorithms.GetCompliantAttributeValue(LocalName, NamespaceUri, element, value);

        // The default policy may have removed the attribute from its element
        if (OwnerElement is null)
        {
            _value = verifiedValue;
            return;
        }

        OwnerElement.ChangeAttribute(this, verifiedValue);
    }

    internal override Node CloneSingleNode(Document document) => new Attr(document, LocalName, NamespaceUri, Prefix, Value);

    private protected override bool IsEqualNodeCore(Node otherNode) =>
        otherNode is Attr other && NamespaceUri == other.NamespaceUri && LocalName == other.LocalName && Value == other.Value;
}
