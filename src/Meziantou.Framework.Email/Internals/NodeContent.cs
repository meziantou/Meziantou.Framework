using Meziantou.Framework.Email.Internals.Ndb;

namespace Meziantou.Framework.Email.Internals;

/// <summary>The content of a node to write: its data (heap blocks, bytes or a stream) and its subnodes.</summary>
internal sealed class NodeContent
{
    private uint _nextSubnodeIndex = 0x400;

    public List<byte[]>? HeapBlocks { get; set; }

    public byte[]? Data { get; set; }

    public Stream? DataStream { get; set; }

    public Dictionary<uint, NodeContent> Subnodes { get; } = [];

    /// <summary>Allocates the identifier of a subnode. The indexes start above the special subnodes (0x671, 0x692).</summary>
    public uint AllocateSubnodeNid(NidType type) => Nids.Make(type, _nextSubnodeIndex++);

    /// <summary>Writes the data and the subnodes, and returns the BIDs of the node.</summary>
    public (ulong BidData, ulong BidSub) Write(NdbWriter writer)
    {
        ulong bidData = 0;
        if (HeapBlocks is not null)
        {
            bidData = writer.WriteDataBlocks(HeapBlocks);
        }
        else if (Data is not null)
        {
            bidData = writer.WriteData(Data);
        }
        else if (DataStream is not null)
        {
            // Rewind the stream, so that the builder that provides it can be written again
            var start = DataStream.CanSeek ? DataStream.Position : -1;
            bidData = writer.WriteData(DataStream);
            if (start >= 0)
            {
                DataStream.Position = start;
            }
        }

        var subnodes = new List<(uint Nid, ulong BidData, ulong BidSub)>(Subnodes.Count);
        foreach (var (nid, subnode) in Subnodes)
        {
            var (subData, subSub) = subnode.Write(writer);
            subnodes.Add((nid, subData, subSub));
        }

        return (bidData, writer.WriteSubnodes(subnodes));
    }
}
