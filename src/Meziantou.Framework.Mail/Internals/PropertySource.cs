using System.Text;
using Meziantou.Framework.Mail.Internals.Ltp;
using Meziantou.Framework.Mail.Internals.Ndb;

namespace Meziantou.Framework.Mail.Internals;

/// <summary>Where the values of properties are: the heap of a property context or a table, and the subnodes of a node.</summary>
internal sealed class PropertySource
{
    public PropertySource(PstContext context, HeapOnNode heap, NdbNode node)
    {
        Context = context;
        Heap = heap;
        Node = node;
    }

    public PstContext Context { get; }

    public HeapOnNode Heap { get; }

    public NdbNode Node { get; }

    public byte[] ReadBytes(RawProperty property)
    {
        if (property.Inline is not null)
        {
            return property.Inline;
        }

        if (property.Hnid == 0)
        {
            return [];
        }

        if (HeapOnNode.IsHid(property.Hnid))
        {
            return Heap.GetItem(property.Hnid).ToArray();
        }

        return GetSubnode(property.Hnid).ReadData(Context.Options.MaxPropertySize);
    }

    public long GetSize(RawProperty property)
    {
        if (property.Inline is not null)
        {
            return property.Inline.Length;
        }

        if (property.Hnid == 0)
        {
            return 0;
        }

        if (HeapOnNode.IsHid(property.Hnid))
        {
            return Heap.GetItem(property.Hnid).Length;
        }

        return GetSubnode(property.Hnid).GetDataSize();
    }

    public Stream OpenRead(RawProperty property)
    {
        if (property.Inline is null && property.Hnid != 0 && !HeapOnNode.IsHid(property.Hnid))
        {
            return GetSubnode(property.Hnid).OpenData();
        }

        return new MemoryStream(ReadBytes(property), writable: false);
    }

    public NdbNode GetSubnode(uint nid)
    {
        return Node.GetSubnode(nid) ?? throw new InvalidPstException($"The subnode 0x{nid:X} of node 0x{Node.Nid:X} does not exist.");
    }

    public object? Decode(RawProperty property, Encoding string8Encoding)
    {
        if (property.Tag.Type == PstPropertyType.ObjectReference)
        {
            // An object value is a reference to a subnode (the subnode identifier, then the size), not decoded here
            return ReadBytes(property);
        }

        return PropertyValueCodec.Decode(property.Tag.Type, ReadBytes(property), string8Encoding);
    }
}
