---
title: Apple Mail (.emlx)
description: Read and write the .emlx files of Apple Mail: the message, its flags and the properties Apple Mail stores after it.
---

Apple Mail stores each message in an `.emlx` file: the size of the message on the first line, the message, then an XML
property list with its flags, its dates and the place it comes from.
[EmlxFile.Load](cref:Meziantou.Framework.Email.Storage.EmlxFile.Load) reads a file, and
[EmlxFile.Save](cref:Meziantou.Framework.Email.Storage.EmlxFile.Save) writes one, with the lines of the message ending with LF:

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/MboxTests.cs;region=EmlxFile_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var emlx = EmlxFile.Load(path);
Assert.Equal("First", emlx.Message.Subject);
Assert.True(emlx.Flags.HasFlag(EmlxFlags.Read));
Assert.Equal(new DateTimeOffset(2025, 7, 1, 8, 52, 37, TimeSpan.Zero), emlx.DateReceived);
Assert.Equal("4242", emlx.Properties["remote-id"]);

using var stream = new MemoryStream();
EmlxFile.Save(emlx.Message, stream, EmlxFlags.Read | EmlxFlags.Flagged);
```
<!-- endref -->

- [Properties](cref:Meziantou.Framework.Email.Storage.EmlxMessage.Properties) has all the properties of the property list.
  The reader ignores the DTD of the XML and does not expand its entities.
- Apple does not document the format. [Flags](cref:Meziantou.Framework.Email.Storage.EmlxMessage.Flags) reads the bits of the
  `flags` property that other tools read (read, deleted, answered, flagged, draft, forwarded...), and
  [AttachmentCount](cref:Meziantou.Framework.Email.Storage.EmlxMessage.AttachmentCount) the number of attachments in the bits 10 to
  15; [RawFlags](cref:Meziantou.Framework.Email.Storage.EmlxMessage.RawFlags) has all the bits.
- A `.partial.emlx` file is a message whose large attachments Apple Mail stores in other files: the message of the file
  does not contain them ([IsPartial](cref:Meziantou.Framework.Email.Storage.EmlxMessage.IsPartial)).
