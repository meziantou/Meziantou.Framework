namespace Meziantou.Framework.Email.Mapi;

/// <summary>How the content of an attachment is stored (PidTagAttachMethod, [MS-OXCMSG] 2.2.2.9).</summary>
#pragma warning disable CA1027 // The values are not flags
public enum MapiAttachmentMethod
{
    /// <summary>The attachment has no content.</summary>
    None = 0,

    /// <summary>The content is in the attachment (<see cref="MapiAttachment.OpenRead"/>).</summary>
    ByValue = 1,

    /// <summary>The attachment is a path to a file on a shared location.</summary>
    ByReference = 2,

    /// <summary>The attachment is a path to a file.</summary>
    ByReferenceOnly = 4,

    /// <summary>The content is a message (<see cref="MapiAttachment.GetEmbeddedMessage"/>).</summary>
    EmbeddedMessage = 5,

    /// <summary>The content is an OLE storage (<see cref="MapiAttachment.OpenRead"/> returns its bytes).</summary>
    Storage = 6,

    /// <summary>The attachment is a link to a file in the cloud.</summary>
    ByWebReference = 7,
}
#pragma warning restore CA1027
