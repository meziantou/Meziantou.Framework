using System.Buffers.Binary;
using System.IO.Compression;

namespace Meziantou.Framework.Email.Internals.Ndb;

/// <summary>Reads the node database ([MS-PST] 2.2): the header, the node and block B-trees, the blocks and the subnode trees.</summary>
internal sealed class NdbReader : IDisposable
{
    public const byte PageTypeBlockBTree = 0x80;
    public const byte PageTypeNodeBTree = 0x81;
    public const byte BlockTypeData = 0x01;
    public const byte BlockTypeSubnode = 0x02;

    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly long _length;
    private readonly Lock _lock = new();
    private readonly LruCache<ulong, byte[]> _pageCache;
    private readonly LruCache<ulong, byte[]> _blockCache;

    private NdbReader(Stream stream, bool leaveOpen, PstReadOptions options)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        Options = options;
        _length = stream.Length;
        _pageCache = new LruCache<ulong, byte[]>(Math.Max(1, options.CacheSize));
        _blockCache = new LruCache<ulong, byte[]>(Math.Max(1, options.CacheSize));

        var headerData = new byte[Math.Min(564, _length)];
        ReadAt(0, headerData);
        Header = NdbHeader.Read(headerData, options);
    }

    public PstReadOptions Options { get; }

    public NdbHeader Header { get; }

    public NdbLayout Layout => Header.Layout;

    public PstEncryption Encryption => Header.Encryption;

    public long Length => _length;

    public static NdbReader Open(Stream stream, bool leaveOpen, PstReadOptions options)
    {
        if (!stream.CanSeek || !stream.CanRead)
        {
            throw new ArgumentException("The stream must be readable and seekable.", nameof(stream));
        }

        try
        {
            return new NdbReader(stream, leaveOpen, options);
        }
        catch
        {
            if (!leaveOpen)
            {
                stream.Dispose();
            }

            throw;
        }
    }

    public void Dispose()
    {
        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }

    public NdbNode? GetNode(uint nid)
    {
        var entry = FindNodeEntry(nid);
        return entry is null ? null : new NdbNode(this, entry.Value.Nid, entry.Value.BidData, entry.Value.BidSub, entry.Value.NidParent);
    }

    public NbtEntry? FindNodeEntry(uint nid)
    {
        var result = SearchBTree(Header.NodeBTreeRoot, PageTypeNodeBTree, nid);
        return result is null ? null : ParseNodeEntry(result);
    }

    public BbtEntry? FindBlockEntry(ulong bid)
    {
        var result = SearchBTree(Header.BlockBTreeRoot, PageTypeBlockBTree, bid & ~1UL);
        return result is null ? null : ParseBlockEntry(result);
    }

    /// <summary>Enumerates the entries of the node B-tree, in key order.</summary>
    public IEnumerable<NbtEntry> EnumerateNodeEntries()
    {
        foreach (var entry in EnumerateBTreeEntries(Header.NodeBTreeRoot, PageTypeNodeBTree))
        {
            yield return ParseNodeEntry(entry);
        }
    }

    /// <summary>Enumerates the entries of the block B-tree, in key order.</summary>
    public IEnumerable<BbtEntry> EnumerateBlockEntries()
    {
        foreach (var entry in EnumerateBTreeEntries(Header.BlockBTreeRoot, PageTypeBlockBTree))
        {
            yield return ParseBlockEntry(entry);
        }
    }

    public static bool IsInternal(ulong bid) => (bid & 2) != 0;

    /// <summary>Reads a block: checks its trailer, decodes it when it is a data block, and inflates it in a 4K file.</summary>
    public byte[] ReadBlock(ulong bid)
    {
        bid &= ~1UL;
        if (_blockCache.TryGetValue(bid, out var cached))
        {
            return cached;
        }

        var entry = FindBlockEntry(bid) ?? throw new InvalidEmailDataException($"The block 0x{bid:X} does not exist.");
        var layout = Layout;
        var alignedSize = layout.GetAlignedBlockSize(entry.Size);
        if (alignedSize > layout.MaxBlockSize || entry.Ib > (ulong)_length || (ulong)alignedSize > (ulong)_length - entry.Ib)
        {
            throw new InvalidEmailDataException($"The block 0x{bid:X} is outside the file.");
        }

        var raw = new byte[alignedSize];
        ReadAt((long)entry.Ib, raw);
        var trailer = raw.AsSpan(alignedSize - layout.BlockTrailerSize);
        var trailerSize = BinaryPrimitives.ReadUInt16LittleEndian(trailer);
        var signature = BinaryPrimitives.ReadUInt16LittleEndian(trailer[2..]);
        uint crc;
        ulong trailerBid;
        if (layout.IsAnsi)
        {
            trailerBid = BinaryPrimitives.ReadUInt32LittleEndian(trailer[4..]);
            crc = BinaryPrimitives.ReadUInt32LittleEndian(trailer[8..]);
        }
        else
        {
            crc = BinaryPrimitives.ReadUInt32LittleEndian(trailer[4..]);
            trailerBid = BinaryPrimitives.ReadUInt64LittleEndian(trailer[8..]);
        }

        if (trailerSize != entry.Size || (trailerBid & ~1UL) != bid)
        {
            throw new InvalidEmailDataException($"The trailer of the block 0x{bid:X} does not match the block B-tree.");
        }

        if (Options.VerifyChecksums)
        {
            if (PstCrc.Compute(raw.AsSpan(0, entry.Size)) != crc)
            {
                throw new InvalidEmailDataException($"The CRC of the block 0x{bid:X} does not match its content.");
            }

            if (signature != ComputeSignature(entry.Ib, trailerBid))
            {
                throw new InvalidEmailDataException($"The signature of the block 0x{bid:X} does not match its position.");
            }
        }

        var data = raw.AsSpan(0, entry.Size).ToArray();
        if (!IsInternal(bid))
        {
            PstCrypt.Decode(Encryption, data, bid);
        }

        if (layout.Is4K && entry.InflatedSize != entry.Size)
        {
            data = Inflate(data, entry.InflatedSize, bid);
        }

        _blockCache.Add(bid, data);
        return data;
    }

    /// <summary>Returns the blocks of the data of a node: a data block, or the leaves of a data tree (XBLOCK or XXBLOCK).</summary>
    public byte[][] ReadDataBlocks(ulong bid)
    {
        var result = new List<byte[]>();
        long total = 0;
        foreach (var leaf in EnumerateDataLeaves(bid))
        {
            var block = ReadBlock(leaf);
            total += block.Length;
            if (total > Options.MaxPropertySize)
            {
                throw new EmailResourceLimitException($"The data of the block 0x{bid:X} is larger than the limit of {Options.MaxPropertySize} bytes.");
            }

            result.Add(block);
        }

        return [.. result];
    }

    public byte[] ReadData(ulong bid, long maxSize)
    {
        var size = GetDataSize(bid);
        if (size > maxSize)
        {
            throw new EmailResourceLimitException($"The data of the block 0x{bid:X} has {size} bytes, more than the limit of {maxSize} bytes.");
        }

        if (!IsInternal(bid))
        {
            return ReadBlock(bid);
        }

        var result = new byte[size];
        var offset = 0;
        foreach (var leaf in EnumerateDataLeaves(bid))
        {
            var block = ReadBlock(leaf);
            if (block.Length > result.Length - offset)
            {
                throw new InvalidEmailDataException($"The data tree 0x{bid:X} has more data than its declared size.");
            }

            block.CopyTo(result, offset);
            offset += block.Length;
        }

        if (offset != result.Length)
        {
            throw new InvalidEmailDataException($"The data tree 0x{bid:X} has less data than its declared size.");
        }

        return result;
    }

    /// <summary>Returns the size of the data of a node: the size of its block, or the total of its data tree.</summary>
    public long GetDataSize(ulong bid)
    {
        if (!IsInternal(bid))
        {
            var entry = FindBlockEntry(bid) ?? throw new InvalidEmailDataException($"The block 0x{bid:X} does not exist.");
            return Layout.Is4K ? entry.InflatedSize : entry.Size;
        }

        var block = ReadBlock(bid);
        if (block.Length < 8 || block[0] != BlockTypeData)
        {
            throw new InvalidEmailDataException($"The block 0x{bid:X} is not a data tree.");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(4));
    }

    /// <summary>Enumerates the BIDs of the data blocks of a data tree, in order.</summary>
    public IEnumerable<ulong> EnumerateDataLeaves(ulong bid)
    {
        if (!IsInternal(bid))
        {
            return [bid];
        }

        var result = new List<ulong>();
        CollectDataLeaves(bid, expectedLevel: -1, result, []);
        return result;
    }

    private void CollectDataLeaves(ulong bid, int expectedLevel, List<ulong> result, HashSet<ulong> visited)
    {
        if (!visited.Add(bid & ~1UL))
        {
            throw new InvalidEmailDataException($"The data tree block 0x{bid:X} is referenced twice.");
        }

        var block = ReadBlock(bid);
        if (block.Length < 8 || block[0] != BlockTypeData)
        {
            throw new InvalidEmailDataException($"The block 0x{bid:X} is not a data tree.");
        }

        int level = block[1];
        if (level is < 1 or > 2 || (expectedLevel >= 0 && level != expectedLevel))
        {
            throw new InvalidEmailDataException($"The data tree block 0x{bid:X} has an invalid level ({level}).");
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(2));
        var size = Layout.IdSize;
        if (8 + (count * size) > block.Length)
        {
            throw new InvalidEmailDataException($"The data tree block 0x{bid:X} has more entries than it has bytes.");
        }

        for (var i = 0; i < count; i++)
        {
            var child = NdbHeader.ReadId(block.AsSpan(8 + (i * size)), size);
            if (level == 1)
            {
                if (IsInternal(child))
                {
                    throw new InvalidEmailDataException($"The data tree block 0x{bid:X} refers to an internal block as data.");
                }

                result.Add(child);
            }
            else
            {
                CollectDataLeaves(child, 1, result, visited);
            }
        }
    }

    /// <summary>Reads a subnode tree: SLBLOCKs and SIBLOCKs ([MS-PST] 2.2.2.8.3.3).</summary>
    public Dictionary<uint, NdbNode> ReadSubnodes(ulong bidSub, uint parentNid)
    {
        var result = new Dictionary<uint, NdbNode>();
        CollectSubnodes(bidSub, parentNid, expectedLevel: -1, depth: 0, result, []);
        return result;
    }

    private void CollectSubnodes(ulong bid, uint parentNid, int expectedLevel, int depth, Dictionary<uint, NdbNode> result, HashSet<ulong> visited)
    {
        if (depth > Options.MaxTreeDepth)
        {
            throw new EmailResourceLimitException($"The subnode tree of node 0x{parentNid:X} is deeper than the limit of {Options.MaxTreeDepth} levels.");
        }

        if (!visited.Add(bid & ~1UL))
        {
            throw new InvalidEmailDataException($"The subnode block 0x{bid:X} is referenced twice.");
        }

        if (!IsInternal(bid))
        {
            throw new InvalidEmailDataException($"The subnode tree of node 0x{parentNid:X} refers to a data block (0x{bid:X}).");
        }

        var block = ReadBlock(bid);
        var headerSize = Layout.IsAnsi ? 4 : 8;
        if (block.Length < headerSize || block[0] != BlockTypeSubnode)
        {
            throw new InvalidEmailDataException($"The block 0x{bid:X} is not a subnode tree.");
        }

        int level = block[1];
        if (level > 1 || (expectedLevel >= 0 && level != expectedLevel))
        {
            throw new InvalidEmailDataException($"The subnode block 0x{bid:X} has an invalid level ({level}).");
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(2));
        var entrySize = level == 0 ? Layout.SubnodeLeafEntrySize : Layout.SubnodeIntermediateEntrySize;
        if (headerSize + (count * entrySize) > block.Length)
        {
            throw new InvalidEmailDataException($"The subnode block 0x{bid:X} has more entries than it has bytes.");
        }

        var size = Layout.IdSize;
        for (var i = 0; i < count; i++)
        {
            var entry = block.AsSpan(headerSize + (i * entrySize), entrySize);
            var nid = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            if (level == 0)
            {
                var bidData = NdbHeader.ReadId(entry[size..], size);
                var bidSub = NdbHeader.ReadId(entry[(2 * size)..], size);
                result[nid] = new NdbNode(this, nid, bidData, bidSub, parentNid);
            }
            else
            {
                CollectSubnodes(NdbHeader.ReadId(entry[size..], size), parentNid, 0, depth + 1, result, visited);
            }
        }
    }

    public static ushort ComputeSignature(ulong ib, ulong bid)
    {
        ib ^= bid;
        var low = (uint)ib;
        return (ushort)((low >> 16) ^ (low & 0xFFFF));
    }

    private static byte[] Inflate(byte[] data, int inflatedSize, ulong bid)
    {
        try
        {
            var result = new byte[inflatedSize];
            using var input = new MemoryStream(data, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            zlib.ReadExactly(result);
            if (zlib.ReadByte() >= 0)
            {
                throw new InvalidEmailDataException($"The compressed block 0x{bid:X} has more data than its inflated size.");
            }

            return result;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            throw new InvalidEmailDataException($"The compressed block 0x{bid:X} is invalid.", ex);
        }
    }

    /// <summary>Reads a page and checks its trailer.</summary>
    public byte[] ReadPage(Bref bref, byte pageType)
    {
        if (_pageCache.TryGetValue(bref.Ib, out var cached) && cached[Layout.PageDataSize] == pageType && GetPageBid(cached) == bref.Bid)
        {
            return cached;
        }

        var layout = Layout;
        if (bref.Ib > (ulong)_length || (ulong)layout.PageSize > (ulong)_length - bref.Ib)
        {
            throw new InvalidEmailDataException($"The page 0x{bref.Bid:X} is outside the file.");
        }

        var page = new byte[layout.PageSize];
        ReadAt((long)bref.Ib, page);
        var trailer = page.AsSpan(layout.PageDataSize);
        var signature = BinaryPrimitives.ReadUInt16LittleEndian(trailer[2..]);
        uint crc;
        ulong bid;
        if (layout.IsAnsi)
        {
            bid = BinaryPrimitives.ReadUInt32LittleEndian(trailer[4..]);
            crc = BinaryPrimitives.ReadUInt32LittleEndian(trailer[8..]);
        }
        else
        {
            crc = BinaryPrimitives.ReadUInt32LittleEndian(trailer[4..]);
            bid = BinaryPrimitives.ReadUInt64LittleEndian(trailer[8..]);
        }

        if (trailer[0] != pageType || trailer[1] != pageType)
        {
            throw new InvalidEmailDataException($"The page 0x{bref.Bid:X} has the type 0x{trailer[0]:X2} instead of 0x{pageType:X2}.");
        }

        if (bid != bref.Bid)
        {
            throw new InvalidEmailDataException($"The page at offset 0x{bref.Ib:X} has the identifier 0x{bid:X} instead of 0x{bref.Bid:X}.");
        }

        if (Options.VerifyChecksums)
        {
            if (PstCrc.Compute(page.AsSpan(0, layout.PageDataSize)) != crc)
            {
                throw new InvalidEmailDataException($"The CRC of the page 0x{bref.Bid:X} does not match its content.");
            }

            if (signature != ComputeSignature(bref.Ib, bref.Bid))
            {
                throw new InvalidEmailDataException($"The signature of the page 0x{bref.Bid:X} does not match its position.");
            }
        }

        _pageCache.Add(bref.Ib, page);
        return page;
    }

    private ulong GetPageBid(byte[] page)
    {
        var trailer = page.AsSpan(Layout.PageDataSize);
        return Layout.IsAnsi ? BinaryPrimitives.ReadUInt32LittleEndian(trailer[4..]) : BinaryPrimitives.ReadUInt64LittleEndian(trailer[8..]);
    }

    private BTreePageInfo ReadBTreePage(Bref bref, byte pageType, int expectedLevel)
    {
        var page = ReadPage(bref, pageType);
        var layout = Layout;
        int count;
        int entrySize;
        int level;
        var metadata = page.AsSpan(layout.BTreeEntriesSize);
        if (layout.Is4K)
        {
            count = BinaryPrimitives.ReadUInt16LittleEndian(metadata);
            entrySize = metadata[4];
            level = metadata[5];
        }
        else
        {
            count = metadata[0];
            entrySize = metadata[2];
            level = metadata[3];
        }

        if (expectedLevel >= 0 && level != expectedLevel)
        {
            throw new InvalidEmailDataException($"The B-tree page 0x{bref.Bid:X} has the level {level} instead of {expectedLevel}.");
        }

        if (level > Options.MaxTreeDepth)
        {
            throw new EmailResourceLimitException($"The B-tree is deeper than the limit of {Options.MaxTreeDepth} levels.");
        }

        var expectedEntrySize = level > 0 ? layout.BTreeEntrySize : pageType == PageTypeNodeBTree ? layout.NodeBTreeEntrySize : layout.BlockBTreeEntrySize;
        if (entrySize < expectedEntrySize || count * entrySize > layout.BTreeEntriesSize)
        {
            throw new InvalidEmailDataException($"The B-tree page 0x{bref.Bid:X} has invalid entries.");
        }

        return new BTreePageInfo(page, count, entrySize, level);
    }

    private byte[]? SearchBTree(Bref root, byte pageType, ulong key)
    {
        var bref = root;
        var expectedLevel = -1;
        var size = Layout.IdSize;
        for (var depth = 0; ; depth++)
        {
            if (depth > Options.MaxTreeDepth)
            {
                throw new EmailResourceLimitException($"The B-tree is deeper than the limit of {Options.MaxTreeDepth} levels.");
            }

            var info = ReadBTreePage(bref, pageType, expectedLevel);
            if (info.Level == 0)
            {
                for (var i = 0; i < info.Count; i++)
                {
                    var entry = info.GetEntry(i);
                    var entryKey = pageType == PageTypeNodeBTree ? BinaryPrimitives.ReadUInt32LittleEndian(entry) : NdbHeader.ReadId(entry, size) & ~1UL;
                    if (entryKey == key)
                    {
                        return entry.ToArray();
                    }
                }

                return null;
            }

            // Descend into the last child whose first key is less than or equal to the key
            var found = -1;
            for (var i = 0; i < info.Count; i++)
            {
                var entryKey = NdbHeader.ReadId(info.GetEntry(i), size);
                if (pageType == PageTypeBlockBTree)
                {
                    entryKey &= ~1UL;
                }

                if (entryKey > key)
                {
                    break;
                }

                found = i;
            }

            if (found < 0)
            {
                return null;
            }

            var selected = info.GetEntry(found);
            bref = new Bref(NdbHeader.ReadId(selected[size..], size), NdbHeader.ReadId(selected[(2 * size)..], size));
            expectedLevel = info.Level - 1;
        }
    }

    /// <summary>Enumerates the leaf entries of a B-tree in key order, and calls <paramref name="onPage"/> for each page.</summary>
    public IEnumerable<byte[]> EnumerateBTreeEntries(Bref root, byte pageType, Action<Bref>? onPage = null)
    {
        var visited = new HashSet<ulong>();
        var stack = new Stack<(Bref Bref, int Level)>();
        stack.Push((root, -1));
        var size = Layout.IdSize;
        while (stack.Count > 0)
        {
            var (bref, level) = stack.Pop();
            if (!visited.Add(bref.Ib))
            {
                throw new InvalidEmailDataException($"The B-tree page 0x{bref.Bid:X} is referenced twice.");
            }

            var info = ReadBTreePage(bref, pageType, level);
            onPage?.Invoke(bref);
            if (info.Level == 0)
            {
                for (var i = 0; i < info.Count; i++)
                {
                    yield return info.GetEntry(i).ToArray();
                }

                continue;
            }

            for (var i = info.Count - 1; i >= 0; i--)
            {
                var entry = info.GetEntry(i);
                stack.Push((new Bref(NdbHeader.ReadId(entry[size..], size), NdbHeader.ReadId(entry[(2 * size)..], size)), info.Level - 1));
            }
        }
    }

    private NbtEntry ParseNodeEntry(ReadOnlySpan<byte> entry)
    {
        var size = Layout.IdSize;
        var nid = BinaryPrimitives.ReadUInt32LittleEndian(entry);
        var bidData = NdbHeader.ReadId(entry[size..], size);
        var bidSub = NdbHeader.ReadId(entry[(2 * size)..], size);
        var nidParent = BinaryPrimitives.ReadUInt32LittleEndian(entry[(3 * size)..]);
        return new NbtEntry(nid, bidData, bidSub, nidParent);
    }

    private BbtEntry ParseBlockEntry(ReadOnlySpan<byte> entry)
    {
        var size = Layout.IdSize;
        var bid = NdbHeader.ReadId(entry, size);
        var ib = NdbHeader.ReadId(entry[size..], size);
        var cb = BinaryPrimitives.ReadUInt16LittleEndian(entry[(2 * size)..]);
        if (Layout.Is4K)
        {
            var inflated = BinaryPrimitives.ReadUInt16LittleEndian(entry[((2 * size) + 2)..]);
            var references = BinaryPrimitives.ReadUInt16LittleEndian(entry[((2 * size) + 4)..]);
            return new BbtEntry(bid, ib, cb, inflated == 0 ? cb : inflated, references);
        }

        return new BbtEntry(bid, ib, cb, cb, BinaryPrimitives.ReadUInt16LittleEndian(entry[((2 * size) + 2)..]));
    }

    public void ReadAt(long offset, Span<byte> buffer)
    {
        lock (_lock)
        {
            if (offset < 0 || offset > _length - buffer.Length)
            {
                throw new InvalidEmailDataException($"The structure at offset 0x{offset:X} is outside the file.");
            }

            _stream.Position = offset;
            _stream.ReadExactly(buffer);
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct BTreePageInfo(byte[] Page, int Count, int EntrySize, int Level)
    {
        public ReadOnlySpan<byte> GetEntry(int index) => Page.AsSpan(index * EntrySize, EntrySize);
    }
}
