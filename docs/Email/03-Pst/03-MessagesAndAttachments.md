---
title: Messages and attachments
description: Read the properties, the bodies, the recipients and the attachments of an email, and the embedded messages.
---

## Messages

A [MapiMessage](cref:Meziantou.Framework.Email.Mapi.MapiMessage) gives the common properties of an email: its
[Subject](cref:Meziantou.Framework.Email.Mapi.MapiMessage.Subject), its sender, its dates (in UTC), its
[Flags](cref:Meziantou.Framework.Email.Mapi.MapiMessage.Flags), its headers
([TransportMessageHeaders](cref:Meziantou.Framework.Email.Mapi.MapiMessage.TransportMessageHeaders)) and its
[Recipients](cref:Meziantou.Framework.Email.Mapi.MapiMessage.Recipients):

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/PstFileTests.cs;region=PstMessage_Read;format=md-fenced-code-block;dedent=true -->
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

- [Body](cref:Meziantou.Framework.Email.Mapi.MapiMessage.Body), the plain text,
- [BodyHtml](cref:Meziantou.Framework.Email.Mapi.MapiMessage.BodyHtml), decoded with the code page of the message,
- [GetRtfBody](cref:Meziantou.Framework.Email.Mapi.MapiMessage.GetRtfBody), the RTF, decompressed from the compressed RTF property.

### Bodies encapsulated in RTF

Outlook often keeps the HTML of a message only inside its RTF body, and the RTF of a message written from plain text
says so ([MS-OXRTFEX]). [GetHtmlBody](cref:Meziantou.Framework.Email.Mapi.MapiMessage.GetHtmlBody) returns the HTML
property, or the HTML encapsulated in the RTF when the message has no HTML property, and
[GetTextBody](cref:Meziantou.Framework.Email.Mapi.MapiMessage.GetTextBody) does the same for the plain text.
[EmailMessage.FromMapi](cref:Meziantou.Framework.Email.EmailMessage.FromMapi) uses them, so that converting such a
message to an `.eml` file keeps its HTML.

[RtfEncapsulation](cref:Meziantou.Framework.Email.Mapi.RtfEncapsulation) works on the RTF itself:
[GetFormat](cref:Meziantou.Framework.Email.Mapi.RtfEncapsulation.GetFormat) reads the header,
[Decapsulate](cref:Meziantou.Framework.Email.Mapi.RtfEncapsulation.Decapsulate) extracts the HTML or the text, and
[EncapsulateHtml](cref:Meziantou.Framework.Email.Mapi.RtfEncapsulation.EncapsulateHtml) and
[EncapsulateText](cref:Meziantou.Framework.Email.Mapi.RtfEncapsulation.EncapsulateText) write a body that
[SetRtfBody](cref:Meziantou.Framework.Email.Mapi.MapiMessageBuilder.SetRtfBody) stores in an item:

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/RtfEncapsulationTests.cs;region=RtfEncapsulation_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var rtf = RtfEncapsulation.EncapsulateHtml("<p>Hello {world}</p>\r\n");
Assert.Equal(RtfEncapsulationFormat.Html, RtfEncapsulation.GetFormat(rtf));
Assert.Equal("<p>Hello {world}</p>\r\n", RtfEncapsulation.Decapsulate(rtf));

// An RTF body that does not encapsulate another format
Assert.Equal(RtfEncapsulationFormat.None, RtfEncapsulation.GetFormat(@"{\rtf1\ansi Hello}"u8));
Assert.Null(RtfEncapsulation.Decapsulate(@"{\rtf1\ansi Hello}"u8));
```
<!-- endref -->

The reader follows the code pages of the header and of the fonts. The RTF that
[EncapsulateHtml](cref:Meziantou.Framework.Email.Mapi.RtfEncapsulation.EncapsulateHtml) writes contains only the HTML:
a reader that does not support the encapsulation shows an empty document.

[MS-OXRTFEX]: https://learn.microsoft.com/openspecs/exchange_server_protocols/ms-oxrtfex

## Attachments

[Attachments](cref:Meziantou.Framework.Email.Mapi.MapiMessage.Attachments) lists the attachments.
[OpenRead](cref:Meziantou.Framework.Email.Mapi.MapiAttachment.OpenRead) reads the content block by block, without loading it in
memory, and [GetContent](cref:Meziantou.Framework.Email.Mapi.MapiAttachment.GetContent) returns it as bytes:

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/PstFileTests.cs;region=PstAttachment_Save;format=md-fenced-code-block;dedent=true -->
```csharp
using var file = PstFile.Open(path);
var message = file.GetFolder("Inbox")!.EnumerateItems().First();
foreach (var attachment in message.Attachments)
{
    if (attachment.Method == MapiAttachmentMethod.ByValue)
    {
        // Read the content block by block, without loading it in memory
        using var content = attachment.OpenRead();
        using var output = File.Create(Path.Combine(directory.FullName, Path.GetFileName(attachment.Name ?? "attachment")));
        content.CopyTo(output);
    }
}
```
<!-- endref -->

An attachment whose [Method](cref:Meziantou.Framework.Email.Mapi.MapiAttachment.Method) is
[EmbeddedMessage](cref:Meziantou.Framework.Email.Mapi.MapiAttachmentMethod.EmbeddedMessage) contains a message:
[GetEmbeddedMessage](cref:Meziantou.Framework.Email.Mapi.MapiAttachment.GetEmbeddedMessage) returns it, with its own recipients and
attachments. The exceptions of a recurring appointment are embedded messages too.

## Associated items

[EnumerateAssociatedItems](cref:Meziantou.Framework.Email.Storage.PstFolder.EnumerateAssociatedItems) enumerates the hidden items of a
folder, where Outlook stores views, rules and settings.

## Search folders

The items of a search folder ([IsSearchFolder](cref:Meziantou.Framework.Email.Storage.PstFolder.IsSearchFolder)) are the results of
its search, stored in other folders. Outlook stores the results of some search folders in a structure that [MS-PST] does
not document: these search folders have no items.
