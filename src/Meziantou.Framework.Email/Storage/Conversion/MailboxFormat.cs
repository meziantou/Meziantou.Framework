namespace Meziantou.Framework.Email.Storage;

/// <summary>The formats of mailboxes <see cref="MailboxConverter"/> converts between.</summary>
public enum MailboxFormat
{
    /// <summary>An Outlook data file (<c>.pst</c>; an <c>.ost</c> file is read too): its folders under the top of the personal folders.</summary>
    Pst,

    /// <summary>A folder of mbox files in the layout of Thunderbird: a file per folder, and the subfolders of a folder in a <c>.sbd</c> directory with its name. A single mbox file is read as a mailbox of one folder.</summary>
    Mbox,

    /// <summary>A Maildir folder with its Maildir++ subfolders (<c>.Sent</c>, <c>.Archive.2026</c>): the messages of the root are the inbox.</summary>
    Maildir,
}
