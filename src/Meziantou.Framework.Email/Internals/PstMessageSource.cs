using Meziantou.Framework.Email.Internals.Ltp;
using Meziantou.Framework.Email.Internals.Ndb;

namespace Meziantou.Framework.Email.Internals;

/// <summary>An item stored in a node of an Outlook data file.</summary>
internal sealed class PstMessageSource : MessageSource
{
    public PstMessageSource(PstContext context, NdbNode node)
    {
        Context = context;
        Node = node;
    }

    public PstContext Context { get; }

    public NdbNode Node { get; }

    public override PstReadOptions Options => Context.Options;

    public override uint NodeId => Node.Nid;

    public override bool IsAssociated => Node.NidType == NidType.AssociatedMessage;

    public static MapiMessage Load(PstContext context, NdbNode node, int embeddingDepth)
    {
        MapiMessage.CheckDepth(context.Options, embeddingDepth);
        var pc = new PropertyContext(node);
        var properties = new MapiPropertyCollection(new PstPropertySource(context, pc.Heap, node), pc.Properties);
        return MapiMessage.Create(new PstMessageSource(context, node), properties, embeddingDepth);
    }

    public override List<MapiRecipient> LoadRecipients(MapiMessage message)
    {
        var result = new List<MapiRecipient>();
        var node = Node.GetSubnode(Nids.RecipientTable);
        if (node is null)
        {
            return result;
        }

        var table = new TableContext(node);
        var source = new PstPropertySource(Context, table.Heap, node);
        for (var i = 0; i < table.RowCount; i++)
        {
            result.Add(new MapiRecipient(new MapiPropertyCollection(source, table.GetRow(i))));
        }

        return result;
    }

    public override List<MapiAttachment> LoadAttachments(MapiMessage message)
    {
        var result = new List<MapiAttachment>();
        var node = Node.GetSubnode(Nids.AttachmentTable);
        if (node is null)
        {
            return result;
        }

        var table = new TableContext(node);
        for (var i = 0; i < table.RowCount; i++)
        {
            var nid = table.GetRowId(i);
            var attachmentNode = Node.GetSubnode(nid) ?? throw new InvalidPstException($"The attachment 0x{nid:X} of message 0x{Node.Nid:X} does not exist.");
            result.Add(new MapiAttachment(message, new PstAttachmentSource(Context, attachmentNode)));
        }

        return result;
    }
}
