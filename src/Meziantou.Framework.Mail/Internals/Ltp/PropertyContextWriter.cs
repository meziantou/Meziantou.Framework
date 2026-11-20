using System.Buffers.Binary;
using Meziantou.Framework.Mail.Internals.Ndb;

namespace Meziantou.Framework.Mail.Internals.Ltp;

/// <summary>Builds a property context ([MS-PST] 2.3.3) in the content of a node.</summary>
internal static class PropertyContextWriter
{
    public static void Write(NodeContent node, IEnumerable<WriterProperty> properties, int maxBlockSize)
    {
        var heap = new HeapWriter(PropertyContext.Signature, maxBlockSize);

        // Outlook allocates the header of the B-tree first (HID 0x20): some readers expect it there
        var headerHid = heap.Allocate(new byte[8]);
        var records = new List<(byte[] Key, byte[] Data)>();
        foreach (var property in properties.OrderBy(property => property.Tag.Id))
        {
            var key = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(key, property.Tag.Id);
            var data = new byte[6];
            BinaryPrimitives.WriteUInt16LittleEndian(data, (ushort)property.Tag.Type);
            if (property.Stream is null && PropertyTypes.IsStoredInPropertyContextRecord(property.Tag.Type))
            {
                property.Value.AsSpan(0, Math.Min(4, property.Value?.Length ?? 0)).CopyTo(data.AsSpan(2));
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(2), StoreValue(heap, node, property));
            }

            if (records.Count > 0 && records[^1].Key.AsSpan().SequenceEqual(key))
            {
                throw new InvalidOperationException($"The property 0x{property.Tag.Id:X4} is set twice.");
            }

            records.Add((key, data));
        }

        heap.UserRoot = BTreeOnHeapWriter.Write(heap, records, 2, 6, headerHid);
        node.HeapBlocks = heap.Build();
    }

    /// <summary>Stores a value in the heap when it is small enough, in a subnode otherwise, and returns its HNID.</summary>
    public static uint StoreValue(HeapWriter heap, NodeContent node, WriterProperty property)
    {
        if (property.Stream is not null)
        {
            var nid = node.AllocateSubnodeNid(NidType.Ltp);
            node.Subnodes[nid] = new NodeContent { DataStream = property.Stream };
            return nid;
        }

        var value = property.Value ?? [];
        if (value.Length <= HeapWriter.MaxItemSize)
        {
            return heap.Allocate(value);
        }

        var subnode = node.AllocateSubnodeNid(NidType.Ltp);
        node.Subnodes[subnode] = new NodeContent { Data = value };
        return subnode;
    }
}
