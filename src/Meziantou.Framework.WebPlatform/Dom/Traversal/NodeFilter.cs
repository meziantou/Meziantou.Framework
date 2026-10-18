namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#callbackdef-nodefilter</summary>
[IdlInterface("NodeFilter", "https://dom.spec.whatwg.org/#callbackdef-nodefilter")]
public interface INodeFilter
{
    /// <summary>
    /// Returns whether the given node is accepted, rejected or skipped (one of the <c>FILTER_*</c> constants).
    /// </summary>
    [IdlMember("acceptNode", "https://dom.spec.whatwg.org/#dom-nodefilter-acceptnode")]
    ushort AcceptNode(Node node);
}

/// <summary>The constants of the NodeFilter callback interface (https://dom.spec.whatwg.org/#interface-nodefilter).</summary>
[IdlInterface("NodeFilter", "https://dom.spec.whatwg.org/#callbackdef-nodefilter")]
public static class NodeFilter
{
    /// <summary>The node is accepted by the filter.</summary>
    [IdlMember("FILTER_ACCEPT", "https://dom.spec.whatwg.org/#dom-nodefilter-filter_accept")]
    public const ushort FilterAccept = 1;

    /// <summary>The node and its descendants are rejected by the filter.</summary>
    [IdlMember("FILTER_REJECT", "https://dom.spec.whatwg.org/#dom-nodefilter-filter_reject")]
    public const ushort FilterReject = 2;

    /// <summary>The node is skipped by the filter, but its descendants are still considered.</summary>
    [IdlMember("FILTER_SKIP", "https://dom.spec.whatwg.org/#dom-nodefilter-filter_skip")]
    public const ushort FilterSkip = 3;

    /// <summary>Bit mask that shows all the node types.</summary>
    [IdlMember("SHOW_ALL", "https://dom.spec.whatwg.org/#dom-nodefilter-show_all")]
    public const uint ShowAll = 0xFFFFFFFF;

    /// <summary>Bit mask that shows <c>Element</c> nodes.</summary>
    [IdlMember("SHOW_ELEMENT", "https://dom.spec.whatwg.org/#dom-nodefilter-show_element")]
    public const uint ShowElement = 0x1;

    /// <summary>Bit mask that shows <c>Attr</c> nodes.</summary>
    [IdlMember("SHOW_ATTRIBUTE", "https://dom.spec.whatwg.org/#dom-nodefilter-show_attribute")]
    public const uint ShowAttribute = 0x2;

    /// <summary>Bit mask that shows <c>Text</c> nodes.</summary>
    [IdlMember("SHOW_TEXT", "https://dom.spec.whatwg.org/#dom-nodefilter-show_text")]
    public const uint ShowText = 0x4;

    /// <summary>Bit mask that shows <c>CDATASection</c> nodes.</summary>
    [IdlMember("SHOW_CDATA_SECTION", "https://dom.spec.whatwg.org/#dom-nodefilter-show_cdata_section")]
    public const uint ShowCDataSection = 0x8;

    /// <summary>Bit mask for the legacy entity reference node type, which is never present in a tree.</summary>
    [IdlMember("SHOW_ENTITY_REFERENCE", "https://dom.spec.whatwg.org/#dom-nodefilter-show_entity_reference")]
    public const uint ShowEntityReference = 0x10;

    /// <summary>Bit mask for the legacy entity node type, which is never present in a tree.</summary>
    [IdlMember("SHOW_ENTITY", "https://dom.spec.whatwg.org/#dom-nodefilter-show_entity")]
    public const uint ShowEntity = 0x20;

    /// <summary>Bit mask that shows <c>ProcessingInstruction</c> nodes.</summary>
    [IdlMember("SHOW_PROCESSING_INSTRUCTION", "https://dom.spec.whatwg.org/#dom-nodefilter-show_processing_instruction")]
    public const uint ShowProcessingInstruction = 0x40;

    /// <summary>Bit mask that shows <c>Comment</c> nodes.</summary>
    [IdlMember("SHOW_COMMENT", "https://dom.spec.whatwg.org/#dom-nodefilter-show_comment")]
    public const uint ShowComment = 0x80;

    /// <summary>Bit mask that shows <c>Document</c> nodes.</summary>
    [IdlMember("SHOW_DOCUMENT", "https://dom.spec.whatwg.org/#dom-nodefilter-show_document")]
    public const uint ShowDocument = 0x100;

    /// <summary>Bit mask that shows <c>DocumentType</c> nodes.</summary>
    [IdlMember("SHOW_DOCUMENT_TYPE", "https://dom.spec.whatwg.org/#dom-nodefilter-show_document_type")]
    public const uint ShowDocumentType = 0x200;

    /// <summary>Bit mask that shows <c>DocumentFragment</c> nodes.</summary>
    [IdlMember("SHOW_DOCUMENT_FRAGMENT", "https://dom.spec.whatwg.org/#dom-nodefilter-show_document_fragment")]
    public const uint ShowDocumentFragment = 0x400;

    /// <summary>Bit mask for the legacy notation node type, which is never present in a tree.</summary>
    [IdlMember("SHOW_NOTATION", "https://dom.spec.whatwg.org/#dom-nodefilter-show_notation")]
    public const uint ShowNotation = 0x800;
}

/// <summary>State shared by <see cref="NodeIterator"/> and <see cref="TreeWalker"/> (https://dom.spec.whatwg.org/#traversal).</summary>
internal sealed class Traverser(Node root, uint whatToShow, INodeFilter? filter)
{
    private bool _isActive;

    public Node Root { get; } = root;

    public uint WhatToShow { get; } = whatToShow;

    public INodeFilter? Filter { get; } = filter;

    /// <summary>https://dom.spec.whatwg.org/#concept-node-filter</summary>
    public ushort FilterNode(Node node)
    {
        if (_isActive)
            throw DomException.InvalidState("The filter is already running");

        var n = node.NodeType - 1;
        if ((WhatToShow & (1u << n)) == 0)
            return NodeFilter.FilterSkip;

        if (Filter is null)
            return NodeFilter.FilterAccept;

        _isActive = true;
        try
        {
            return Filter.AcceptNode(node);
        }
        finally
        {
            _isActive = false;
        }
    }
}
