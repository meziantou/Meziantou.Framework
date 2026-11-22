---
title: Overview
description: The mailboxes of Unix mail tools, Thunderbird and Apple Mail the library reads and writes, and how to convert a mailbox between them and Outlook data files.
---

Mail clients other than Outlook store a folder of messages as plain MIME messages (see [Email files](../04-Mime/01-Overview.md)):
one after the other in a file, or a file per message in a directory. The types of these mailboxes are in the
[Meziantou.Framework.Email.Storage](cref:Meziantou.Framework.Email.Storage) namespace:

| Mailbox | Used by | Types | Page |
| --- | --- | --- | --- |
| mbox file | Unix mail tools (mutt), Thunderbird, exports of Gmail | [MboxReader](cref:Meziantou.Framework.Email.Storage.MboxReader), [MboxWriter](cref:Meziantou.Framework.Email.Storage.MboxWriter) | [mbox files](02-Mbox.md) |
| Maildir folder | Mail servers (Dovecot, Courier), mutt | [MaildirFolder](cref:Meziantou.Framework.Email.Storage.MaildirFolder) | [Maildir folders](03-Maildir.md) |
| `.emlx` file | Apple Mail | [EmlxFile](cref:Meziantou.Framework.Email.Storage.EmlxFile) | [Apple Mail](04-AppleMail.md) |
| `.msf` summary, Mork database | Thunderbird | [ThunderbirdSummary](cref:Meziantou.Framework.Email.Storage.ThunderbirdSummary), [MorkDocument](cref:Meziantou.Framework.Email.Storage.MorkDocument) | [Thunderbird](05-Thunderbird.md) |

[MailboxConverter](cref:Meziantou.Framework.Email.Storage.MailboxConverter) converts a whole mailbox, with its folders,
between Outlook data files, the mbox files of Thunderbird and Maildir folders, or exports its messages to .eml or .msg
files: see [Convert mailboxes](06-Conversion.md).
