namespace Meziantou.Framework.Mail;

/// <summary>The flags of a message (PidTagMessageFlags, [MS-OXCMSG] 2.2.1.6).</summary>
[Flags]
#pragma warning disable CA1711 // The name of the property is MessageFlags
public enum PstMessageFlags
#pragma warning restore CA1711
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>The message has been read.</summary>
    Read = 0x0001,

    /// <summary>The message has not been modified since it was received.</summary>
    Unmodified = 0x0002,

    /// <summary>The message is queued to be sent.</summary>
    Submitted = 0x0004,

    /// <summary>The message is a draft that has not been sent.</summary>
    Unsent = 0x0008,

    /// <summary>The message has attachments.</summary>
    HasAttachments = 0x0010,

    /// <summary>The user who receives the message is also its sender.</summary>
    FromMe = 0x0020,

    /// <summary>The message is an associated (hidden) item.</summary>
    Associated = 0x0040,

    /// <summary>The message is to be resent.</summary>
    Resend = 0x0080,

    /// <summary>A read receipt is requested.</summary>
    NotifyRead = 0x0100,

    /// <summary>A non-read receipt is requested.</summary>
    NotifyUnread = 0x0200,

    /// <summary>The message has been read at least once.</summary>
    EverRead = 0x0400,

    /// <summary>The message was received from the Internet.</summary>
    Internet = 0x2000,

    /// <summary>The message comes from an untrusted source.</summary>
    Untrusted = 0x8000,
}
