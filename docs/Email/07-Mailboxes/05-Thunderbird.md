---
title: Thunderbird
description: Read the mbox files of the folders of Thunderbird, their .msf summaries, and other Mork databases.
---

Thunderbird stores a local folder in an mbox file (read with [MboxReader](cref:Meziantou.Framework.Email.Storage.MboxReader)), and
its summary next to it, in a file with the same name and the `.msf` extension: the headers it shows in the list of
messages, the flags, and the position of each message in the mbox file. The summary is a Mork database, a text format of
Mozilla.

## Summaries

[ThunderbirdSummary.ReadMessages](cref:Meziantou.Framework.Email.Storage.ThunderbirdSummary.ReadMessages) reads the messages of a
summary: their subject, sender, recipients, identifiers, dates, [flags](cref:Meziantou.Framework.Email.Storage.ThunderbirdMessageFlags),
size and [Offset](cref:Meziantou.Framework.Email.Storage.ThunderbirdMessageSummary.Offset) in the mbox file:

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/MboxTests.cs;region=ThunderbirdSummary_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var messages = ThunderbirdSummary.ReadMessages(path);
var first = messages[0];
Assert.Equal("First", first.Subject);
Assert.Equal("first@example.com", first.MessageId);
Assert.Equal(new DateTimeOffset(2025, 7, 1, 8, 52, 37, TimeSpan.Zero), first.Date);
Assert.True(first.Flags.HasFlag(ThunderbirdMessageFlags.Read));

// The position of the message in the mbox file of the folder
Assert.Equal(421, messages[1].Offset);
```
<!-- endref -->

Thunderbird removes `Re:` from the subjects it stores, and sets
[HasRe](cref:Meziantou.Framework.Email.Storage.ThunderbirdMessageFlags.HasRe) instead.
[ThunderbirdSummary.Create](cref:Meziantou.Framework.Email.Storage.ThunderbirdSummary.Create) writes a summary of messages, with
the columns above. Thunderbird keeps more in its summaries (threads, folder information): it rebuilds a summary that does
not match its folder, so a written summary is mostly useful to other tools.

## Mork databases

[MorkDocument](cref:Meziantou.Framework.Email.Storage.MorkDocument) reads and writes any Mork database: the summaries, and the
`.mab` address books of the old versions of Thunderbird. It has [tables](cref:Meziantou.Framework.Email.Storage.MorkTable) of
[rows](cref:Meziantou.Framework.Email.Storage.MorkRow), and the cells of a row by column name:

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/MboxTests.cs;region=MorkDocument_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var document = MorkDocument.Load(path);
var table = document.FindTable("ns:msg:db:table:kind:msgs")!;
Assert.Equal("First", table.Rows[0]["subject"]);
Assert.Equal("Café", table.Rows[1]["subject"]);
```
<!-- endref -->

The reader resolves the dictionaries of the columns and of the values, and applies the transactions of the file (the
groups `@$${...}@` that Thunderbird appends when it updates a summary); it ignores the aborted ones. The values are decoded
as UTF-8. The writer writes a dictionary of the names of the columns and of the scopes, then the tables and the rows.
