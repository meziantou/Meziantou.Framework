using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;

namespace Meziantou.Framework.Email.Internals.Ndb;

/// <summary>Writes a new node database: blocks, data trees, subnode trees, the node and block B-trees, the allocation maps and the header ([MS-PST] 2.2 and 2.6.1).</summary>
/// <remarks>
/// The blocks are written sequentially as the nodes are added. The B-trees, the allocation maps, the density list and the header
/// are written by <see cref="Complete"/>. The layout of the allocation maps follows [MS-PST] 2.2.2.7 and the reference implementation
/// of Microsoft (outlook-pst-rs) for the positions [MS-PST] does not give (PMaps, FMaps, FPMaps).
/// </remarks>
internal sealed class NdbWriter
{
    private const ulong FirstAMapOffset = 0x4400;
    private const ulong FirstAMapOffset4K = 0x22000;
    private const ulong DListOffset = 0x4200;
    private const ulong DListOffset4K = 0x21000;
    private const int FMapFirstAMap = 128;
    private const int FMapInterval = 496;
    private const int FPMapFirstAMap = 128 * 64;
    private const int FPMapInterval = 496 * 64;

    private readonly Stream _stream;
    private readonly List<NbtEntry> _nodes = [];
    private readonly List<BbtEntry> _blocks = [];
    private readonly List<bool> _allocated = [];
    private readonly uint[] _nextNids = new uint[32];
    private ulong _nextBid = 1;
    private ulong _nextPageBid = 1;
    private ulong _position;

    public NdbWriter(Stream stream, PstFormat format, PstEncryption encryption)
    {
        _stream = stream;
        Layout = NdbLayout.Get(format);
        Encryption = encryption;
        for (var i = 0; i < _nextNids.Length; i++)
        {
            _nextNids[i] = 0x400;
        }

        _nextNids[(int)NidType.SearchFolder] = 0x4000;
        _nextNids[(int)NidType.NormalMessage] = 0x10000;
        _nextNids[(int)NidType.AssociatedMessage] = 0x8000;

        // The data starts after the first AMap and the pages that follow it
        _position = FirstAMap;
        _position = SkipMapPages(_position, (ulong)Layout.PageSize);
    }

    public NdbLayout Layout { get; }

    public PstEncryption Encryption { get; }

    /// <summary>The value of dwUnique in the header ([MS-PST] 2.2.2.6), which changes with each modification of the file.</summary>
    public uint Unique { get; set; } = 1;

    /// <summary>The size of the space an AMap maps: 64 bytes per bit (512 bytes in a 4K file).</summary>
    private ulong AMapCoverage => (ulong)AMapBitsSize * 8 * SlotSize;

    private ulong SlotSize => Layout.Is4K ? 512u : 64u;

    private int AMapBitsSize => Layout.Is4K ? Layout.PageDataSize : 496;

    private int AMapBitsOffset => Layout.IsAnsi ? 4 : 0;

    private ulong FirstAMap => Layout.Is4K ? FirstAMapOffset4K : FirstAMapOffset;

    private ulong MaxFileSize => Layout.IsAnsi ? int.MaxValue : (ulong)long.MaxValue;

    /// <summary>Allocates the identifier of a new top-level node of a type.</summary>
    public uint AllocateNid(NidType type)
    {
        var index = ++_nextNids[(int)type];
        return Nids.Make(type, index);
    }

    /// <summary>Records a node allocated with a specific identifier (the special nodes), so that the header counters stay above it.</summary>
    public void ReserveNid(uint nid)
    {
        var type = (int)(nid & 0x1F);
        _nextNids[type] = Math.Max(_nextNids[type], Nids.GetIndex(nid));
    }

    public void AddNode(uint nid, ulong bidData, ulong bidSub, uint nidParent)
    {
        _nodes.Add(new NbtEntry(nid, bidData, bidSub, nidParent));
    }

    /// <summary>Writes the data of a node: a single block, or a data tree when it is larger than a block.</summary>
    public ulong WriteData(ReadOnlySpan<byte> data)
    {
        var max = Layout.MaxLeafDataSize;
        if (data.Length <= max)
        {
            return WriteBlock(data, isInternal: false);
        }

        var leaves = new List<(ulong Bid, int Size)>();
        for (var offset = 0; offset < data.Length; offset += max)
        {
            var length = Math.Min(max, data.Length - offset);
            leaves.Add((WriteBlock(data.Slice(offset, length), isInternal: false), length));
        }

        return WriteDataTree(leaves);
    }

