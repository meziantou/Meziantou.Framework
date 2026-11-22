namespace Meziantou.Framework.Email.Storage;

/// <summary>The formats of mailboxes <see cref="MailboxConverter"/> converts between. <see cref="Eml"/> and <see cref="Msg"/> are only destinations.</summary>
public enum MailboxFormat
{
    /// <summary>An Outlook data file (<c>.pst</c>; an <c>.ost</c> file is read too): its folders under the top of the personal folders.</summary>
    Pst,

    /// <summary>A folder of mbox files in the layout of Thunderbird: a file per folder, and the subfolders of a folder in a <c>.sbd</c> directory with its name. A single mbox file is read as a mailbox of one folder.</summary>
    Mbox,

    /// <summary>A Maildir folder with its Maildir++ subfolders (<c>.Sent</c>, <c>.Archive.2026</c>): the messages of the root are the inbox.</summary>
    Maildir,

    /// <summary>
    /// A directory of email messages (<c>.eml</c>), only as a destination: a subdirectory per folder (<c>Inbox</c>, <c>Archive/2026</c>),
    /// and a file per message, named after its subject. The messages of mbox and Maildir folders are copied as they are.
    /// </summary>
    Eml,

    /// <summary>
    /// A directory of Outlook items (<c>.msg</c>), only as a destination: a subdirectory per folder (<c>Inbox</c>, <c>Archive/2026</c>),
    /// and a file per item, named after its subject. As in an Outlook data file, the items that are not emails (contacts,
    /// appointments...) are kept.
    /// </summary>
    Msg,
}
