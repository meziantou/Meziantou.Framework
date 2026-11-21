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
| `Meziantou.Framework.Email` | The common model ([EmailMessage](cref:Meziantou.Framework.Email.EmailMessage)), email messages ([MimeMessage](cref:Meziantou.Framework.Email.MimeMessage)), the exceptions |
| `Meziantou.Framework.Email.Mapi` | The Outlook items and their MAPI properties ([MapiMessage](cref:Meziantou.Framework.Email.Mapi.MapiMessage), [MapiContact](cref:Meziantou.Framework.Email.Mapi.MapiContact)...), `.msg` files ([MsgFile](cref:Meziantou.Framework.Email.Mapi.MsgFile)), TNEF streams ([TnefFile](cref:Meziantou.Framework.Email.Mapi.TnefFile)) |
| `Meziantou.Framework.Email.Storage` | The files that store items: Outlook data files ([PstFile](cref:Meziantou.Framework.Email.Storage.PstFile)), mbox, Maildir, Apple Mail, Thunderbird, offline address books |
| `Meziantou.Framework.Email.Contacts` | vCard ([VCard](cref:Meziantou.Framework.Email.Contacts.VCard)) |
| `Meziantou.Framework.Email.Calendar` | iCalendar ([VCalendar](cref:Meziantou.Framework.Email.Calendar.VCalendar)) |

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

The other headers of a message are kept in [Headers](cref:Meziantou.Framework.Email.EmailMessage.Headers). What a format
cannot store is lost when a message is converted to it: a MIME message has no RTF body, and an address without an
address part (only a name) cannot be written in a MIME header.
