namespace Meziantou.Framework.Mail.Internals.Ndb;

/// <summary>The sizes of the structures of the node database, which depend on the format ([MS-PST] 2.2.2).</summary>
internal sealed class NdbLayout
{
    public static NdbLayout Ansi { get; } = new()
    {
        Format = PstFormat.Ansi,
        IdSize = 4,
        PageSize = 512,
        PageTrailerSize = 12,
        BTreeEntriesSize = 496,
        BlockTrailerSize = 12,
        BlockAlignment = 64,
        MaxBlockSize = 8192,
        BTreeEntrySize = 12,
        BlockBTreeEntrySize = 12,
        NodeBTreeEntrySize = 16,
        SubnodeLeafEntrySize = 12,
        SubnodeIntermediateEntrySize = 8,
        HeaderSize = 512,
    };

    public static NdbLayout Unicode { get; } = new()
    {
        Format = PstFormat.Unicode,
        IdSize = 8,
        PageSize = 512,
        PageTrailerSize = 16,
        BTreeEntriesSize = 488,
        BlockTrailerSize = 16,
        BlockAlignment = 64,
        MaxBlockSize = 8192,
        BTreeEntrySize = 24,
        BlockBTreeEntrySize = 24,
        NodeBTreeEntrySize = 32,
        SubnodeLeafEntrySize = 24,
        SubnodeIntermediateEntrySize = 16,
        HeaderSize = 564,
    };

    public static NdbLayout Unicode4K { get; } = new()
    {
        Format = PstFormat.Unicode4K,
        IdSize = 8,
        PageSize = 4096,
        PageTrailerSize = 24,
        BTreeEntriesSize = 4056,
        BlockTrailerSize = 24,
        BlockAlignment = 512,
        MaxBlockSize = 65536,
        BTreeEntrySize = 24,
        BlockBTreeEntrySize = 24,
        NodeBTreeEntrySize = 32,
        SubnodeLeafEntrySize = 24,
        SubnodeIntermediateEntrySize = 16,
        HeaderSize = 564,
    };

    public PstFormat Format { get; private init; }

    public bool IsAnsi => Format == PstFormat.Ansi;

    public bool Is4K => Format == PstFormat.Unicode4K;

    /// <summary>The size of a BID or of a file offset (IB).</summary>
    public int IdSize { get; private init; }

    public int PageSize { get; private init; }

    /// <summary>The size of the trailer of a page: 16 bytes for a Unicode page, 12 for ANSI, 24 for a 4K page (16 bytes, then 8 bytes that are not described).</summary>
    public int PageTrailerSize { get; private init; }

    /// <summary>The size of the data of a page, covered by its CRC. The trailer follows it (in a 4K page, 8 more bytes follow the trailer).</summary>
    public int PageDataSize => PageSize - PageTrailerSize;

    /// <summary>The size of the entries of a B-tree page (rgentries).</summary>
    public int BTreeEntriesSize { get; private init; }

    public int BlockTrailerSize { get; private init; }

    public int BlockAlignment { get; private init; }

    /// <summary>The maximum size of a block, trailer included.</summary>
    public int MaxBlockSize { get; private init; }

    /// <summary>The maximum size of the data of a block: what a data tree puts in each block, except the last.</summary>
    public int MaxBlockDataSize => MaxBlockSize - BlockTrailerSize;

    /// <summary>The size of the data the writer puts in each block of a data tree, of a heap or of a row matrix: 8176 bytes (8180 in an ANSI file), as Outlook does, in a 4K file too.</summary>
    public int MaxLeafDataSize => IsAnsi ? 8180 : 8176;

    public int BTreeEntrySize { get; private init; }

    public int BlockBTreeEntrySize { get; private init; }

    public int NodeBTreeEntrySize { get; private init; }

    public int SubnodeLeafEntrySize { get; private init; }

    public int SubnodeIntermediateEntrySize { get; private init; }

    public int HeaderSize { get; private init; }

    public int BrefSize => IdSize * 2;

    public static NdbLayout Get(PstFormat format) => format switch
    {
        PstFormat.Ansi => Ansi,
        PstFormat.Unicode => Unicode,
        _ => Unicode4K,
    };

    public int GetAlignedBlockSize(int dataSize)
    {
        var size = dataSize + BlockTrailerSize;
        return (size + BlockAlignment - 1) / BlockAlignment * BlockAlignment;
    }
}
