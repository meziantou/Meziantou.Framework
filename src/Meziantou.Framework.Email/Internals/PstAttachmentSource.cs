using System.Buffers.Binary;
using Meziantou.Framework.Email.Internals.Ltp;
using Meziantou.Framework.Email.Internals.Ndb;

namespace Meziantou.Framework.Email.Internals;

/// <summary>An attachment stored in a subnode of an item of an Outlook data file.</summary>
internal sealed class PstAttachmentSource : AttachmentSource
{
    private readonly PstContext _context;
    private readonly NdbNode _node;

    public PstAttachmentSource(PstContext context, NdbNode node)
    {
        _context = context;
        _node = node;
        var pc = new PropertyContext(node);
        Properties = new MapiPropertyCollection(new PstPropertySource(context, pc.Heap, node), pc.Properties);
    }

    public override MapiPropertyCollection Properties { get; }

    public override MapiMessage? LoadEmbeddedMessage(MapiProperty reference, int embeddingDepth)
    {
        // [MS-PST] 2.3.3.5: the value of an object is the identifier of a subnode, then the size of the object
        uint nid;
        if (reference.Raw.Inline is null && reference.Raw.Hnid != 0 && !HeapOnNode.IsHid(reference.Raw.Hnid))
        {
            nid = reference.Raw.Hnid;
        }
        else
        {
            var value = reference.GetBytes();
            if (value.Length < 4)
            {
                throw new InvalidPstException("The reference to an embedded message is truncated.");
            }

            nid = BinaryPrimitives.ReadUInt32LittleEndian(value);
        }

        var node = _node.GetSubnode(nid) ?? throw new InvalidPstException($"The embedded message 0x{nid:X} of attachment 0x{_node.Nid:X} does not exist.");
        return PstMessageSource.Load(_context, node, embeddingDepth);
    }
}
