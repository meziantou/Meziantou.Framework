namespace Meziantou.Framework.Mail.Internals.Ndb;

/// <summary>A node of the node database: a top-level node of the NBT, or a subnode of another node.</summary>
internal sealed class NdbNode
{
    private Dictionary<uint, NdbNode>? _subnodes;

    public NdbNode(NdbReader reader, uint nid, ulong bidData, ulong bidSub, uint nidParent = 0)
    {
        Reader = reader;
        Nid = nid;
        BidData = bidData;
        BidSub = bidSub;
        NidParent = nidParent;
    }

    public NdbReader Reader { get; }

    public uint Nid { get; }

    public ulong BidData { get; }

    public ulong BidSub { get; }

    public uint NidParent { get; }

    public NidType NidType => (NidType)(Nid & 0x1F);

    public byte[][] ReadDataBlocks() => BidData == 0 ? [] : Reader.ReadDataBlocks(BidData);

    public byte[] ReadData(long maxSize) => BidData == 0 ? [] : Reader.ReadData(BidData, maxSize);

    public long GetDataSize() => BidData == 0 ? 0 : Reader.GetDataSize(BidData);

    public Stream OpenData() => BidData == 0 ? new MemoryStream([], writable: false) : new NdbDataStream(Reader, BidData);

    public NdbNode? GetSubnode(uint nid)
    {
        var subnodes = GetSubnodes();
        return subnodes.TryGetValue(nid, out var node) ? node : null;
    }

    public IReadOnlyDictionary<uint, NdbNode> GetSubnodes()
    {
        return _subnodes ??= BidSub == 0 ? [] : Reader.ReadSubnodes(BidSub, Nid);
    }
}
