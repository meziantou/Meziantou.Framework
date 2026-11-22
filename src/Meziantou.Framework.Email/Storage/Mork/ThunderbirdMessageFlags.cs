namespace Meziantou.Framework.Email.Storage;

/// <summary>The flags of a message in the summary of a folder of Thunderbird (the <c>flags</c> column, <c>nsMsgMessageFlags</c>).</summary>
[Flags]
#pragma warning disable CA1711 // The name of the type is the name of the concept
public enum ThunderbirdMessageFlags
#pragma warning restore CA1711
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>The message was read.</summary>
    Read = 0x1,

    /// <summary>The message was replied to.</summary>
    Replied = 0x2,

    /// <summary>The message is starred (flagged).</summary>
    Marked = 0x4,

    /// <summary>The message is deleted and waits for the folder to be compacted.</summary>
    Expunged = 0x8,

    /// <summary>The subject started with <c>Re:</c>, which Thunderbird removes from the subject of the summary.</summary>
    HasRe = 0x10,

    /// <summary>The message is stored for offline use.</summary>
    Offline = 0x80,

    /// <summary>The thread of the message is watched.</summary>
    Watched = 0x100,

    /// <summary>Only the headers of the message were downloaded.</summary>
    Partial = 0x400,

    /// <summary>The message is queued to be sent.</summary>
    Queued = 0x800,

    /// <summary>The message was forwarded.</summary>
    Forwarded = 0x1000,

    /// <summary>The message is new.</summary>
    New = 0x10000,

    /// <summary>The thread of the message is ignored.</summary>
    Ignored = 0x40000,

    /// <summary>The message is marked as deleted on the IMAP server.</summary>
    ImapDeleted = 0x200000,

    /// <summary>The message is a template.</summary>
    Template = 0x1000000,

    /// <summary>The message has attachments.</summary>
    Attachment = 0x10000000,
}
