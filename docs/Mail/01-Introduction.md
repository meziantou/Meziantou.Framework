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

Each format has its own objects, which give access to everything the format stores: the MAPI properties of an Outlook
item, the headers and the MIME parts of a message. [EmailMessage](cref:Meziantou.Framework.Mail.EmailMessage) is the
model they convert to and from, so that a message read in one format can be written in another: see
[Getting started](02-GettingStarted.md).
