---
title: Messages and attachments
description: Read the properties, the bodies, the recipients and the attachments of an email, and the embedded messages.
---

## Messages

A [PstMessage](cref:Meziantou.Framework.Mail.PstMessage) gives the common properties of an email: its
[Subject](cref:Meziantou.Framework.Mail.PstMessage.Subject), its sender, its dates (in UTC), its
[Flags](cref:Meziantou.Framework.Mail.PstMessage.Flags), its headers
([TransportMessageHeaders](cref:Meziantou.Framework.Mail.PstMessage.TransportMessageHeaders)) and its
[Recipients](cref:Meziantou.Framework.Mail.PstMessage.Recipients):

<!-- ref:../../tests/Meziantou.Framework.Mail.Tests/PstFileTests.cs;region=PstMessage_Read;format=md-fenced-code-block;dedent=true -->
```csharp
using var file = PstFile.Open(path);
var message = file.GetFolder("Inbox")!.EnumerateItems().First();
Assert.Equal("Welcome to Microsoft Office Outlook 2003", message.Subject);
Assert.Equal("Outlook 2003 Team", message.SenderName);
Assert.Single(message.Recipients);
Assert.HasCount(11, message.Attachments);
Assert.Contains("Outlook", message.Body, StringComparison.Ordinal);
```
<!-- endref -->

An email has up to three bodies:

- [Body](cref:Meziantou.Framework.Mail.PstMessage.Body), the plain text,
- [BodyHtml](cref:Meziantou.Framework.Mail.PstMessage.BodyHtml), decoded with the code page of the message,
- [GetRtfBody](cref:Meziantou.Framework.Mail.PstMessage.GetRtfBody), the RTF, decompressed from the compressed RTF property.
  Outlook keeps the HTML of some messages inside the RTF only.

## Attachments

[Attachments](cref:Meziantou.Framework.Mail.PstMessage.Attachments) lists the attachments.
[OpenRead](cref:Meziantou.Framework.Mail.PstAttachment.OpenRead) reads the content block by block, without loading it in
memory, and [GetContent](cref:Meziantou.Framework.Mail.PstAttachment.GetContent) returns it as bytes:

<!-- ref:../../tests/Meziantou.Framework.Mail.Tests/PstFileTests.cs;region=PstAttachment_Save;format=md-fenced-code-block;dedent=true -->
```csharp
using var file = PstFile.Open(path);
var message = file.GetFolder("Inbox")!.EnumerateItems().First();
foreach (var attachment in message.Attachments)
{
    if (attachment.Method == PstAttachmentMethod.ByValue)
    {
        // Read the content block by block, without loading it in memory
        using var content = attachment.OpenRead();
        using var output = File.Create(Path.Combine(directory.FullName, Path.GetFileName(attachment.Name ?? "attachment")));
        content.CopyTo(output);
    }
}
```
<!-- endref -->

An attachment whose [Method](cref:Meziantou.Framework.Mail.PstAttachment.Method) is
[EmbeddedMessage](cref:Meziantou.Framework.Mail.PstAttachmentMethod.EmbeddedMessage) contains a message:
[GetEmbeddedMessage](cref:Meziantou.Framework.Mail.PstAttachment.GetEmbeddedMessage) returns it, with its own recipients and
attachments. The exceptions of a recurring appointment are embedded messages too.

## Associated items

[EnumerateAssociatedItems](cref:Meziantou.Framework.Mail.PstFolder.EnumerateAssociatedItems) enumerates the hidden items of a
folder, where Outlook stores views, rules and settings.

## Search folders

The items of a search folder ([IsSearchFolder](cref:Meziantou.Framework.Mail.PstFolder.IsSearchFolder)) are the results of
its search, stored in other folders. Outlook stores the results of some search folders in a structure that [MS-PST] does
not document: these search folders have no items.
