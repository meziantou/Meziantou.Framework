using System.Buffers.Binary;
using Meziantou.Framework.Email.Internals.Ndb;

namespace Meziantou.Framework.Email.Internals.Ltp;

/// <summary>A heap-on-node ([MS-PST] 2.3.1): allocations, addressed by HIDs, inside the data blocks of a node.</summary>
internal sealed class HeapOnNode
{
    public const byte Signature = 0xEC;

    private readonly byte[][] _blocks;
    private readonly int[][] _pageMaps;

    public HeapOnNode(NdbNode node)
        : this(node, node.ReadDataBlocks())
    {
    }

    /// <summary>Initializes a heap from the data blocks of its node, already read.</summary>
    public HeapOnNode(NdbNode node, byte[][] blocks)
    {
        Node = node;
        _blocks = blocks;
        if (_blocks.Length == 0)
        {
            throw new InvalidPstException($"The heap of node 0x{node.Nid:X} has no data.");
        }

        var first = _blocks[0];
        if (first.Length < 12 || first[2] != Signature)
        {
            throw new InvalidPstException($"The heap of node 0x{node.Nid:X} has an invalid header.");
        }

        ClientSignature = first[3];
        UserRoot = BinaryPrimitives.ReadUInt32LittleEndian(first.AsSpan(4));
        _pageMaps = new int[_blocks.Length][];
        for (var i = 0; i < _blocks.Length; i++)
        {
            _pageMaps[i] = ReadPageMap(_blocks[i], node.Nid, i);
        }
    }

    public NdbNode Node { get; }

    public byte ClientSignature { get; }

    public uint UserRoot { get; }

    public static bool IsHid(uint hnid) => (hnid & 0x1F) == 0;

    public ReadOnlySpan<byte> GetItem(uint hid)
    {
        if (hid == 0)
        {
            return [];
        }

        if (!IsHid(hid))
        {
            throw new InvalidPstException($"0x{hid:X} is not a heap identifier (node 0x{Node.Nid:X}).");
        }

        var index = (int)((hid >> 5) & 0x7FF);
        var blockIndex = (int)(hid >> 16);
        if (blockIndex >= _blocks.Length)
        {
            throw new InvalidPstException($"The heap identifier 0x{hid:X} refers to block {blockIndex} of node 0x{Node.Nid:X}, which has {_blocks.Length} blocks.");
        }

        var map = _pageMaps[blockIndex];
        if (index < 1 || index >= map.Length)
        {
            throw new InvalidPstException($"The heap identifier 0x{hid:X} refers to allocation {index} of a block that has {map.Length - 1} allocations (node 0x{Node.Nid:X}).");
        }

        var start = map[index - 1];
        var end = map[index];
        return _blocks[blockIndex].AsSpan(start, end - start);
    }

    public bool TryGetItem(uint hid, out ReadOnlySpan<byte> item)
    {
        if (hid == 0 || !IsHid(hid))
        {
            item = default;
            return false;
        }

        item = GetItem(hid);
        return true;
    }

    private static int[] ReadPageMap(byte[] block, uint nid, int blockIndex)
    {
        if (block.Length < 2)
        {
            throw new InvalidPstException($"Block {blockIndex} of the heap of node 0x{nid:X} is too small.");
        }

        int pageMapOffset = BinaryPrimitives.ReadUInt16LittleEndian(block);
        if (pageMapOffset + 4 > block.Length)
        {
            throw new InvalidPstException($"The page map of block {blockIndex} of the heap of node 0x{nid:X} is outside the block.");
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(pageMapOffset));
        if (pageMapOffset + 4 + ((count + 1) * 2) > block.Length)
        {
            throw new InvalidPstException($"The page map of block {blockIndex} of the heap of node 0x{nid:X} is outside the block.");
        }

        var offsets = new int[count + 1];
        var previous = 0;
        for (var i = 0; i <= count; i++)
        {
            int offset = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(pageMapOffset + 4 + (i * 2)));
            if (offset < previous || offset > pageMapOffset)
            {
                throw new InvalidPstException($"The allocation {i} of block {blockIndex} of the heap of node 0x{nid:X} is outside the block.");
            }

            offsets[i] = offset;
            previous = offset;
        }

        return offsets;
    }
}
