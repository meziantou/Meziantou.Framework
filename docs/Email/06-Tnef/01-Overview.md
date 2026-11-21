---
title: Overview
description: Read a winmail.dat file with the types of the items of Outlook data files, write one from a builder, and expand the winmail.dat attachments of MIME messages.
---

Outlook and Exchange can send a message with its rich content (formatted body, attachments, Outlook properties) in a
`winmail.dat` attachment of type `application/ms-tnef`: a TNEF stream ([MS-OXTNEF]). Other email clients show the
attachment instead of its content. A TNEF stream stores the MAPI properties of an item, as an
[Outlook data file](../03-Pst/01-Overview.md) or an [.msg file](../05-Msg/01-Overview.md) does, so the library reads and
writes it with the same types.

## Read a stream

[TnefFile.Load](cref:Meziantou.Framework.Email.Mapi.TnefFile.Load) reads the item of a stream, from a path, a stream or bytes.
The item is a [MapiMessage](cref:Meziantou.Framework.Email.Mapi.MapiMessage), or the type its class gives (a
[MapiContact](cref:Meziantou.Framework.Email.Mapi.MapiContact), a [MapiAppointment](cref:Meziantou.Framework.Email.Mapi.MapiAppointment)...),
with its recipients, its attachments and its embedded messages:

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/TnefFileTests.cs;region=TnefFile_Load;format=md-fenced-code-block;dedent=true -->
```csharp
var message = TnefFile.Load(path);

// The class of the old versions of Microsoft Mail (IPM.Microsoft Mail.Note) is read as its MAPI class
Assert.Equal("IPM.Note", message.MessageClass);
foreach (var attachment in message.Attachments)
{
    File.WriteAllBytes(Path.Combine(directory.FullName, Path.GetFileName(attachment.Name ?? "attachment")), attachment.GetContent());
}
```
<!-- endref -->

- The MAPI properties come from the attributes `attMsgProps`, `attRecipTable` and `attAttachment`. The attributes of the
  old versions of Microsoft Mail (`attSubject`, `attBody`, `attAttachTitle`, `attAttachData`, `attFrom`, the dates and the
  priority) give the properties the stream does not have, and the class `IPM.Microsoft Mail.Note` is read as `IPM.Note`.
- The 8-bit strings are in the code page of the stream (`attOemCodepage`).
- [PstReadOptions.VerifyChecksums](cref:Meziantou.Framework.Email.Storage.PstReadOptions.VerifyChecksums) checks the checksum of
  each attribute; some writers compute them wrong, so set it to `false` to read their streams.
  [MaxPropertySize](cref:Meziantou.Framework.Email.Storage.PstReadOptions.MaxPropertySize) limits the size of a property, and
  [MaxEmbeddedMessageDepth](cref:Meziantou.Framework.Email.Storage.PstReadOptions.MaxEmbeddedMessageDepth) the depth of the
  embedded messages. A malformed stream throws an
  [InvalidEmailDataException](cref:Meziantou.Framework.Email.InvalidEmailDataException).

## Write a stream

[TnefFile.Save](cref:Meziantou.Framework.Email.Mapi.TnefFile.Save) and
[TnefFile.ToByteArray](cref:Meziantou.Framework.Email.Mapi.TnefFile.ToByteArray) write the item a builder describes, with the
builders of the [writer of Outlook data files](../03-Pst/06-Writing.md). The properties are written in `attMsgProps`,
`attRecipTable` and `attAttachment`, with Unicode strings, and the class in `attMessageClass`:

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/TnefFileTests.cs;region=TnefFile_Save;format=md-fenced-code-block;dedent=true -->
```csharp
var message = new MapiMessageBuilder
{
    Subject = "Meeting notes",
    Body = "The notes are attached.",
    SenderName = "Alice",
    SenderEmailAddress = "alice@example.com",
    SenderAddressType = "SMTP",
};
message.Recipients.Add(new MapiRecipientBuilder("Bob", "bob@example.com"));
message.Attachments.Add(MapiAttachmentBuilder.FromContent("notes.txt", "Notes"u8.ToArray(), "text/plain"));
var winmail = TnefFile.ToByteArray(message);

var item = TnefFile.Load(winmail);
Assert.Equal("Meeting notes", item.Subject);
Assert.Equal("notes.txt", item.Attachments.Single().Name);
```
<!-- endref -->

[MapiMessageBuilder.FromMessage](cref:Meziantou.Framework.Email.Mapi.MapiMessageBuilder.FromMessage) copies an item of any of
these formats, so an item converts between TNEF, `.msg` and Outlook data files. The copy converts the 8-bit strings to
Unicode, with the code page of the item they come from.

## winmail.dat attachments of MIME messages

[EmailMessage.FromMime](cref:Meziantou.Framework.Email.EmailMessage.FromMime) replaces a `winmail.dat` attachment
(`application/ms-tnef` or `application/vnd.ms-tnef`) by the attachments of the item it contains, and takes the bodies
of the item when the message has none. A malformed stream is kept as an attachment:

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/TnefFileTests.cs;region=EmailMessage_Tnef;format=md-fenced-code-block;dedent=true -->
```csharp
// The winmail.dat attachment is replaced by the attachments of the Outlook item it contains
var email = EmailMessage.FromMime(MimeMessage.Load(mime.ToByteArray()));
Assert.Equal(["quick.doc", "quick.html", "quick.pdf", "quick.txt", "quick.xml"], email.Attachments.Select(attachment => attachment.FileName).Order(StringComparer.Ordinal));
```
<!-- endref -->

[MS-OXTNEF]: https://learn.microsoft.com/openspecs/exchange_server_protocols/ms-oxtnef/1f0544d7-30b7-4194-b58f-adc82f3763bb
