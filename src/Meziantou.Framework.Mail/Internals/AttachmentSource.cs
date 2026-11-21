namespace Meziantou.Framework.Mail.Internals;

/// <summary>Where an attachment is stored: a node of an Outlook data file, or a storage of an .msg file.</summary>
internal abstract class AttachmentSource
{
    public abstract PstPropertyCollection Properties { get; }

    /// <summary>Returns the embedded message the object property of the attachment references, or <see langword="null"/> when the object is not a message (an OLE object).</summary>
    public abstract PstMessage? LoadEmbeddedMessage(PstProperty reference, int embeddingDepth);
}
