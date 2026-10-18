using System.Text;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#interface-characterdata</summary>
[IdlInterface("CharacterData", "https://dom.spec.whatwg.org/#characterdata")]
public abstract partial class CharacterData : Node
{
    private string _data;

    private protected CharacterData(Document document, string data)
        : base(document)
    {
        _data = data;
    }

    /// <summary>Gets or sets the text data of the node.</summary>
    [IdlMember("data", "https://dom.spec.whatwg.org/#dom-characterdata-data")]
    public string Data
    {
        get => _data;
        set => ReplaceData(0, Length, value ?? "");
    }

    /// <summary>Sets the data without running the "replace data" algorithm (used by the parser to append text).</summary>
    internal void SetDataCore(string value) => _data = value;

    /// <summary>Gets the length of the data, in UTF-16 code units.</summary>
    [IdlMember("length", "https://dom.spec.whatwg.org/#dom-characterdata-length")]
    public uint Length => (uint)_data.Length;

    /// <summary>Gets or sets the text data of the node.</summary>
    public override string? NodeValue
    {
        get => _data;
        set => ReplaceData(0, Length, value ?? "");
    }

    /// <summary>Gets or sets the text data of the node.</summary>
    public override string? TextContent
    {
        get => _data;
        set => ReplaceData(0, Length, value ?? "");
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-cd-substring</summary>
    [IdlMember("substringData", "https://dom.spec.whatwg.org/#dom-characterdata-substringdata")]
    public string SubstringData(uint offset, uint count)
    {
        var length = Length;
        if (offset > length)
            throw DomException.IndexSize("The offset is greater than the length of the data");

        if (offset + (ulong)count > length)
            return _data[(int)offset..];

        return _data.Substring((int)offset, (int)count);
    }

    /// <summary>Appends the given string to the data.</summary>
    [IdlMember("appendData", "https://dom.spec.whatwg.org/#dom-characterdata-appenddata")]
    public void AppendData(string data) => ReplaceData(Length, 0, data);

    /// <summary>Inserts the given string in the data at the given offset.</summary>
    [IdlMember("insertData", "https://dom.spec.whatwg.org/#dom-characterdata-insertdata")]
    public void InsertData(uint offset, string data) => ReplaceData(offset, 0, data);

    /// <summary>Removes the given number of code units from the data, starting at the given offset.</summary>
    [IdlMember("deleteData", "https://dom.spec.whatwg.org/#dom-characterdata-deletedata")]
    public void DeleteData(uint offset, uint count) => ReplaceData(offset, count, "");

    /// <summary>https://dom.spec.whatwg.org/#concept-cd-replace</summary>
    [IdlMember("replaceData", "https://dom.spec.whatwg.org/#dom-characterdata-replacedata")]
    public void ReplaceData(uint offset, uint count, string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var length = Length;
        if (offset > length)
            throw DomException.IndexSize("The offset is greater than the length of the data");

        if (offset + (ulong)count > length)
        {
            count = length - offset;
        }

        QueueMutationRecordIfObserved("characterData", this, null, null, _data);
        _data = string.Concat(_data.AsSpan(0, (int)offset), data, _data.AsSpan((int)(offset + count)));
        var document = NodeDocument;
        if (document.HasLiveRanges)
        {
            document.UpdateLiveRangesForReplaceData(this, offset, count, (uint)data.Length);
        }

        DataChangedSteps();
        document.OnCharacterDataChangedForStyles(this);
        document.EventLoop?.OnOperationCompleted();
    }

    /// <summary>Called after the data changed (e.g. the child text content change steps of the parent).</summary>
    private protected virtual void DataChangedSteps()
    {
        ParentNode?.ChildTextContentChanged();
    }

    private protected override bool IsEqualNodeCore(Node otherNode) => otherNode is CharacterData other && _data == other._data && IsEqualCharacterDataCore(other);

    private protected virtual bool IsEqualCharacterDataCore(CharacterData other) => true;
}

/// <summary>https://dom.spec.whatwg.org/#interface-text</summary>
[IdlInterface("Text", "https://dom.spec.whatwg.org/#text")]
public partial class Text : CharacterData
{
    /// <summary>Creates a text node with the given data.</summary>
    [IdlMember("constructor", "https://dom.spec.whatwg.org/#dom-text-text")]
    public Text(string data = "")
        : this(Document.CurrentGlobalDocument, data)
    {
    }

