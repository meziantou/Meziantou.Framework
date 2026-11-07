namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#interface-documenttype</summary>
/// <seealso href="https://dom.spec.whatwg.org/#documenttype"/>
[IdlInterface("DocumentType", "https://dom.spec.whatwg.org/#documenttype")]
public sealed partial class DocumentType : Node
{
    internal DocumentType(Document document, string name, string publicId, string systemId)
        : base(document)
    {
        Name = name;
        PublicId = publicId;
        SystemId = systemId;
    }

    /// <summary>Gets the node type of a document type, which is <c>10</c> (<c>DOCUMENT_TYPE_NODE</c>).</summary>
    public override ushort NodeType => DocumentTypeNode;

    /// <summary>Gets the node name of a document type, which is its name.</summary>
    public override string NodeName => Name;

    /// <summary>Gets the name of the document type.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-documenttype-name"/>
    [IdlMember("name", "https://dom.spec.whatwg.org/#dom-documenttype-name")]
    public string Name { get; }

    /// <summary>Gets the public identifier of the document type.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-documenttype-publicid"/>
    [IdlMember("publicId", "https://dom.spec.whatwg.org/#dom-documenttype-publicid")]
    public string PublicId { get; }

    /// <summary>Gets the system identifier of the document type.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-documenttype-systemid"/>
    [IdlMember("systemId", "https://dom.spec.whatwg.org/#dom-documenttype-systemid")]
    public string SystemId { get; }

    internal override Node CloneSingleNode(Document document) => new DocumentType(document, Name, PublicId, SystemId);

    private protected override bool IsEqualNodeCore(Node otherNode) =>
        otherNode is DocumentType other && Name == other.Name && PublicId == other.PublicId && SystemId == other.SystemId;
}
