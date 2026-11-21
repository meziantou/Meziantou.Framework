---
title: Overview
description: Read and write the offline address books of Exchange and Outlook (udetails.oab), and convert their records to vCards and contacts.
---

An offline address book is the copy of an address list of Exchange that Outlook downloads to use the directory offline
([MS-OXOAB]). Outlook keeps it, uncompressed, in the file `udetails.oab` of a subfolder of
`%LOCALAPPDATA%\Microsoft\Outlook\Offline Address Books`: the version 4 Full Details format.
[OabFile](cref:Meziantou.Framework.Mail.OabFile) reads and writes this format:

<!-- ref:../../../tests/Meziantou.Framework.Mail.Tests/OabFileTests.cs;region=OabFile_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var book = OabFile.Create();
book.Header.Values[PstPropertyTag.FromValue(0x6800001F)] = "\\Global Address List";
book.Header.Values[PstPropertyTag.FromValue(0x68010003)] = 1;
var record = new OabRecord();
record.Values[PstPropertyTag.FromValue(0x3003001E)] = "/o=Example/ou=Exchange Administrative Group/cn=Recipients/cn=alice";
record.Values[PstPropertyTag.FromValue(0x39FE001F)] = "alice@example.com";
record.Values[PstPropertyTag.FromValue(0x3001001F)] = "Alice Martin";
record.Values[PstPropertyTag.FromValue(0x800F101F)] = new[] { "SMTP:alice@example.com", "smtp:a.martin@example.com" };
record.Values[PstPropertyTag.FromValue(0x0FFE0003)] = 6;
book.Records.Add(record);
book.Save(path);

var file = OabFile.Load(path);
Assert.Equal("\\Global Address List", file.Name);
var alice = file.Records.Single();
Assert.Equal("alice@example.com", alice.SmtpAddress);
Assert.Equal(["SMTP:alice@example.com", "smtp:a.martin@example.com"], alice.ProxyAddresses);

// A record as a vCard, or as a contact of Outlook
var card = VCard.FromOab(alice);
Assert.Equal("Alice Martin", card.FormattedName);
```
<!-- endref -->

- A file has two schemas: the properties of its header record
  ([HeaderProperties](cref:Meziantou.Framework.Mail.OabFile.HeaderProperties)) and of its records
  ([RecordProperties](cref:Meziantou.Framework.Mail.OabFile.RecordProperties)), with their
  [flags](cref:Meziantou.Framework.Mail.OabPropertyFlags) (primary key, ANR index).
  [OabFile.Create](cref:Meziantou.Framework.Mail.OabFile.Create) creates a file with the properties the specification
  requires.
- The [Header](cref:Meziantou.Framework.Mail.OabFile.Header) describes the address list: its
  [Name](cref:Meziantou.Framework.Mail.OabFile.Name), its distinguished name, its
  [Sequence](cref:Meziantou.Framework.Mail.OabFile.Sequence) number and its GUID. Each of the
  [Records](cref:Meziantou.Framework.Mail.OabFile.Records) is an Address Book object (a user, a contact, a distribution
  list...): its [Values](cref:Meziantou.Framework.Mail.OabRecord.Values) by property tag, and members for the common
  ones ([DisplayName](cref:Meziantou.Framework.Mail.OabRecord.DisplayName),
  [SmtpAddress](cref:Meziantou.Framework.Mail.OabRecord.SmtpAddress),
  [ProxyAddresses](cref:Meziantou.Framework.Mail.OabRecord.ProxyAddresses)...). The values are integers, booleans,
  strings (UTF-8 for the Unicode properties), binaries (the certificates) and lists of them.
- The reader checks the checksum of the file (a CRC-32); `verifyChecksum: false` reads a file whose checksum is wrong. The
  writer computes it. The format does not store the empty values: the writer does not write the empty strings, binaries
  and lists, nor the values of the object properties.
- [VCard.FromOab](cref:Meziantou.Framework.Mail.VCard.FromOab) converts a record to a [vCard](../08-ContactsAndCalendars/01-VCard.md),
  which [ToPstContactBuilder](cref:Meziantou.Framework.Mail.VCard.ToPstContactBuilder) converts to a contact of Outlook.

The files Exchange sends to Outlook are compressed with LZX, and Outlook updates its copy with differential patches:
these formats, and the versions 2 and 3 of the address books, are not supported. An address book contains the personal
data of the people of the organization: protect the files you read and write.

[MS-OXOAB]: https://learn.microsoft.com/openspecs/exchange_server_protocols/ms-oxoab
