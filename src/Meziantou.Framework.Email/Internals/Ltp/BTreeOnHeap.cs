using System.Buffers.Binary;

namespace Meziantou.Framework.Email.Internals.Ltp;

/// <summary>A B-tree-on-heap ([MS-PST] 2.3.2): sorted records stored in the allocations of a heap.</summary>
internal sealed class BTreeOnHeap
{
    public const byte Signature = 0xB5;

    private readonly HeapOnNode _heap;

    public BTreeOnHeap(HeapOnNode heap, uint hidHeader, int maxDepth)
    {
        _heap = heap;
        var header = heap.GetItem(hidHeader);
        if (header.Length < 8 || header[0] != Signature)
        {
            throw new InvalidEmailDataException($"The B-tree-on-heap header 0x{hidHeader:X} of node 0x{heap.Node.Nid:X} is invalid.");
        }

        KeySize = header[1];
        DataSize = header[2];
        Levels = header[3];
        Root = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        if (KeySize is not (2 or 4 or 8 or 16) || DataSize is 0 || Levels > maxDepth)
        {
            throw new InvalidEmailDataException($"The B-tree-on-heap header 0x{hidHeader:X} of node 0x{heap.Node.Nid:X} is invalid.");
        }
    }

    public int KeySize { get; }

    public int DataSize { get; }

    public int Levels { get; }

    public uint Root { get; }

    /// <summary>Calls <paramref name="action"/> for each leaf record, in key order.</summary>
    public void ForEach(RecordAction action)
    {
        if (Root == 0)
        {
            return;
        }

        Visit(Root, Levels, action, []);
    }

    public delegate void RecordAction(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data);

    private void Visit(uint hid, int level, RecordAction action, HashSet<uint> visited)
    {
        // In a valid tree, each allocation is referenced once: a repeated reference would make the walk exponential
        if (!visited.Add(hid))
        {
            throw new InvalidEmailDataException($"The allocation 0x{hid:X} is referenced twice in a B-tree-on-heap of node 0x{_heap.Node.Nid:X}.");
        }

        var records = _heap.GetItem(hid);
        if (level == 0)
        {
            var recordSize = KeySize + DataSize;
            if (records.Length % recordSize != 0)
            {
                throw new InvalidEmailDataException($"The leaf 0x{hid:X} of a B-tree-on-heap of node 0x{_heap.Node.Nid:X} has a size that is not a multiple of its records.");
            }

            for (var offset = 0; offset < records.Length; offset += recordSize)
            {
                action(records.Slice(offset, KeySize), records.Slice(offset + KeySize, DataSize));
            }

            return;
        }

        var entrySize = KeySize + 4;
        if (records.Length % entrySize != 0)
        {
            throw new InvalidEmailDataException($"The index 0x{hid:X} of a B-tree-on-heap of node 0x{_heap.Node.Nid:X} has a size that is not a multiple of its records.");
        }

        // Copy the children first: the span of the heap stays valid, but the recursion is easier to read this way
        var children = new uint[records.Length / entrySize];
        for (var i = 0; i < children.Length; i++)
        {
            children[i] = BinaryPrimitives.ReadUInt32LittleEndian(records[((i * entrySize) + KeySize)..]);
        }

        foreach (var child in children)
        {
            Visit(child, level - 1, action, visited);
        }
    }
}
