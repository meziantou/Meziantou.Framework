---
title: Introduction
description: The email, contact and calendar formats Meziantou.Framework.Mail reads and writes, and the model they convert to.
---

`Meziantou.Framework.Mail` reads and writes the files of email clients, written in C# without native dependencies:

| Format | Files | Read | Write | Pages |
| --- | --- | --- | --- | --- |
| Outlook data files | `.pst`, `.ost` | Yes | Yes | [Outlook data files](03-Pst/01-Overview.md) |
| Email messages (Internet Message Format, MIME) | `.eml` | Yes | Yes | [Email files](04-Mime/01-Overview.md) |
| Outlook items | `.msg` | Yes | Yes | [Outlook items](05-Msg/01-Overview.md) |
| TNEF streams | `winmail.dat` | Yes | Yes | [Winmail.dat](06-Tnef/01-Overview.md) |
| mbox files (mboxo, mboxrd, mboxcl, mboxcl2) | `.mbox`, Thunderbird folders | Yes | Yes | [mbox files](07-Mailboxes/01-Mbox.md) |
| Maildir folders (Maildir++) | `cur`, `new`, `tmp` | Yes | Yes | [Maildir folders](07-Mailboxes/02-Maildir.md) |
| Apple Mail messages | `.emlx` | Yes | Yes | [Apple Mail](07-Mailboxes/03-AppleMail.md) |
| Thunderbird summaries, Mork databases | `.msf`, `.mab` | Yes | Yes | [Thunderbird](07-Mailboxes/04-Thunderbird.md) |
| Contacts (vCard 2.1, 3.0, 4.0) | `.vcf` | Yes | Yes | [vCard](08-ContactsAndCalendars/01-VCard.md) |
| Calendars (iCalendar), with time zones and recurrences | `.ics` | Yes | Yes | [iCalendar](08-ContactsAndCalendars/02-ICalendar.md) |
| Offline address books (version 4 Full Details) | `udetails.oab` | Yes | Yes | [Offline address books](09-OfflineAddressBooks/01-Overview.md) |

Each format has its own objects, which give access to everything the format stores: the MAPI properties of an Outlook
item, the headers and the MIME parts of a message. [EmailMessage](cref:Meziantou.Framework.Mail.EmailMessage) is the
model they convert to and from, so that a message read in one format can be written in another: see
[Getting started](02-GettingStarted.md).
