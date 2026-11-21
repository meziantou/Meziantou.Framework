using System.Buffers.Binary;
using Meziantou.Framework.Email.Internals.Ltp;
using Meziantou.Framework.Email.Internals.Ndb;

namespace Meziantou.Framework.Email.Internals;

/// <summary>Checks the structures of a file, without stopping at the first error ([MS-PST] 2.2 and 2.3).</summary>
internal static class PstValidator
{
    public static List<PstValidationIssue> Validate(PstFile file)
    {
        var issues = new List<PstValidationIssue>();
        var reader = file.Reader;
        var layout = reader.Layout;
        var allocations = new List<(ulong Offset, ulong Size, string Name)>();

        // The B-trees: pages, levels, key order
        var nodes = ValidateBTree(reader, reader.Header.NodeBTreeRoot, NdbReader.PageTypeNodeBTree, issues, allocations);
        var blocks = ValidateBTree(reader, reader.Header.BlockBTreeRoot, NdbReader.PageTypeBlockBTree, issues, allocations);

        // The blocks: position, trailer, CRC, signature, content
        var blockIds = new HashSet<ulong>();
        foreach (var entry in blocks)
        {
            var bid = NdbHeader.ReadId(entry, layout.IdSize) & ~1UL;
            blockIds.Add(bid);
            var ib = NdbHeader.ReadId(entry.AsSpan(layout.IdSize), layout.IdSize);
            var size = BinaryPrimitives.ReadUInt16LittleEndian(entry.AsSpan(2 * layout.IdSize));
            if (ib % (ulong)layout.BlockAlignment != 0)
            {
                issues.Add(new PstValidationIssue($"The block 0x{bid:X} is not aligned on {layout.BlockAlignment} bytes.", ib, blockId: bid));
            }

            allocations.Add((ib, (ulong)layout.GetAlignedBlockSize(size), $"block 0x{bid:X}"));
            Check(issues, () => reader.ReadBlock(bid), blockId: bid);
        }

        // The nodes: their data and their subnodes exist and can be read
        foreach (var entry in nodes)
        {
            var nid = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            var bidData = NdbHeader.ReadId(entry.AsSpan(layout.IdSize), layout.IdSize);
            var bidSub = NdbHeader.ReadId(entry.AsSpan(2 * layout.IdSize), layout.IdSize);
            if (bidData != 0 && !blockIds.Contains(bidData & ~1UL))
            {
                issues.Add(new PstValidationIssue($"The data block 0x{bidData:X} of node 0x{nid:X} does not exist.", nodeId: nid, blockId: bidData));
                continue;
            }

            if (bidSub != 0 && !blockIds.Contains(bidSub & ~1UL))
            {
                issues.Add(new PstValidationIssue($"The subnode block 0x{bidSub:X} of node 0x{nid:X} does not exist.", nodeId: nid, blockId: bidSub));
                continue;
            }

            var node = new NdbNode(reader, nid, bidData, bidSub);
            Check(issues, () => ValidateNode(file.Context, node, depth: 0), nodeId: nid);
        }

        ValidateAllocations(reader, allocations, issues);
        return issues;
    }

    private static List<byte[]> ValidateBTree(NdbReader reader, Bref root, byte pageType, List<PstValidationIssue> issues, List<(ulong Offset, ulong Size, string Name)> allocations)
    {
        var name = pageType == NdbReader.PageTypeNodeBTree ? "node B-tree" : "block B-tree";
        var result = new List<byte[]>();
        try
        {
            ulong? previous = null;
            foreach (var entry in reader.EnumerateBTreeEntries(root, pageType, page => allocations.Add((page.Ib, (ulong)reader.Layout.PageSize, $"page 0x{page.Bid:X}"))))
            {
                var key = pageType == NdbReader.PageTypeNodeBTree ? BinaryPrimitives.ReadUInt32LittleEndian(entry) : NdbHeader.ReadId(entry, reader.Layout.IdSize) & ~1UL;
                if (previous is not null && key <= previous)
                {
                    issues.Add(new PstValidationIssue($"The keys of the {name} are not sorted (0x{key:X} after 0x{previous:X})."));
                }

                previous = key;
                result.Add(entry);
            }
        }
        catch (EmailException ex)
        {
            issues.Add(new PstValidationIssue($"The {name} is invalid: {ex.Message}"));
        }

        return result;
    }