    internal Text(Document document, string data)
        : base(document, data)
    {
    }

    /// <summary>Gets the type of the node, which is always <c>TEXT_NODE</c>.</summary>
    public override ushort NodeType => TextNode;

    /// <summary>Gets the name of the node, which is always <c>#text</c>.</summary>
    public override string NodeName => "#text";

    /// <summary>Gets the open shadow tree slot the text node is assigned to, or null when it is not assigned.</summary>
    [IdlMember("assignedSlot", "https://dom.spec.whatwg.org/#dom-slotable-assignedslot")]
    public HtmlSlotElement? AssignedSlot => Slots.FindASlot(this, open: true);

    /// <summary>https://dom.spec.whatwg.org/#concept-text-split</summary>
    [IdlMember("splitText", "https://dom.spec.whatwg.org/#dom-text-splittext")]
    public Text SplitText(uint offset)
    {
        var length = Length;
        if (offset > length)
            throw DomException.IndexSize("The offset is greater than the length of the data");

        var count = length - offset;
        var newData = SubstringData(offset, count);
        var newNode = new Text(NodeDocument, newData);
        var parent = ParentNode;
        if (parent is not null)
        {
            parent.Insert(newNode, NextSibling);
            var document = NodeDocument;
            if (document.HasLiveRanges)
            {
                var index = GetIndex();
                foreach (var range in document.GetLiveRangesSnapshot())
                {
                    range.OnSplitText(this, newNode, offset, parent, index);
                }
            }
        }

        ReplaceData(offset, count, "");
        return newNode;
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-text-wholetext</summary>
    [IdlMember("wholeText", "https://dom.spec.whatwg.org/#dom-text-wholetext")]
    public string WholeText
    {
        get
        {
            Node first = this;
            while (first.PreviousSibling is Text previous)
            {
                first = previous;
            }

            var builder = new StringBuilder();
            for (Node? node = first; node is Text text; node = node.NextSibling)
            {
                builder.Append(text.Data);
            }

            return builder.ToString();
        }
    }

    internal override Node CloneSingleNode(Document document) => new Text(document, Data);
}

/// <summary>https://dom.spec.whatwg.org/#interface-cdatasection</summary>
[IdlInterface("CDATASection", "https://dom.spec.whatwg.org/#cdatasection")]
public sealed class CDataSection : Text
{
    internal CDataSection(Document document, string data)
        : base(document, data)
    {
    }

    /// <summary>Gets the type of the node, which is always <c>CDATA_SECTION_NODE</c>.</summary>
    public override ushort NodeType => CDataSectionNode;

    /// <summary>Gets the name of the node, which is always <c>#cdata-section</c>.</summary>
    public override string NodeName => "#cdata-section";

    internal override Node CloneSingleNode(Document document) => new CDataSection(document, Data);
}

/// <summary>https://dom.spec.whatwg.org/#interface-comment</summary>
[IdlInterface("Comment", "https://dom.spec.whatwg.org/#comment")]
public sealed class Comment : CharacterData
{
    /// <summary>Creates a comment node with the given data.</summary>
    [IdlMember("constructor", "https://dom.spec.whatwg.org/#dom-comment-comment")]
    public Comment(string data = "")
        : this(Document.CurrentGlobalDocument, data)
    {
    }

    internal Comment(Document document, string data)
        : base(document, data)
    {
    }

    /// <summary>Gets the type of the node, which is always <c>COMMENT_NODE</c>.</summary>
    public override ushort NodeType => CommentNode;

    /// <summary>Gets the name of the node, which is always <c>#comment</c>.</summary>
    public override string NodeName => "#comment";

    internal override Node CloneSingleNode(Document document) => new Comment(document, Data);

    private protected override void DataChangedSteps()
    {
        // Comments do not affect the child text content of their parent, but replacing the data runs the children changed
        // steps of the parent (https://dom.spec.whatwg.org/#concept-cd-replace), e.g. a style element updates its style block
        ParentNode?.ChildrenChangedSteps(childrenInserted: false);
    }
}
