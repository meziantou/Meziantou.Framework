---
title: Overview
description: What the library reads and writes in Outlook data files (PST and OST).
---

The library reads and writes the data files of Outlook: the personal folders files (PST) and the offline
files (OST) of Exchange and IMAP accounts. It implements the [MS-PST] specification in C#: it needs neither Outlook nor
MAPI, and runs on every platform .NET supports.

| Format | Outlook versions | Read | Write |
| --- | --- | --- | --- |
| ANSI | 97 to 2002 | Yes | Yes |
| Unicode | 2003 and later | Yes | Yes |
| Unicode with 4 KB pages and compressed blocks (OST) | 2013 and later | Yes | Yes, for tests: Outlook does not open an OST as a data file |

The library reads:

- the folders, including the search folders and the associated (hidden) items of each folder,
- the emails: subject, sender, dates, flags, headers, recipients, and the bodies in plain text, HTML and RTF (decompressed
  from the compressed RTF property),
- the attachments, read block by block, and the embedded messages (a message attached to a message),
- the contacts, the distribution lists, the appointments with their recurrence and its exceptions, and the tasks,
- every MAPI property of every object, including the named properties.

[PstWriter](cref:Meziantou.Framework.Mail.PstWriter) creates new files with the same content. It does not modify an existing
file: to change a file, copy its items to a new file (see [Writing files](06-Writing.md)).

The library does not need the password of a protected file: Outlook asks for it, but the content of the file is not
encrypted with it (see [Formats and encryption](07-FormatsAndEncryption.md)).