    private static void ValidateNode(PstContext context, NdbNode node, int depth)
    {
        var data = node.ReadDataBlocks();
        foreach (var subnode in node.GetSubnodes().Values)
        {
            if (depth < node.Reader.Options.MaxTreeDepth)
            {
                ValidateNode(context, subnode, depth + 1);
            }
        }

        // The structures of the LTP layer: a heap, and a property or a table context
        if (data.Length == 0 || data[0].Length < 4 || data[0][2] != HeapOnNode.Signature)
        {
            return;
        }

        switch (data[0][3])
        {
            case PropertyContext.Signature:
                var pc = new PropertyContext(new HeapOnNode(node, data));
                var source = new PstPropertySource(context, pc.Heap, node);
                foreach (var property in pc.Properties)
                {
                    source.GetSize(property);
                }

                break;
            case TableContext.Signature:
                var tc = new TableContext(new HeapOnNode(node, data));
                var tableSource = new PstPropertySource(context, tc.Heap, node);
                for (var i = 0; i < tc.RowCount; i++)
                {
                    foreach (var cell in tc.GetRow(i))
                    {
                        tableSource.GetSize(cell);
                    }
                }

                break;
        }
    }

    private static void ValidateAllocations(NdbReader reader, List<(ulong Offset, ulong Size, string Name)> allocations, List<PstValidationIssue> issues)
    {
        allocations.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        for (var i = 1; i < allocations.Count; i++)
        {
            var previous = allocations[i - 1];
            if (previous.Offset + previous.Size > allocations[i].Offset)
            {
                issues.Add(new PstValidationIssue($"The {previous.Name} overlaps the {allocations[i].Name}.", allocations[i].Offset));
            }
        }

        foreach (var allocation in allocations)
        {
            if (allocation.Offset + allocation.Size > reader.Header.FileEof || allocation.Offset + allocation.Size > (ulong)reader.Length)
            {
                issues.Add(new PstValidationIssue($"The {allocation.Name} is beyond the end of the file.", allocation.Offset));
            }
        }

        // The AMaps mark the space of every page and block as allocated
        if (reader.Header.AMapValid == 0)
        {
            return;
        }

        var layout = reader.Layout;
        var firstAMap = layout.Is4K ? 0x22000UL : 0x4400UL;
        var slotSize = layout.Is4K ? 512UL : 64UL;
        var bitsSize = layout.Is4K ? layout.PageDataSize : 496;
        var bitsOffset = layout.IsAnsi ? 4 : 0;
        var coverage = (ulong)bitsSize * 8 * slotSize;
        var maps = new Dictionary<ulong, byte[]>();
        foreach (var allocation in allocations)
        {
            for (var offset = allocation.Offset; offset < allocation.Offset + allocation.Size; offset += slotSize)
            {
                if (offset < firstAMap)
                {
                    continue;
                }

                var mapOffset = firstAMap + ((offset - firstAMap) / coverage * coverage);
                if (!maps.TryGetValue(mapOffset, out var map))
                {
                    try
                    {
                        map = reader.ReadPage(new Bref(mapOffset, mapOffset), 0x84);
                    }
                    catch (EmailException ex)
                    {
                        issues.Add(new PstValidationIssue($"The AMap at offset 0x{mapOffset:X} is invalid: {ex.Message}", mapOffset));
                        map = [];
                    }

                    maps[mapOffset] = map;
                }

                if (map.Length == 0)
                {
                    break;
                }

                var slot = (int)((offset - mapOffset) / slotSize);
                if ((map[bitsOffset + (slot / 8)] & (0x80 >> (slot % 8))) == 0)
                {
                    issues.Add(new PstValidationIssue($"The AMap at offset 0x{mapOffset:X} does not mark the space of the {allocation.Name} as allocated.", offset));
                    break;
                }
            }
        }
    }

    private static void Check(List<PstValidationIssue> issues, Action action, uint? nodeId = null, ulong? blockId = null)
    {
        try
        {
            action();
        }
        catch (EmailException ex)
        {
            issues.Add(new PstValidationIssue(ex.Message, nodeId: nodeId, blockId: blockId));
        }
    }
}
