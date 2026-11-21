namespace Meziantou.Framework.Mail.Internals;

/// <summary>Where an item is stored: a node of an Outlook data file, or the storages of an .msg file.</summary>
internal abstract class MessageSource
{
    public abstract PstReadOptions Options { get; }

    /// <summary>Gets the identifier of the node of the item, or 0 when the item is not in an Outlook data file.</summary>
    public abstract uint NodeId { get; }

    public abstract bool IsAssociated { get; }

    public abstract List<PstRecipient> LoadRecipients(PstMessage message);

    public abstract List<PstAttachment> LoadAttachments(PstMessage message);
}
