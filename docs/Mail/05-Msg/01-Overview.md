---
title: Overview
description: Read an .msg file with the types of the items of Outlook data files, write one from a builder, and convert items between .msg and PST files.
---

An `.msg` file is an Outlook item saved as a file ([MS-OXMSG]): an email, a contact, an appointment, a task... in a
compound file ([MS-CFB]), the container of the documents of the old versions of Office. It stores the same MAPI
properties as an [Outlook data file](../03-Pst/01-Overview.md), so the library reads and writes it with the same types.

## Read a file

[MsgFile.Load](cref:Meziantou.Framework.Mail.MsgFile.Load) reads the item of a file, from a path, a stream or bytes. The
item is a [PstMessage](cref:Meziantou.Framework.Mail.PstMessage), or the type its class gives:
[PstContact](cref:Meziantou.Framework.Mail.PstContact) for `IPM.Contact`,
[PstAppointment](cref:Meziantou.Framework.Mail.PstAppointment) for `IPM.Appointment`,
[PstTask](cref:Meziantou.Framework.Mail.PstTask) for `IPM.Task` and
[PstDistributionList](cref:Meziantou.Framework.Mail.PstDistributionList) for `IPM.DistList`. Everything the
[pages on the items](../03-Pst/03-MessagesAndAttachments.md) describe applies: the recipients, the bodies, the
attachments and the embedded messages, the [properties](../03-Pst/05-Properties.md) and the named properties.

The file is read in memory, and its streams when they are accessed.
[PstReadOptions.MaxPropertySize](cref:Meziantou.Framework.Mail.PstReadOptions.MaxPropertySize) limits the size of a
stream, and [MaxEmbeddedMessageDepth](cref:Meziantou.Framework.Mail.PstReadOptions.MaxEmbeddedMessageDepth) the depth of
the embedded messages. A file that is not a compound file, or whose structures are malformed, throws an
[InvalidMailDataException](cref:Meziantou.Framework.Mail.InvalidMailDataException).

The strings of a file are in Unicode, or in the code page of the item in the files of old versions of Outlook (the
"ANSI" files): the code page of the message (`PidTagMessageCodepage`), else its internet code page
(`PidTagInternetCodepage`), else Windows-1252. The recipients and the attachments use the code page of their message.

## Write a file

[MsgFile.Save](cref:Meziantou.Framework.Mail.MsgFile.Save) writes the item a builder describes, with the builders of the
[writer of Outlook data files](../03-Pst/06-Writing.md):
[PstMessageBuilder](cref:Meziantou.Framework.Mail.PstMessageBuilder),
[PstContactBuilder](cref:Meziantou.Framework.Mail.PstContactBuilder),
[PstAppointmentBuilder](cref:Meziantou.Framework.Mail.PstAppointmentBuilder)... The strings are written in Unicode.

<!-- ref:../../../tests/Meziantou.Framework.Mail.Tests/MsgFileTests.cs;region=MsgFile_Save;format=md-fenced-code-block;dedent=true -->
```csharp
var message = new PstMessageBuilder
{
    Subject = "Quarterly report",
    Body = "The report is attached.",
    SenderName = "Alice",
    SenderEmailAddress = "alice@example.com",
    SenderAddressType = "SMTP",
};
message.Recipients.Add(new PstRecipientBuilder("Bob", "bob@example.com"));
message.Attachments.Add(PstAttachmentBuilder.FromContent("report.csv", "Quarter,Total\nQ1,42\n"u8.ToArray(), "text/csv"));
MsgFile.Save(message, path);

var item = MsgFile.Load(path);
Assert.Equal("Quarterly report", item.Subject);
Assert.Equal("bob@example.com", item.Recipients.Single().EmailAddress);
Assert.Equal("report.csv", item.Attachments.Single().Name);
```
<!-- endref -->

## Convert between .msg and PST files

[PstMessageBuilder.FromMessage](cref:Meziantou.Framework.Mail.PstMessageBuilder.FromMessage) copies an item, with all
its properties, recipients and attachments. The copy of an item of an Outlook data file can be saved as an `.msg` file,
and the item of an `.msg` file can be added to an Outlook data file with
[PstWriterFolder.AddItem](cref:Meziantou.Framework.Mail.PstWriterFolder.AddItem):

<!-- ref:../../../tests/Meziantou.Framework.Mail.Tests/MsgFileTests.cs;region=MsgFile_FromPst;format=md-fenced-code-block;dedent=true -->
```csharp
using (var file = PstFile.Open(pstPath))
{
    var item = file.GetFolder("Inbox")!.EnumerateItems().First();
    MsgFile.Save(PstMessageBuilder.FromMessage(item), msgPath);
}

var message = MsgFile.Load(msgPath);
Assert.Equal("Welcome to Microsoft Office Outlook 2003", message.Subject);
Assert.HasCount(11, message.Attachments);
```
<!-- endref -->

[EmailMessage.FromPst](cref:Meziantou.Framework.Mail.EmailMessage.FromPst) converts the item of an `.msg` file to the
[common model](../02-GettingStarted.md), to save it as an `.eml` file.

[MS-OXMSG]: https://learn.microsoft.com/openspecs/exchange_server_protocols/ms-oxmsg/b046868c-9fbf-41ae-9ffb-8de2bd4eec82
[MS-CFB]: https://learn.microsoft.com/openspecs/windows_protocols/ms-cfb/53989ce4-7b05-4f8d-829b-d08d6148375b
