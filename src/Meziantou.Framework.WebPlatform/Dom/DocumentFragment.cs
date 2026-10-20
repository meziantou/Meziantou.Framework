namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#interface-documentfragment</summary>
/// <seealso href="https://dom.spec.whatwg.org/#documentfragment"/>
[IdlInterface("DocumentFragment", "https://dom.spec.whatwg.org/#documentfragment")]
public partial class DocumentFragment : Node
{
    /// <summary>Creates an empty document fragment owned by the current global document.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-documentfragment-documentfragment"/>
    [IdlMember("constructor", "https://dom.spec.whatwg.org/#dom-documentfragment-documentfragment")]
    public DocumentFragment()
        : this(Document.CurrentGlobalDocument)
    {
    }

    internal DocumentFragment(Document document)
        : base(document)
    {
    }

    /// <summary>The host of the fragment (https://dom.spec.whatwg.org/#concept-documentfragment-host): the template element or the shadow host.</summary>
    internal Element? Host { get; set; }

    /// <summary>
    /// Gets the node type of a document fragment, which is <c>11</c> (<c>DOCUMENT_FRAGMENT_NODE</c>).
    /// </summary>
    public override ushort NodeType => DocumentFragmentNode;

    /// <summary>Gets the node name of a document fragment, which is <c>#document-fragment</c>.</summary>
    public override string NodeName => "#document-fragment";

    /// <summary>
    /// Gets or sets the concatenation of the text of the descendant text nodes; setting it replaces all the children.
    /// </summary>
    public override string? TextContent
    {
        get => GetDescendantTextContent();
        set => StringReplaceAll(value ?? "");
    }

    internal override Node CloneSingleNode(Document document) => new DocumentFragment(document);

    private protected override bool IsEqualNodeCore(Node otherNode) => otherNode is DocumentFragment;
}
