using System.Text;
using Meziantou.Framework.Email.Internals.Ltp;
using Meziantou.Framework.Email.Internals.Ndb;

namespace Meziantou.Framework.Email.Internals;

/// <summary>Where the values of properties of an Outlook data file are: the heap of a property context or a table, and the subnodes of a node.</summary>
internal sealed class PstPropertySource : PropertySource
{
    public PstPropertySource(PstContext context, HeapOnNode heap, NdbNode node)
    {
        Context = context;
        Heap = heap;
        Node = node;
    }

    public PstContext Context { get; }

    public HeapOnNode Heap { get; }

    public NdbNode Node { get; }

    public override Encoding DefaultEncoding => Context.DefaultEncoding;

    public override MapiNamedProperty? GetName(ushort id) => Context.NameToIdMap.GetName(id);

    public override ushort? GetId(MapiNamedProperty name) => Context.NameToIdMap.GetId(name);

    public override byte[] ReadBytes(RawProperty property)
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

    public override long GetSize(RawProperty property)
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

    public override Stream OpenRead(RawProperty property)
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

}