    /// <summary>Writes the data of a node from a stream, block by block.</summary>
    public ulong WriteData(Stream data)
    {
        var max = Layout.MaxLeafDataSize;
        var buffer = ArrayPool<byte>.Shared.Rent(max);
        try
        {
            var leaves = new List<(ulong Bid, int Size)>();
            long total = 0;
            while (true)
            {
                var read = ReadBlock(data, buffer.AsSpan(0, max));
                if (read == 0 && leaves.Count > 0)
                {
                    break;
                }

                leaves.Add((WriteBlock(buffer.AsSpan(0, read), isInternal: false), read));
                total += read;
                if (read < max)
                {
                    break;
                }
            }

            if (leaves.Count == 1)
            {
                return leaves[0].Bid;
            }

            if (total > uint.MaxValue)
            {
                throw new InvalidOperationException("The data of a node cannot be larger than 4 GB.");
            }

            return WriteDataTree(leaves);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Writes the blocks of a heap: each one is a data block of the data tree of the node.</summary>
    public ulong WriteDataBlocks(IReadOnlyList<byte[]> blocks)
    {
        if (blocks.Count == 1)
        {
            return WriteBlock(blocks[0], isInternal: false);
        }

        var leaves = new List<(ulong Bid, int Size)>(blocks.Count);
        foreach (var block in blocks)
        {
            leaves.Add((WriteBlock(block, isInternal: false), block.Length));
        }

        return WriteDataTree(leaves);
    }

    /// <summary>Writes the subnode tree of a node: SLBLOCKs, under an SIBLOCK when they do not fit in one block.</summary>
    public ulong WriteSubnodes(IEnumerable<(uint Nid, ulong BidData, ulong BidSub)> entries)
    {
        var sorted = entries.OrderBy(entry => entry.Nid).ToList();
        if (sorted.Count == 0)
        {
            return 0;
        }

        var headerSize = Layout.IsAnsi ? 4 : 8;
        var size = Layout.IdSize;
                var leafCapacity = (Layout.MaxLeafDataSize - headerSize) / Layout.SubnodeLeafEntrySize;
        var leaves = new List<(uint FirstNid, ulong Bid)>();
        for (var start = 0; start < sorted.Count; start += leafCapacity)
        {
            var count = Math.Min(leafCapacity, sorted.Count - start);
            var block = new byte[headerSize + (count * Layout.SubnodeLeafEntrySize)];
            block[0] = NdbReader.BlockTypeSubnode;
            block[1] = 0;
            BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(2), (ushort)count);
            for (var i = 0; i < count; i++)
            {
                var entry = sorted[start + i];
                var span = block.AsSpan(headerSize + (i * Layout.SubnodeLeafEntrySize));
                BinaryPrimitives.WriteUInt32LittleEndian(span, entry.Nid);
                WriteId(span[size..], entry.BidData);
                WriteId(span[(2 * size)..], entry.BidSub);
            }

            leaves.Add((sorted[start].Nid, WriteBlock(block, isInternal: true)));
        }

        if (leaves.Count == 1)
        {
            return leaves[0].Bid;
        }

        var intermediateCapacity = (Layout.MaxLeafDataSize - headerSize) / Layout.SubnodeIntermediateEntrySize;
        if (leaves.Count > intermediateCapacity)
        {
            throw new InvalidOperationException("A node cannot have that many subnodes.");
        }

        var index = new byte[headerSize + (leaves.Count * Layout.SubnodeIntermediateEntrySize)];
        index[0] = NdbReader.BlockTypeSubnode;
        index[1] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(index.AsSpan(2), (ushort)leaves.Count);
        for (var i = 0; i < leaves.Count; i++)
        {
            var span = index.AsSpan(headerSize + (i * Layout.SubnodeIntermediateEntrySize));
            BinaryPrimitives.WriteUInt32LittleEndian(span, leaves[i].FirstNid);
            WriteId(span[size..], leaves[i].Bid);
        }

        return WriteBlock(index, isInternal: true);
    }

    /// <summary>Writes the B-trees, the allocation maps, the density list and the header.</summary>
    public void Complete()
    {
        var nodeRoot = WriteBTree(NdbReader.PageTypeNodeBTree, [.. _nodes.OrderBy(node => node.Nid).Select(EncodeNodeEntry)], Layout.NodeBTreeEntrySize, _nodes.OrderBy(node => node.Nid).Select(node => (ulong)node.Nid).ToList());
        var blocks = _blocks.OrderBy(block => block.Bid).ToList();
        var blockRoot = WriteBTree(NdbReader.PageTypeBlockBTree, [.. blocks.Select(EncodeBlockEntry)], Layout.BlockBTreeEntrySize, blocks.Select(block => block.Bid).ToList());

        // The file grows by whole AMaps
        var amapCount = (int)Math.Max(1, (_position - FirstAMap + AMapCoverage - 1) / AMapCoverage);
        var fileEof = FirstAMap + ((ulong)amapCount * AMapCoverage);
        if (fileEof > MaxFileSize)
        {
            throw new InvalidOperationException("The content is too large for the format of the file (2 GB for an ANSI file).");
        }

        // Reserve the pages of the AMaps that follow the data, so that the bitmaps count them
        for (var i = 0; i < amapCount; i++)
        {
            foreach (var (offset, _) in GetMapPages(i))
            {
                MarkAllocated(offset, (ulong)Layout.PageSize);
            }
        }

        _stream.SetLength((long)fileEof);
        ulong totalFree = 0;
        var freeSlots = new int[amapCount];
        var maxFreeRuns = new byte[amapCount];
        for (var i = 0; i < amapCount; i++)
        {
            var page = new byte[Layout.PageSize];
            var bits = page.AsSpan(AMapBitsOffset, AMapBitsSize);
            var firstSlot = i * AMapBitsSize * 8;
            var free = 0;
            var run = 0;
            var maxRun = 0;
            for (var slot = 0; slot < AMapBitsSize * 8; slot++)
            {
                var index = firstSlot + slot;
                if (index < _allocated.Count && _allocated[index])
                {
                    bits[slot / 8] |= (byte)(0x80 >> (slot % 8));
                    run = 0;
                }
                else
                {
                    free++;
                    run++;
                    maxRun = Math.Max(maxRun, run);
                }
            }

            freeSlots[i] = free;
            maxFreeRuns[i] = (byte)Math.Min(255, maxRun);
            totalFree += (ulong)free * SlotSize;
            var offset = FirstAMap + ((ulong)i * AMapCoverage);
            WritePage(page, offset, 0x84, offset, signature: false);
        }

        if (!Layout.Is4K)
        {
            for (var i = 0; i < amapCount; i++)
            {
                foreach (var (offset, type) in GetMapPages(i).Skip(1))
                {
                    var page = new byte[Layout.PageSize];
                    var bits = page.AsSpan(AMapBitsOffset, AMapBitsSize);
                    if (type == 0x82)
                    {
                        // FMap: the longest free run of each of the next AMaps
                        bits.Clear();
                        for (var j = 0; j < AMapBitsSize && i + j < amapCount; j++)
                        {
                            bits[j] = maxFreeRuns[i + j];
                        }
                    }
                    else
                    {
                        bits.Fill(0xFF);
                    }

                    WritePage(page, offset, type, offset, signature: false);
                }
            }
        }

        // The density list: the AMaps that have free space, the emptiest first
        var dlist = new byte[Layout.PageSize];
        var entries = Enumerable.Range(0, amapCount).Where(i => freeSlots[i] > 0).OrderByDescending(i => freeSlots[i]).Take((Layout.PageDataSize - 8 - 12) / 4).ToList();
        dlist[0] = 0x01; // DFL_BACKFILL_COMPLETE
        dlist[1] = (byte)entries.Count;
        for (var i = 0; i < entries.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(dlist.AsSpan(8 + (i * 4)), (uint)entries[i] | ((uint)Math.Min(freeSlots[entries[i]], 0xFFF) << 20));
        }

        var dlistBid = _nextPageBid;
        WritePage(dlist, Layout.Is4K ? DListOffset4K : DListOffset, 0x86, dlistBid, signature: true);

        // The header
        var header = new byte[Layout.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, NdbHeader.Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), Layout.Is4K ? NdbHeader.MagicClientOst : NdbHeader.MagicClientPst);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), Layout.Format switch { PstFormat.Ansi => 14, PstFormat.Unicode => 23, _ => 36 });
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), 19);
        header[14] = 1;
        header[15] = 1;
        int nidsOffset;
        int rootOffset;
        int fmOffset;
        int cryptOffset;
        if (Layout.IsAnsi)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), (uint)_nextBid << 2);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(28), (uint)_nextPageBid);
            nidsOffset = 36;
            rootOffset = 164;
            fmOffset = 204;
            cryptOffset = 461;
            header[460] = 0x80;
        }
        else
        {
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(32), _nextPageBid);
            nidsOffset = 44;
            rootOffset = 180;
            fmOffset = 256;
            cryptOffset = 513;
            header[512] = 0x80;
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(516), _nextBid << 2);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(Layout.IsAnsi ? 32 : 40), Unique);
        for (var i = 0; i < _nextNids.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(nidsOffset + (i * 4)), _nextNids[i]);
        }

        header.AsSpan(fmOffset, 256).Fill(0xFF);
        header[cryptOffset] = (byte)Encryption;
        var root = header.AsSpan(rootOffset);
        var idSize = Layout.IdSize;
        WriteId(root[4..], fileEof);
        WriteId(root[(4 + idSize)..], FirstAMap + ((ulong)(amapCount - 1) * AMapCoverage));
        WriteId(root[(4 + (2 * idSize))..], totalFree);
        WriteId(root[(4 + (4 * idSize))..], nodeRoot.Bid);
        WriteId(root[(4 + (5 * idSize))..], nodeRoot.Ib);
        WriteId(root[(4 + (6 * idSize))..], blockRoot.Bid);
        WriteId(root[(4 + (7 * idSize))..], blockRoot.Ib);
        root[4 + (8 * idSize)] = 0x02; // VALID_AMAP2

        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), PstCrc.Compute(header.AsSpan(8, 471)));
        if (!Layout.IsAnsi)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(524), PstCrc.Compute(header.AsSpan(8, 516)));
        }

        _stream.Position = 0;
        _stream.Write(header);
        _stream.Flush();
    }

    private Bref WriteBTree(byte pageType, List<byte[]> leafEntries, int leafEntrySize, List<ulong> keys)
    {
        var maxLeaf = Layout.BTreeEntriesSize / leafEntrySize;
        var maxIndex = Layout.BTreeEntriesSize / Layout.BTreeEntrySize;

        // Fill the pages to 90 % so that a later writer can insert entries without splitting every page
        var leafCount = Math.Max(1, (int)(maxLeaf * 0.9));
        var level = new List<(ulong Key, Bref Bref)>();
        for (var start = 0; start < Math.Max(1, leafEntries.Count); start += leafCount)
        {
            var count = Math.Min(leafCount, leafEntries.Count - start);
            var page = CreateBTreePage(leafEntries.Skip(start).Take(count).ToList(), leafEntrySize, maxLeaf, 0);
            level.Add((count > 0 ? keys[start] : 0, WriteBTreePage(page, pageType)));
        }

        var depth = 0;
        while (level.Count > 1)
        {
            depth++;
            var next = new List<(ulong Key, Bref Bref)>();
            var indexCount = Math.Max(2, (int)(maxIndex * 0.9));
            for (var start = 0; start < level.Count; start += indexCount)
            {
                var children = level.Skip(start).Take(indexCount).ToList();
                var entries = children.Select(child =>
                {
                    var entry = new byte[Layout.BTreeEntrySize];
                    WriteId(entry, child.Key);
                    WriteId(entry.AsSpan(Layout.IdSize), child.Bref.Bid);
                    WriteId(entry.AsSpan(2 * Layout.IdSize), child.Bref.Ib);
                    return entry;
                }).ToList();
                next.Add((children[0].Key, WriteBTreePage(CreateBTreePage(entries, Layout.BTreeEntrySize, maxIndex, depth), pageType)));
            }

            level = next;
        }

        return level[0].Bref;
    }

    private byte[] CreateBTreePage(List<byte[]> entries, int entrySize, int maxEntries, int level)
    {
        var page = new byte[Layout.PageSize];
        for (var i = 0; i < entries.Count; i++)
        {
            entries[i].CopyTo(page, i * entrySize);
        }

        var metadata = page.AsSpan(Layout.BTreeEntriesSize);
        if (Layout.Is4K)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(metadata, (ushort)entries.Count);
            BinaryPrimitives.WriteUInt16LittleEndian(metadata[2..], (ushort)maxEntries);
            metadata[4] = (byte)entrySize;
            metadata[5] = (byte)level;
        }
        else
        {
            metadata[0] = (byte)entries.Count;
            metadata[1] = (byte)maxEntries;
            metadata[2] = (byte)entrySize;
            metadata[3] = (byte)level;
        }

        return page;
    }

    private Bref WriteBTreePage(byte[] page, byte pageType)
    {
        var offset = Allocate((ulong)Layout.PageSize, (ulong)Layout.PageSize);
        var bid = _nextPageBid++;
        WritePage(page, offset, pageType, bid, signature: true);
        return new Bref(bid, offset);
    }

    private void WritePage(byte[] page, ulong offset, byte pageType, ulong bid, bool signature)
    {
        var trailer = page.AsSpan(Layout.PageDataSize);
        trailer[0] = pageType;
        trailer[1] = pageType;
        BinaryPrimitives.WriteUInt16LittleEndian(trailer[2..], signature ? NdbReader.ComputeSignature(offset, bid) : (ushort)0);
        var crc = PstCrc.Compute(page.AsSpan(0, Layout.PageDataSize));
        if (Layout.IsAnsi)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(trailer[4..], (uint)bid);
            BinaryPrimitives.WriteUInt32LittleEndian(trailer[8..], crc);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(trailer[4..], crc);
            BinaryPrimitives.WriteUInt64LittleEndian(trailer[8..], bid);
            if (Layout.Is4K)
            {
                // Outlook writes 1 in the 8 bytes that follow the trailer of a 4K page
                BinaryPrimitives.WriteUInt64LittleEndian(trailer[16..], 1);
            }
        }

        _stream.Position = (long)offset;
        _stream.Write(page);
    }

    private ulong WriteBlock(ReadOnlySpan<byte> data, bool isInternal)
    {
        var bid = (_nextBid++ << 2) | (isInternal ? 2u : 0u);
        var stored = data.ToArray();
        var inflatedSize = stored.Length;
        if (!isInternal)
        {
            if (Layout.Is4K && stored.Length > 64)
            {
                var compressed = Compress(stored);
                if (compressed.Length < stored.Length)
                {
                    stored = compressed;
                }
            }

            PstCrypt.Encode(Encryption, stored, bid);
        }

        var alignedSize = Layout.GetAlignedBlockSize(stored.Length);
        var offset = Allocate((ulong)alignedSize, Layout.Is4K ? 512u : 64u);
        var block = new byte[alignedSize];
        stored.CopyTo(block, 0);
        var trailer = block.AsSpan(alignedSize - Layout.BlockTrailerSize);
        BinaryPrimitives.WriteUInt16LittleEndian(trailer, (ushort)stored.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(trailer[2..], NdbReader.ComputeSignature(offset, bid));
        var crc = PstCrc.Compute(stored);
        if (Layout.IsAnsi)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(trailer[4..], (uint)bid);
            BinaryPrimitives.WriteUInt32LittleEndian(trailer[8..], crc);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(trailer[4..], crc);
            BinaryPrimitives.WriteUInt64LittleEndian(trailer[8..], bid);
            if (Layout.Is4K)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(trailer[16..], 2);
                BinaryPrimitives.WriteUInt16LittleEndian(trailer[18..], (ushort)inflatedSize);
            }
        }

        _stream.Position = (long)offset;
        _stream.Write(block);
        _blocks.Add(new BbtEntry(bid, offset, stored.Length, inflatedSize, ReferenceCount: 2));
        return bid;
    }

    private ulong WriteDataTree(List<(ulong Bid, int Size)> leaves)
    {
        var capacity = (Layout.MaxLeafDataSize - 8) / Layout.IdSize;
        var totalSize = leaves.Sum(leaf => (long)leaf.Size);
        if (leaves.Count <= capacity)
        {
            return WriteXBlock([.. leaves.Select(leaf => leaf.Bid)], totalSize, level: 1);
        }

        if (leaves.Count > capacity * capacity)
        {
            throw new InvalidOperationException("The data of a node is too large.");
        }

        var xblocks = new List<ulong>();
        for (var start = 0; start < leaves.Count; start += capacity)
        {
            var range = leaves.GetRange(start, Math.Min(capacity, leaves.Count - start));
            xblocks.Add(WriteXBlock([.. range.Select(leaf => leaf.Bid)], range.Sum(leaf => (long)leaf.Size), level: 1));
        }

        return WriteXBlock(xblocks, totalSize, level: 2);
    }

    private ulong WriteXBlock(List<ulong> children, long totalSize, int level)
    {
        var size = Layout.IdSize;
        var block = new byte[8 + (children.Count * size)];
        block[0] = NdbReader.BlockTypeData;
        block[1] = (byte)level;
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(2), (ushort)children.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4), (uint)totalSize);
        for (var i = 0; i < children.Count; i++)
        {
            WriteId(block.AsSpan(8 + (i * size)), children[i]);
        }

        return WriteBlock(block, isInternal: true);
    }

    /// <summary>Allocates space in the file, after the space already allocated, skipping the pages of the allocation maps.</summary>
    private ulong Allocate(ulong size, ulong alignment)
    {
        var position = (_position + alignment - 1) / alignment * alignment;
        while (true)
        {
            var next = SkipMapPages(position, size);
            if (next == position)
            {
                break;
            }

            position = (next + alignment - 1) / alignment * alignment;
        }

        if (position + size > MaxFileSize)
        {
            throw new InvalidOperationException("The content is too large for the format of the file (2 GB for an ANSI file).");
        }

        MarkAllocated(position, size);
        _position = position + size;
        return position;
    }

    /// <summary>Returns the first position at or after <paramref name="position"/> where <paramref name="size"/> bytes do not overlap the pages of an allocation map.</summary>
    private ulong SkipMapPages(ulong position, ulong size)
    {
        if (position < FirstAMap)
        {
            position = FirstAMap;
        }

        var index = (int)((position - FirstAMap) / AMapCoverage);
        for (var i = index; i <= index + 1; i++)
        {
            foreach (var (offset, _) in GetMapPages(i))
            {
                if (position < offset + (ulong)Layout.PageSize && offset < position + size)
                {
                    return offset + (ulong)Layout.PageSize;
                }
            }
        }

        return position;
    }

    /// <summary>Returns the pages of the allocation maps at the start of the space an AMap maps: the AMap, then a PMap, an FMap and an FPMap at their intervals.</summary>
    private IEnumerable<(ulong Offset, byte Type)> GetMapPages(int amapIndex)
    {
        var offset = FirstAMap + ((ulong)amapIndex * AMapCoverage);
        yield return (offset, 0x84);
        if (Layout.Is4K)
        {
            yield break;
        }

        if (amapIndex % 8 == 0)
        {
            yield return (offset + 512, 0x83);
            if (amapIndex >= FMapFirstAMap && (amapIndex - FMapFirstAMap) % FMapInterval == 0)
            {
                yield return (offset + 1024, 0x82);
            }

            if (!Layout.IsAnsi && amapIndex >= FPMapFirstAMap && (amapIndex - FPMapFirstAMap) % FPMapInterval == 0)
            {
                yield return (offset + 1536, 0x85);
            }
        }
    }

    private void MarkAllocated(ulong offset, ulong size)
    {
        if (offset < FirstAMap)
        {
            return;
        }

        var first = (int)((offset - FirstAMap) / SlotSize);
        var last = (int)((offset + size - FirstAMap + SlotSize - 1) / SlotSize);
        while (_allocated.Count < last)
        {
            _allocated.Add(false);
        }

        for (var i = first; i < last; i++)
        {
            _allocated[i] = true;
        }
    }

    private byte[] EncodeNodeEntry(NbtEntry node)
    {
        var size = Layout.IdSize;
        var entry = new byte[Layout.NodeBTreeEntrySize];
        BinaryPrimitives.WriteUInt32LittleEndian(entry, node.Nid);
        WriteId(entry.AsSpan(size), node.BidData);
        WriteId(entry.AsSpan(2 * size), node.BidSub);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(3 * size), node.NidParent);
        return entry;
    }

    private byte[] EncodeBlockEntry(BbtEntry block)
    {
        var size = Layout.IdSize;
        var entry = new byte[Layout.BlockBTreeEntrySize];
        WriteId(entry, block.Bid);
        WriteId(entry.AsSpan(size), block.Ib);
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(2 * size), (ushort)block.Size);
        if (Layout.Is4K)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan((2 * size) + 2), (ushort)block.InflatedSize);
            BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan((2 * size) + 4), (ushort)block.ReferenceCount);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan((2 * size) + 2), (ushort)block.ReferenceCount);
        }

        return entry;
    }

    private void WriteId(Span<byte> destination, ulong value)
    {
        if (Layout.IdSize == 4)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)value);
        }
        else
        {
            BinaryPrimitives.WriteUInt64LittleEndian(destination, value);
        }
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    private static int ReadBlock(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
