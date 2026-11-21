---
title: Getting started
description: Install the package, and convert a message between two formats with the EmailMessage model.
---

## Install the package

Add the `Meziantou.Framework.Email` package to a project:

<InstallNuGetPackage Id="Meziantou.Framework.Email" />

The package does not send email: it reads and writes files. Its types are in these namespaces:

| Namespace | Types |
| --- | --- |
| [Meziantou.Framework.Email](cref:Meziantou.Framework.Email) | The common model ([EmailMessage](cref:Meziantou.Framework.Email.EmailMessage)), email messages ([MimeMessage](cref:Meziantou.Framework.Email.MimeMessage)), the exceptions |
| [Meziantou.Framework.Email.Mapi](cref:Meziantou.Framework.Email.Mapi) | The Outlook items and their MAPI properties ([MapiMessage](cref:Meziantou.Framework.Email.Mapi.MapiMessage), [MapiContact](cref:Meziantou.Framework.Email.Mapi.MapiContact)...), `.msg` files ([MsgFile](cref:Meziantou.Framework.Email.Mapi.MsgFile)), TNEF streams ([TnefFile](cref:Meziantou.Framework.Email.Mapi.TnefFile)) |
| [Meziantou.Framework.Email.Storage](cref:Meziantou.Framework.Email.Storage) | The files that store items: Outlook data files ([PstFile](cref:Meziantou.Framework.Email.Storage.PstFile)), mbox, Maildir, Apple Mail, Thunderbird, offline address books |
| [Meziantou.Framework.Email.Contacts](cref:Meziantou.Framework.Email.Contacts) | vCard ([VCard](cref:Meziantou.Framework.Email.Contacts.VCard)) |
| [Meziantou.Framework.Email.Calendar](cref:Meziantou.Framework.Email.Calendar) | iCalendar ([VCalendar](cref:Meziantou.Framework.Email.Calendar.VCalendar)) |

The examples of this documentation omit the `using` directives of these namespaces.

## Convert a message

[EmailMessage](cref:Meziantou.Framework.Email.EmailMessage) holds what the formats have in common: the addresses, the
subject, the dates, the identifiers of the thread, the text, HTML and RTF bodies, the attachments and the embedded
messages. [FromMapi](cref:Meziantou.Framework.Email.EmailMessage.FromMapi) and
[FromMime](cref:Meziantou.Framework.Email.EmailMessage.FromMime) create it from an item of an Outlook data file or from a
MIME message, and [ToMime](cref:Meziantou.Framework.Email.EmailMessage.ToMime) and
[ToMapiBuilder](cref:Meziantou.Framework.Email.EmailMessage.ToMapiBuilder) write it back:

<!-- ref:../../tests/Meziantou.Framework.Email.Tests/EmailMessageTests.cs;region=EmailMessage_Convert;format=md-fenced-code-block;dedent=true -->
```csharp
// An item of an Outlook data file, saved as an .eml file
using (var file = PstFile.Open(pstPath))
{
    var item = file.GetFolder("Inbox")!.EnumerateItems().First();
    EmailMessage.FromMapi(item).ToMime().WriteTo(emlPath);
}

// The .eml file, read back
var email = EmailMessage.FromMime(MimeMessage.Load(emlPath));
Assert.Equal("Welcome to Microsoft Office Outlook 2003", email.Subject);
Assert.HasCount(11, email.Attachments);
```
<!-- endref -->

In an Outlook item, the author of a message ([From](cref:Meziantou.Framework.Email.EmailMessage.From)) is the user the
message is sent on behalf of
([SentRepresentingName](cref:Meziantou.Framework.Email.Mapi.MapiMessage.SentRepresentingName)), the
[Sender](cref:Meziantou.Framework.Email.EmailMessage.Sender) is the user who sent it
([SenderName](cref:Meziantou.Framework.Email.Mapi.MapiMessage.SenderName)), and the
[ReplyTo](cref:Meziantou.Framework.Email.EmailMessage.ReplyTo) addresses are the
[ReplyRecipients](cref:Meziantou.Framework.Email.Mapi.MapiMessage.ReplyRecipients). An item that has no
represented user, or the same user as its sender, has no Sender.

The other headers of a message are kept in [Headers](cref:Meziantou.Framework.Email.EmailMessage.Headers). What a format
cannot store is lost when a message is converted to it: a MIME message has no RTF body, and an address without an
address part (only a name) cannot be written in a MIME header.

## Detect the format of a file

[EmailFormatDetector.Detect](cref:Meziantou.Framework.Email.EmailFormatDetector.Detect) returns the
[format](cref:Meziantou.Framework.Email.EmailFileFormat) of a file, of a stream or of bytes, from their content: the
signatures of the binary formats (Outlook data files, `.msg`, TNEF, offline address books) and the first lines of the
text formats (`.eml`, `.emlx`, mbox, Mork, vCard, iCalendar). A folder that has the `cur`, `new` and `tmp` subfolders is
a Maildir folder.

<!-- ref:../../tests/Meziantou.Framework.Email.Tests/EmailFormatDetectorTests.cs;region=EmailFormatDetector_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var format = EmailFormatDetector.Detect(path);
Assert.Equal(EmailFileFormat.Msg, format);

Assert.Equal(EmailFileFormat.VCard, EmailFormatDetector.Detect("BEGIN:VCARD\r\nVERSION:4.0\r\nFN:Alice\r\nEND:VCARD\r\n"u8));
Assert.Equal(EmailFileFormat.Eml, EmailFormatDetector.Detect("From: alice@example.com\r\nSubject: Hello\r\n\r\nHi"u8));
Assert.Equal(EmailFileFormat.Unknown, EmailFormatDetector.Detect("Hello world"u8));
```
<!-- endref -->

A compound file is an Outlook item when it has the property stream of an item, so detecting an `.msg` file reads the
whole file. The other formats are detected from the first 64 KB. An email message is detected from its header fields:
the content must start with header fields, one of them a usual field of a message (`From`, `Date`, `Subject`,
`Received`...).
