using System.Buffers.Binary;

namespace Meziantou.Framework.Email.Internals.Ltp;

/// <summary>Builds a heap-on-node ([MS-PST] 2.3.1): allocations in blocks of at most 8176 bytes (8180 in an ANSI file).</summary>
internal sealed class HeapWriter
{
    public const int MaxItemSize = 3580;

    private readonly int _maxBlockSize;
    private readonly List<List<byte[]>> _blocks = [[]];
    private readonly List<int> _usedSizes = [0];

    public HeapWriter(byte clientSignature, int maxBlockSize)
    {
        ClientSignature = clientSignature;
        _maxBlockSize = maxBlockSize;
    }

    public byte ClientSignature { get; }

    public uint UserRoot { get; set; }

    /// <summary>Allocates an item and returns its HID.</summary>
    public uint Allocate(ReadOnlySpan<byte> item)
    {
        if (item.Length > MaxItemSize)
        {
            throw new InvalidOperationException($"A heap item cannot be larger than {MaxItemSize} bytes.");
        }

        var blockIndex = _blocks.Count - 1;
        if (!Fits(blockIndex, item.Length))
        {
            // The HID has 16 bits for the index of the block
            if (blockIndex >= ushort.MaxValue)
            {
                throw new InvalidOperationException("A heap cannot have more than 65,536 blocks.");
            }

            _blocks.Add([]);
            _usedSizes.Add(0);
            blockIndex++;
        }

        var items = _blocks[blockIndex];
        items.Add(item.ToArray());
        _usedSizes[blockIndex] += item.Length;
        return ((uint)blockIndex << 16) | ((uint)items.Count << 5);
    }

    /// <summary>Replaces the content of an item by bytes of the same size.</summary>
    public void Update(uint hid, ReadOnlySpan<byte> item)
    {
        var existing = _blocks[(int)(hid >> 16)][(int)((hid >> 5) & 0x7FF) - 1];
        if (existing.Length != item.Length)
        {
            throw new InvalidOperationException("An item can only be replaced by an item of the same size.");
        }

        item.CopyTo(existing);
    }

    /// <summary>Returns the blocks of the heap, with their headers and page maps.</summary>
    public List<byte[]> Build()
    {
        var result = new List<byte[]>(_blocks.Count);
        var fillLevels = new byte[_blocks.Count];
        for (var i = 0; i < _blocks.Count; i++)
        {
            fillLevels[i] = GetFillLevel(_maxBlockSize - GetBlockSize(i, _blocks[i].Count, _usedSizes[i]));
        }

        for (var i = 0; i < _blocks.Count; i++)
        {
            var items = _blocks[i];
            var headerSize = GetHeaderSize(i);
            var dataEnd = headerSize + _usedSizes[i];
            var pageMapOffset = (dataEnd + 1) & ~1;
            var block = new byte[pageMapOffset + 4 + ((items.Count + 1) * 2)];
            BinaryPrimitives.WriteUInt16LittleEndian(block, (ushort)pageMapOffset);
            if (i == 0)
            {
                block[2] = HeapOnNode.Signature;
                block[3] = ClientSignature;
                BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4), UserRoot);
                WriteFillLevels(block.AsSpan(8, 4), fillLevels, 0, 8);
            }
            else if (headerSize == 66)
            {
                WriteFillLevels(block.AsSpan(2, 64), fillLevels, i, 128);
            }

            var offset = headerSize;
            var pageMap = block.AsSpan(pageMapOffset);
            BinaryPrimitives.WriteUInt16LittleEndian(pageMap, (ushort)items.Count);
            BinaryPrimitives.WriteUInt16LittleEndian(pageMap[2..], 0);
            for (var j = 0; j < items.Count; j++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(pageMap[(4 + (j * 2))..], (ushort)offset);
                items[j].CopyTo(block, offset);
                offset += items[j].Length;
            }

            BinaryPrimitives.WriteUInt16LittleEndian(pageMap[(4 + (items.Count * 2))..], (ushort)offset);
            result.Add(block);
        }

        return result;
    }

    /// <summary>Block 0 starts with the HNHDR, blocks 8, 136, 264... with an HNBITMAPHDR, the others with an HNPAGEHDR.</summary>
    private static int GetHeaderSize(int blockIndex) => blockIndex == 0 ? 12 : (blockIndex - 8) % 128 == 0 && blockIndex >= 8 ? 66 : 2;

    private static int GetBlockSize(int blockIndex, int itemCount, int usedSize) => ((GetHeaderSize(blockIndex) + usedSize + 1) & ~1) + 4 + ((itemCount + 1) * 2);

    private bool Fits(int blockIndex, int itemSize)
    {
        // The HID has 11 bits for the index of the allocation
        var items = _blocks[blockIndex];
        return items.Count < 2047 && GetBlockSize(blockIndex, items.Count + 1, _usedSizes[blockIndex] + itemSize) <= _maxBlockSize;
    }

    private static byte GetFillLevel(int free) => free switch
    {
        >= 3584 => 0x0,
        >= 2560 => 0x1,
        >= 2048 => 0x2,
        >= 1792 => 0x3,
        >= 1536 => 0x4,
        >= 1280 => 0x5,
        >= 1024 => 0x6,
        >= 768 => 0x7,
        >= 512 => 0x8,
        >= 256 => 0x9,
        >= 128 => 0xA,
        >= 64 => 0xB,
        >= 32 => 0xC,
        >= 16 => 0xD,
        >= 8 => 0xE,
        _ => 0xF,
    };

    private static void WriteFillLevels(Span<byte> destination, byte[] levels, int first, int count)
    {
        for (var i = 0; i < count && first + i < levels.Length; i++)
        {
            destination[i / 2] |= (byte)(i % 2 == 0 ? levels[first + i] : levels[first + i] << 4);
        }
    }
}
