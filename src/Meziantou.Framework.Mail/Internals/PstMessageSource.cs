using Meziantou.Framework.Mail.Internals.Ltp;
using Meziantou.Framework.Mail.Internals.Ndb;

namespace Meziantou.Framework.Mail.Internals;

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

    public static PstMessage Load(PstContext context, NdbNode node, int embeddingDepth)
    {
        PstMessage.CheckDepth(context.Options, embeddingDepth);
        var pc = new PropertyContext(node);
        var properties = new PstPropertyCollection(new PstPropertySource(context, pc.Heap, node), pc.Properties);
        return PstMessage.Create(new PstMessageSource(context, node), properties, embeddingDepth);
    }

    public override List<PstRecipient> LoadRecipients(PstMessage message)
    {
        var result = new List<PstRecipient>();
        var node = Node.GetSubnode(Nids.RecipientTable);
        if (node is null)
        {
            return result;
        }

        var table = new TableContext(node);
        var source = new PstPropertySource(Context, table.Heap, node);
        for (var i = 0; i < table.RowCount; i++)
        {
            result.Add(new PstRecipient(new PstPropertyCollection(source, table.GetRow(i))));
        }

        return result;
    }

    public override List<PstAttachment> LoadAttachments(PstMessage message)
    {
        var result = new List<PstAttachment>();
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
            result.Add(new PstAttachment(message, new PstAttachmentSource(Context, attachmentNode)));
        }

        return result;
    }
}
