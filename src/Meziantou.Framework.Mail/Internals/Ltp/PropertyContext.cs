using System.Buffers.Binary;
using Meziantou.Framework.Mail.Internals.Ndb;

namespace Meziantou.Framework.Mail.Internals.Ltp;

/// <summary>A property context ([MS-PST] 2.3.3): the properties of an object, in a B-tree-on-heap keyed by property identifier.</summary>
internal sealed class PropertyContext
{
    public const byte Signature = 0xBC;

    public PropertyContext(NdbNode node)
        : this(new HeapOnNode(node))
    {
    }

    public PropertyContext(HeapOnNode heap)
    {
        var node = heap.Node;
        Heap = heap;
        if (Heap.ClientSignature != Signature)
        {
            throw new InvalidPstException($"Node 0x{node.Nid:X} is not a property context (signature 0x{Heap.ClientSignature:X2}).");
        }

        var tree = new BTreeOnHeap(Heap, Heap.UserRoot, node.Reader.Options.MaxTreeDepth);
        if (tree.KeySize != 2 || tree.DataSize != 6)
        {
            throw new InvalidPstException($"The property context of node 0x{node.Nid:X} has records of an invalid size.");
        }

        var properties = new List<RawProperty>();
        tree.ForEach((key, data) =>
        {
            var id = BinaryPrimitives.ReadUInt16LittleEndian(key);
            var type = (PstPropertyType)BinaryPrimitives.ReadUInt16LittleEndian(data);
            var value = data[2..6];
            var tag = new PstPropertyTag(id, type);
            properties.Add(PropertyTypes.IsStoredInPropertyContextRecord(type)
                ? RawProperty.FromInline(tag, value[..PropertyTypes.GetFixedSize(type)].ToArray())
                : RawProperty.FromHnid(tag, BinaryPrimitives.ReadUInt32LittleEndian(value)));
        });

        Properties = properties;
    }

    public HeapOnNode Heap { get; }

    public IReadOnlyList<RawProperty> Properties { get; }
}
