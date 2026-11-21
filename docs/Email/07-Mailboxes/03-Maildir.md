---
title: Maildir folders
description: Enumerate the messages of a Maildir folder, add messages, change their flags, and use the Maildir++ subfolders.
---

A [Maildir] folder is a directory with three subdirectories: `tmp`, where a message is written, `new`, where it is
delivered, and `cur`, where it goes once a client has seen it, with its flags at the end of the name of its file
(`1767254400.M123456P4242Q1.host:2,RS`).

[MaildirFolder.Open](cref:Meziantou.Framework.Email.Storage.MaildirFolder.Open) opens a folder and
[MaildirFolder.Create](cref:Meziantou.Framework.Email.Storage.MaildirFolder.Create) creates one:

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/MboxTests.cs;region=MaildirFolder_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var folder = MaildirFolder.Create(path);
var message = new MimeMessage { Subject = "Hello" };
var body = new MimePart();
body.SetText("Hello from a Maildir folder.");
message.Body = body;

// A message without flags goes in "new", a message with flags in "cur" with the flags in its name
var added = folder.AddMessage(message);
Assert.True(added.IsNew);
var read = folder.SetFlags(added, MaildirFlags.Seen | MaildirFlags.Replied);
Assert.EndsWith("2,RS", read.FullPath, StringComparison.Ordinal);

var sent = folder.CreateFolder("Sent");
sent.AddMessage(message, MaildirFlags.Seen);
Assert.Equal(["Sent"], folder.EnumerateFolders().Select(subfolder => subfolder.Name));
Assert.Equal("Hello", folder.EnumerateMessages().Single().Load().Subject);
```
<!-- endref -->

- [EnumerateMessages](cref:Meziantou.Framework.Email.Storage.MaildirFolder.EnumerateMessages) returns the messages of `new` and
  `cur`, with their [Key](cref:Meziantou.Framework.Email.Storage.MaildirMessage.Key) (the unique name, without the flags) and
  their [Flags](cref:Meziantou.Framework.Email.Storage.MaildirMessage.Flags).
- [AddMessage](cref:Meziantou.Framework.Email.Storage.MaildirFolder.AddMessage) writes a message in `tmp`, then moves it to
  `new`, or to `cur` when it has flags, so that a reader never sees a partial file.
- [SetFlags](cref:Meziantou.Framework.Email.Storage.MaildirFolder.SetFlags) renames the file of a message in `cur` with its new
  flags, and [Delete](cref:Meziantou.Framework.Email.Storage.MaildirFolder.Delete) deletes it.
- [EnumerateFolders](cref:Meziantou.Framework.Email.Storage.MaildirFolder.EnumerateFolders) and
  [CreateFolder](cref:Meziantou.Framework.Email.Storage.MaildirFolder.CreateFolder) follow the Maildir++ convention: a subfolder is a
  subdirectory whose name starts with a dot (`.Sent`, `.Archive.2026`).

A file name cannot contain `:` on Windows, so the folder writes `!` before the flags there
([InfoSeparator](cref:Meziantou.Framework.Email.Storage.MaildirFolder.InfoSeparator)), and reads `:`, `!` and `;`.

[Maildir]: https://cr.yp.to/proto/maildir.html
